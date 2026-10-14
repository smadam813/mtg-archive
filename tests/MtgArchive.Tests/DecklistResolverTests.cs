using MtgArchive.Data;
using MtgArchive.Simulation;

namespace MtgArchive.Tests;

/// <summary>
/// The decklist simulation input contract: resolution through
/// the collection-import path, tier-1 acceptance of partial mid-build
/// lists, unresolved-name surfacing, and the deck-size echo.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DecklistResolverTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Oracle_id_entry_and_exact_name_entry_resolve_identically()
    {
        var oracleId = Guid.NewGuid();
        var name = await SeedAsync(TestCards.Minimal(oracleId));

        var byName = await ResolveAsync(new Decklist
        {
            Entries = [new() { Card = new() { Name = name }, Quantity = 1 }],
        });
        var byId = await ResolveAsync(new Decklist
        {
            Entries = [new() { Card = new() { OracleId = oracleId }, Quantity = 1 }],
        });

        Assert.Equal(byName.Entries, byId.Entries);
        Assert.Equal(oracleId, byName.Entries.Single().OracleId);
    }

    // Plain-text decklists get the front-face fallback the collection-import
    // composition pins for them: a bare face name resolves to its card.
    [Fact]
    public async Task Bare_face_name_resolves_through_the_plain_text_fallback()
    {
        var oracleId = Guid.NewGuid();
        var front = $"Fire {oracleId:N}";
        var card = TestCards.Minimal(oracleId);
        card.Name = $"{front} // Ice {oracleId:N}";
        card.Faces =
        [
            new CardFace { FaceIndex = 0, Name = front, TypeLine = "Instant" },
            new CardFace { FaceIndex = 1, Name = $"Ice {oracleId:N}", TypeLine = "Instant" },
        ];
        await SeedAsync(card);

        var resolved = await ResolveAsync(new Decklist
        {
            Entries = [new() { Card = new() { Name = front }, Quantity = 1 }],
        });

        Assert.Equal(oracleId, resolved.Entries.Single().OracleId);
    }

    // Tier-1 acceptance: a mid-build 40-card list with no commander is
    // first-class — no 100-card requirement, no commander requirement — and
    // the deck-size echo is the sum of entry quantities.
    [Fact]
    public async Task Mid_build_list_without_commander_is_accepted_and_deck_size_echoes_quantities()
    {
        var land = Guid.NewGuid();
        var landName = await SeedAsync(TestCards.Minimal(land));
        var spell = Guid.NewGuid();
        await SeedAsync(TestCards.Minimal(spell));

        var resolved = await ResolveAsync(new Decklist
        {
            Entries =
            [
                new() { Card = new() { Name = landName }, Quantity = 17 },
                new() { Card = new() { OracleId = spell }, Quantity = 23 },
            ],
        });

        Assert.Empty(resolved.Commanders);
        Assert.Empty(resolved.Unresolved);
        Assert.Equal(40, resolved.DeckSize);
        Assert.Equal(resolved.Entries.Sum(e => e.Quantity), resolved.DeckSize);
    }

    [Fact]
    public async Task Explicit_commanders_resolve_alongside_entries()
    {
        var commander = Guid.NewGuid();
        var commanderName = await SeedAsync(TestCards.Minimal(commander));
        var partner = Guid.NewGuid();
        await SeedAsync(TestCards.Minimal(partner));
        var spell = Guid.NewGuid();
        await SeedAsync(TestCards.Minimal(spell));

        var resolved = await ResolveAsync(new Decklist
        {
            Commanders = [new() { Name = commanderName }, new() { OracleId = partner }],
            Entries = [new() { Card = new() { OracleId = spell }, Quantity = 1 }],
        });

        Assert.Equal([commander, partner], resolved.Commanders);
    }

    // Resolution failures surface the names for the caller — the future
    // endpoint's unresolved_names problem-detail extension.
    [Fact]
    public async Task Unresolved_and_ambiguous_names_are_surfaced_to_the_caller()
    {
        var oracleId = Guid.NewGuid();
        var known = await SeedAsync(TestCards.Minimal(oracleId));
        var missing = $"No Such Card {Guid.NewGuid():N}";
        var token = $"Elemental {Guid.NewGuid():N}";
        var first = TestCards.Minimal(Guid.NewGuid());
        first.Name = token;
        await SeedAsync(first);
        var second = TestCards.Minimal(Guid.NewGuid());
        second.Name = token;
        await SeedAsync(second);

        var resolved = await ResolveAsync(new Decklist
        {
            Entries =
            [
                new() { Card = new() { Name = known }, Quantity = 1 },
                new() { Card = new() { Name = missing }, Quantity = 2 },
                new() { Card = new() { Name = token }, Quantity = 1 },
            ],
        });

        Assert.Equal(new[] { missing, token }.Order(),
            resolved.Unresolved.Select(u => u.Card.Name).Order());
        Assert.All(resolved.Unresolved, u => Assert.False(u.IsCommander));
        Assert.Equal(oracleId, resolved.Entries.Single().OracleId);
    }

    // A fabricated oracle_id is checked against the corpus like a name is:
    // it never inflates the deck silently.
    [Fact]
    public async Task Unknown_oracle_id_is_surfaced_not_accepted()
    {
        var known = Guid.NewGuid();
        await SeedAsync(TestCards.Minimal(known));
        var fabricated = Guid.NewGuid();

        var resolved = await ResolveAsync(new Decklist
        {
            Entries =
            [
                new() { Card = new() { OracleId = known }, Quantity = 1 },
                new() { Card = new() { OracleId = fabricated }, Quantity = 60 },
            ],
        });

        Assert.Equal(1, resolved.DeckSize);
        Assert.Equal(fabricated, resolved.Unresolved.Single().Card.OracleId);
    }

    // A commander that fails to resolve is never a silent drop: its
    // unresolved record keeps the commander role.
    [Fact]
    public async Task Dropped_commander_is_flagged_as_a_commander()
    {
        var partner = Guid.NewGuid();
        var partnerName = await SeedAsync(TestCards.Minimal(partner));
        var typo = $"Nonexistent Partner {Guid.NewGuid():N}";

        var resolved = await ResolveAsync(new Decklist
        {
            Commanders = [new() { Name = partnerName }, new() { Name = typo }],
        });

        Assert.Equal([partner], resolved.Commanders);
        var dropped = resolved.Unresolved.Single();
        Assert.True(dropped.IsCommander);
        Assert.Equal(typo, dropped.Card.Name);
    }

    [Fact]
    public async Task Non_positive_quantity_is_rejected()
        => await Assert.ThrowsAsync<ArgumentException>(() => ResolveAsync(new Decklist
        {
            Entries = [new() { Card = new() { Name = "Island" }, Quantity = 0 }],
        }));

    [Fact]
    public void Non_positive_quantity_is_rejected_before_any_lookup()
        => Assert.Throws<ArgumentException>(() => DecklistResolver.Cards(new Decklist
        {
            Entries = [new() { Card = new() { Name = "Island" }, Quantity = 0 }],
        }));

    [Fact]
    public void Duplicate_commander_is_rejected()
    {
        var id = Guid.NewGuid();
        var decklist = new Decklist
        {
            Commanders = [new() { Name = "Same" }, new() { OracleId = id }],
            Entries = [new() { Card = new() { Name = "Island" }, Quantity = 1 }],
        };

        var ex = Assert.Throws<ArgumentException>(() => DecklistResolver.Resolve(
            decklist, card => card.Name == "Island" ? Guid.NewGuid() : id));

        Assert.Contains("twice", ex.Message);
    }

    [Fact]
    public void Commander_that_is_also_a_library_entry_is_rejected()
    {
        var id = Guid.NewGuid();
        var decklist = new Decklist
        {
            Commanders = [new() { Name = "Same" }],
            Entries = [new() { Card = new() { OracleId = id }, Quantity = 1 }],
        };

        var ex = Assert.Throws<ArgumentException>(() => DecklistResolver.Resolve(decklist, _ => id));

        Assert.Contains("library entry", ex.Message);
    }

    [Fact]
    public async Task Card_with_neither_name_nor_oracle_id_is_rejected()
        => await Assert.ThrowsAsync<ArgumentException>(() => ResolveAsync(new Decklist
        {
            Entries = [new() { Card = new(), Quantity = 1 }],
        }));

    private async Task<ResolvedDecklist> ResolveAsync(Decklist decklist)
    {
        await using var db = postgres.CreateCorpusContext();
        return await new DecklistResolver(db).ResolveAsync(decklist);
    }

    private async Task<string> SeedAsync(Card card)
    {
        await using var db = postgres.CreateCorpusContext();
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return card.Name;
    }
}
