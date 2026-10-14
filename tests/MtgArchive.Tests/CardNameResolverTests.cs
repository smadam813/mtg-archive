using MtgArchive.CollectionImport;
using MtgArchive.Data;

namespace MtgArchive.Tests;

/// <summary>
/// Name-to-oracle_id resolution against the shared corpus
/// database. Exact NFC name match is the sole strategy for ManaBox rows;
/// plain-text rows additionally get the pinned front-face fallback. Seeded
/// names carry a unique suffix so the shared database never accumulates
/// accidental namesakes across tests — ambiguity is always seeded on
/// purpose.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CardNameResolverTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Exact_name_resolves_to_the_single_oracle_id()
    {
        var oracleId = Guid.NewGuid();
        var name = await SeedAsync(TestCards.Minimal(oracleId));

        var resolution = await ResolveAsync(name);

        Assert.Equal(NameResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal(oracleId, resolution.OracleId);
    }

    [Fact]
    public async Task Unknown_name_is_unresolved()
    {
        var resolution = await ResolveAsync($"No Such Card {Guid.NewGuid():N}");

        Assert.Equal(NameResolutionOutcome.Unresolved, resolution.Outcome);
        Assert.Null(resolution.OracleId);
    }

    // 222 names in the 2026-08-24 Oracle Cards bulk are ambiguous, nearly
    // all tokens (Elemental ×31) — and ManaBox does export tokens.
    [Fact]
    public async Task Token_name_on_multiple_oracle_identities_is_ambiguous_never_pick_first()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var name = await SeedAsync(Named(first, $"Elemental {first:N}", "token"));
        await SeedAsync(Named(second, name, "token"));

        var resolution = await ResolveAsync(name);

        Assert.Equal(NameResolutionOutcome.Ambiguous, resolution.Outcome);
        Assert.Null(resolution.OracleId);
        Assert.Equal(new[] { first, second }.Order(), resolution.CandidateOracleIds.Order());
    }

    // An earlier import lost Llanowar Elves to its token namesake.
    [Fact]
    public async Task A_real_card_wins_over_its_token_namesake()
    {
        var elves = Guid.NewGuid();
        var token = Guid.NewGuid();
        var name = await SeedAsync(Named(elves, $"Llanowar Elves {elves:N}", "normal"));
        await SeedAsync(Named(token, name, "token"));

        var resolution = await ResolveAsync(name);

        Assert.Equal(NameResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal(elves, resolution.OracleId);
    }

    [Fact]
    public async Task Two_real_cards_and_a_token_stay_ambiguous_over_the_real_cards_only()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var name = await SeedAsync(Named(first, $"Un-set Twin {first:N}", "normal"));
        await SeedAsync(Named(second, name, "normal"));
        await SeedAsync(Named(Guid.NewGuid(), name, "token"));

        var resolution = await ResolveAsync(name);

        Assert.Equal(NameResolutionOutcome.Ambiguous, resolution.Outcome);
        Assert.Equal(new[] { first, second }.Order(), resolution.CandidateOracleIds.Order());
    }

    // A null layout counts as non-token, so a corpus that has not
    // re-ingested since the layout column arrived stays ambiguous.
    [Fact]
    public async Task A_real_card_and_a_token_without_layouts_stay_ambiguous()
    {
        var real = Guid.NewGuid();
        var token = Guid.NewGuid();
        var name = await SeedAsync(Named(real, $"Unlaid {real:N}"));
        await SeedAsync(Named(token, name));

        var resolution = await ResolveAsync(name);

        Assert.Equal(NameResolutionOutcome.Ambiguous, resolution.Outcome);
        Assert.Equal(new[] { real, token }.Order(), resolution.CandidateOracleIds.Order());
    }

    [Fact]
    public async Task Face_name_fallback_prefers_the_real_card_over_a_token_face()
    {
        var real = Guid.NewGuid();
        var token = Guid.NewGuid();
        var face = $"Fire {Guid.NewGuid():N}";
        var realCard = SplitCard(real, face, $"Ice {real:N}");
        realCard.Layout = "split";
        await SeedAsync(realCard);
        var tokenCard = SplitCard(token, face, $"Smoke {token:N}");
        tokenCard.Layout = "double_faced_token";
        await SeedAsync(tokenCard);

        var resolution = await ResolveAsync(face, faceNameFallback: true);

        Assert.Equal(NameResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal(real, resolution.OracleId);
    }

    // The full Scryfall name is the same Front // Back shape for split and
    // adventure (and transform, MDFC) alike — one exact match covers all.
    [Theory]
    [InlineData("Fire", "Ice")]
    [InlineData("Brazen Borrower", "Petty Theft")]
    public async Task Full_multi_face_name_resolves_by_exact_match(string front, string back)
    {
        var oracleId = Guid.NewGuid();
        var suffix = $"{oracleId:N}";
        await SeedAsync(SplitCard(oracleId, $"{front} {suffix}", $"{back} {suffix}"));

        var resolution = await ResolveAsync($"{front} {suffix} // {back} {suffix}");

        Assert.Equal(NameResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal(oracleId, resolution.OracleId);
    }

    // Pinned product decision: a plain-text bare face name falls back to its
    // full-name card; a ManaBox row never does.
    [Fact]
    public async Task Bare_face_name_falls_back_only_for_plain_text_rows()
    {
        var oracleId = Guid.NewGuid();
        var suffix = $"{oracleId:N}";
        await SeedAsync(SplitCard(oracleId, $"Fire {suffix}", $"Ice {suffix}"));

        var decklist = await ResolveAsync($"Fire {suffix}", faceNameFallback: true);
        Assert.Equal(NameResolutionOutcome.Resolved, decklist.Outcome);
        Assert.Equal(oracleId, decklist.OracleId);

        var manaBox = await ResolveAsync($"Fire {suffix}", faceNameFallback: false);
        Assert.Equal(NameResolutionOutcome.Unresolved, manaBox.Outcome);
    }

    [Fact]
    public async Task Ambiguous_face_name_fallback_quarantines_instead_of_guessing()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var face = $"Fire {Guid.NewGuid():N}";
        await SeedAsync(SplitCard(first, face, $"Ice {first:N}"));
        await SeedAsync(SplitCard(second, face, $"Smoke {second:N}"));

        var resolution = await ResolveAsync(face, faceNameFallback: true);

        Assert.Equal(NameResolutionOutcome.Ambiguous, resolution.Outcome);
        Assert.Equal(new[] { first, second }.Order(), resolution.CandidateOracleIds.Order());
    }

    [Fact]
    public async Task Exact_full_name_match_wins_before_any_face_fallback()
    {
        var whole = Guid.NewGuid();
        var split = Guid.NewGuid();
        // A card whose full name equals another card's face name — the
        // exact whole-name match must win without consulting faces.
        var name = await SeedAsync(Named(whole, $"Fire {whole:N}"));
        await SeedAsync(SplitCard(split, name, $"Ice {split:N}"));

        var resolution = await ResolveAsync(name, faceNameFallback: true);

        Assert.Equal(NameResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal(whole, resolution.OracleId);
    }

    // The corpus is NFC; a decomposed spelling of the same name must match.
    [Fact]
    public async Task Non_ascii_names_match_after_nfc_normalization()
    {
        var oracleId = Guid.NewGuid();
        var name = await SeedAsync(Named(oracleId, $"Bespoke Bō {oracleId:N}"));

        var decomposed = name.Replace("\u014D", "o\u0304");
        Assert.NotEqual(name, decomposed);
        var resolution = await ResolveAsync(decomposed);

        Assert.Equal(NameResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal(oracleId, resolution.OracleId);
    }

    [Fact]
    public async Task Un_set_oddball_names_resolve_under_exactly_those_names()
    {
        var oracleId = Guid.NewGuid();
        var name = await SeedAsync(Named(oracleId, $"_____ _____ _____ Trespasser {oracleId:N}"));

        var resolution = await ResolveAsync(name);

        Assert.Equal(NameResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal(oracleId, resolution.OracleId);
    }

    // Stale cards are never deleted, so collection rows never dangle;
    // resolving to one at import time is valid, and the ingest report — not
    // the import — surfaces such rows.
    [Fact]
    public async Task Stale_card_resolves_and_a_live_namesake_wins_over_it()
    {
        var staleOnly = Guid.NewGuid();
        var stale = Named(staleOnly, $"Stale Only {staleOnly:N}");
        stale.Stale = true;
        var staleName = await SeedAsync(stale);

        var resolution = await ResolveAsync(staleName);
        Assert.Equal(NameResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal(staleOnly, resolution.OracleId);

        // Scryfall oracle_id migrations leave a stale namesake behind; the
        // live row wins, the same rule as GET /cards?name=.
        var migratedStale = Guid.NewGuid();
        var live = Guid.NewGuid();
        var old = Named(migratedStale, $"Migrated {live:N}");
        old.Stale = true;
        var sharedName = await SeedAsync(old);
        await SeedAsync(Named(live, sharedName));

        resolution = await ResolveAsync(sharedName);
        Assert.Equal(NameResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal(live, resolution.OracleId);
    }

    // The CSV's Scryfall ID is the English printing-level id and misses the
    // oracle-level corpus by design — a parsed row resolves by name alone,
    // whatever its Scryfall ID column says.
    [Fact]
    public async Task Scryfall_id_is_never_a_resolution_key()
    {
        var oracleId = Guid.NewGuid();
        var name = await SeedAsync(TestCards.Minimal(oracleId));

        var rows = ManaBoxCsvParser.Parse(new StringReader(
            $"Name,Scryfall ID,Quantity\n{name},{Guid.NewGuid()},1\n"));
        var resolution = await ResolveAsync(rows[0].Name);

        Assert.Equal(NameResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal(oracleId, resolution.OracleId);
    }

    [Fact]
    public async Task Batch_resolution_classifies_every_name_in_one_pass()
    {
        var resolved = Guid.NewGuid();
        var name = await SeedAsync(TestCards.Minimal(resolved));
        var missing = $"No Such Card {Guid.NewGuid():N}";

        await using var db = postgres.CreateCorpusContext();
        var resolutions = await new CardNameResolver(db)
            .ResolveAsync([name, missing], faceNameFallback: false);

        Assert.Equal(2, resolutions.Count);
        Assert.Equal(resolved, resolutions[name].OracleId);
        Assert.Equal(NameResolutionOutcome.Unresolved, resolutions[missing].Outcome);
    }

    private async Task<NameResolution> ResolveAsync(string name, bool faceNameFallback = false)
    {
        await using var db = postgres.CreateCorpusContext();
        return await new CardNameResolver(db).ResolveAsync(name, faceNameFallback);
    }

    private async Task<string> SeedAsync(Card card)
    {
        await using var db = postgres.CreateCorpusContext();
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return card.Name;
    }

    private static Card Named(Guid oracleId, string name, string? layout = null)
    {
        var card = TestCards.Minimal(oracleId);
        card.Name = name;
        card.Layout = layout;
        return card;
    }

    private static Card SplitCard(Guid oracleId, string front, string back)
    {
        var card = Named(oracleId, $"{front} // {back}");
        card.Faces =
        [
            new CardFace { FaceIndex = 0, Name = front, TypeLine = "Instant" },
            new CardFace { FaceIndex = 1, Name = back, TypeLine = "Instant" },
        ];
        return card;
    }
}
