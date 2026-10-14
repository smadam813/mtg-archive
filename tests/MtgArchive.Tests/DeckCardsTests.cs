using MtgArchive.Data;
using MtgArchive.Simulation;

namespace MtgArchive.Tests;

/// <summary>
/// The tier-1 deck-card load: mana derivation reads stored
/// source profiles only — a fetch's Oracle text is never re-parsed at query
/// time — and fetch colors resolve against the deck's lands.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DeckCardsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Fetch_resolves_from_its_stored_profile_not_its_text()
    {
        var fetch = TestCards.Minimal(Guid.NewGuid());
        fetch.TypeLine = "Land";
        fetch.CardTypes = ["Land"];
        fetch.OracleText = "{T}, Sacrifice this land: Search your library for a Forest or Plains card, "
            + "put it onto the battlefield, then shuffle.";
        var forest = TestCards.Minimal(Guid.NewGuid());
        forest.TypeLine = "Basic Land — Forest";
        forest.Supertypes = ["Basic"];
        forest.CardTypes = ["Land"];
        forest.Subtypes = ["Forest"];
        var commander = TestCards.Minimal(Guid.NewGuid());
        commander.ManaCost = "{1}{G}{G}";
        commander.ManaValue = 3;
        commander.TypeLine = "Legendary Creature — Elf";
        commander.CardTypes = ["Creature"];

        await using (var db = postgres.CreateCorpusContext())
        {
            db.Cards.AddRange(fetch, forest, commander);
            await db.SaveChangesAsync();
            db.SourceProfiles.Add(new SourceProfile { CardId = forest.Id, ProducedColors = ["G"] });
            await db.SaveChangesAsync();
        }

        var decklist = new ResolvedDecklist
        {
            Commanders = [commander.OracleId],
            Entries = [new(fetch.OracleId, 1), new(forest.OracleId, 5), new(forest.OracleId, 5)],
        };

        await using (var db = postgres.CreateCorpusContext())
        {
            var withoutProfile = ManaSourceTable.Build(await DeckCards.LoadAsync(db, decklist));
            Assert.Equal(forest.OracleId, Assert.Single(withoutProfile.Sources).OracleId);
            Assert.Equal(10, withoutProfile.Count("G"));

            db.SourceProfiles.Add(new SourceProfile
            {
                CardId = fetch.Id,
                FetchLandTypes = ["Forest", "Plains"],
                FetchActivation = FetchActivation.Untapped,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = postgres.CreateCorpusContext())
        {
            var cards = await DeckCards.LoadAsync(db, decklist);
            var table = ManaSourceTable.Build(cards);

            Assert.Equal(["G"], table.Sources.Single(s => s.OracleId == fetch.OracleId).Colors);
            Assert.Equal(11, table.Count("G"));
            Assert.Equal(FetchActivation.Untapped, cards.Single(c => c.OracleId == fetch.OracleId).FetchActivation);
            var demand = Assert.Single(PipDemand.Extract(cards));
            Assert.True(demand.IsCommander);
            Assert.Equal(2, demand.Pips["G"]);
        }
    }

    // The combined type columns union every face, so a spell // land MDFC
    // is judged on its front face: a spell that demands pips, not a land.
    [Fact]
    public async Task Spell_front_of_a_modal_double_faced_card_is_a_spell_not_a_land()
    {
        var mdfc = TestCards.Minimal(Guid.NewGuid());
        mdfc.TypeLine = "Instant // Land";
        mdfc.CardTypes = ["Instant", "Land"];
        mdfc.ManaValue = 3;
        mdfc.Faces =
        [
            new CardFace { FaceIndex = 0, Name = "Front", TypeLine = "Instant", ManaCost = "{1}{U}{U}" },
            new CardFace { FaceIndex = 1, Name = "Back", TypeLine = "Land" },
        ];
        await using var db = postgres.CreateCorpusContext();
        db.Cards.Add(mdfc);
        await db.SaveChangesAsync();

        var cards = await DeckCards.LoadAsync(db, new ResolvedDecklist { Entries = [new(mdfc.OracleId, 1)] });

        var card = Assert.Single(cards);
        Assert.False(card.IsLand);
        Assert.False(card.IsPermanent);
        Assert.Equal(2, Assert.Single(PipDemand.Extract(cards)).Pips["U"]);
    }

    // Only a modal_dfc carries a back face; tier-1 reads the front face
    // either way, so the pip demand does not change.
    [Theory]
    [InlineData("modal_dfc", true)]
    [InlineData("transform", false)]
    public async Task Only_a_modal_double_faced_card_carries_its_back_face(string layout, bool hasBackFace)
    {
        var card = TestCards.Minimal(Guid.NewGuid());
        card.Name = "Esika, God of the Tree // The Prismatic Bridge";
        card.Layout = layout;
        card.TypeLine = "Legendary Creature — God // Legendary Enchantment";
        card.CardTypes = ["Creature", "Enchantment"];
        card.ManaValue = 3;
        card.Faces =
        [
            new CardFace { FaceIndex = 0, Name = "Esika, God of the Tree", TypeLine = "Legendary Creature — God", ManaCost = "{1}{G}{G}", ManaValue = 3 },
            new CardFace { FaceIndex = 1, Name = "The Prismatic Bridge", TypeLine = "Legendary Enchantment", ManaCost = "{W}{U}{B}{R}{G}", ManaValue = 5 },
        ];
        await using var db = postgres.CreateCorpusContext();
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        var cards = await DeckCards.LoadAsync(db, new ResolvedDecklist { Commanders = [card.OracleId] });

        var esika = Assert.Single(cards);
        Assert.Equal("Esika, God of the Tree", esika.FrontFaceName);
        Assert.Equal(2, Assert.Single(PipDemand.Extract(cards)).Pips["G"]);
        if (!hasBackFace)
        {
            Assert.Null(esika.BackFace);
            return;
        }

        var back = esika.AsBackFace()!;
        Assert.Equal("The Prismatic Bridge", back.Name);
        Assert.Equal("{W}{U}{B}{R}{G}", back.ManaCost);
        Assert.Equal(5, back.ManaValue);
        Assert.Equal(["Enchantment"], back.CardTypes);
        Assert.Equal(["Legendary"], back.Supertypes);
        Assert.True(back.IsPermanent);
        Assert.True(back.IsCommander);
        Assert.Null(back.ProducedColors);
        Assert.Same(esika, back.Whole());
    }

    // The stored identity-bound facet and the commander's color
    // identity clip Command Tower to the deck's color identity.
    [Fact]
    public async Task Identity_bound_profile_clips_to_the_commanders_identity()
    {
        var tower = TestCards.Minimal(Guid.NewGuid());
        tower.Name = "Command Tower";
        tower.TypeLine = "Land";
        tower.CardTypes = ["Land"];
        var commander = TestCards.Minimal(Guid.NewGuid(), "W", "U", "B");
        commander.TypeLine = "Legendary Creature — Human";
        commander.CardTypes = ["Creature"];
        await using var db = postgres.CreateCorpusContext();
        db.Cards.AddRange(tower, commander);
        await db.SaveChangesAsync();
        db.SourceProfiles.Add(new SourceProfile { CardId = tower.Id, ProducedColors = ["B", "G", "R", "U", "W"], IdentityBound = true });
        await db.SaveChangesAsync();

        var table = ManaSourceTable.Build(await DeckCards.LoadAsync(db, new ResolvedDecklist
        {
            Commanders = [commander.OracleId],
            Entries = [new(tower.OracleId, 1)],
        }));

        var source = Assert.Single(table.Sources);
        Assert.Equal(["W", "U", "B"], source.Colors);
        Assert.Equal(ColorSpread.ThreePlus, source.Spread);
    }

    // With a land face, each face reads its own colors, not the profile's union.
    [Fact]
    public async Task Each_face_of_a_modal_card_with_a_land_face_makes_its_own_colors()
    {
        var shatterskull = ModalCard(
            "Shatterskull Smashing", "Sorcery", "{X}{R}{R}", null,
            "Shatterskull, the Hammer Pass", ["R"]);
        var pathway = ModalCard(
            "Brightclimb Pathway", "Land", null, ["W"],
            "Grimclimb Pathway", ["B"]);
        // No ingest has filled the face colors yet: the land face keeps the union.
        var emeria = ModalCard(
            "Emeria's Call", "Sorcery", "{4}{W}{W}{W}", null,
            "Emeria, Shattered Skyclave", null);
        await using var db = postgres.CreateCorpusContext();
        db.Cards.AddRange(shatterskull, pathway, emeria);
        await db.SaveChangesAsync();
        db.SourceProfiles.AddRange(
            new SourceProfile { CardId = shatterskull.Id, ProducedColors = ["R"] },
            new SourceProfile { CardId = pathway.Id, ProducedColors = ["W", "B"] },
            new SourceProfile { CardId = emeria.Id, ProducedColors = ["W"] });
        await db.SaveChangesAsync();

        var cards = await DeckCards.LoadAsync(db, new ResolvedDecklist
        {
            Entries = [new(shatterskull.OracleId, 1), new(pathway.OracleId, 1), new(emeria.OracleId, 1)],
        });

        var unfilled = Assert.Single(cards, c => c.OracleId == emeria.OracleId);
        Assert.Null(unfilled.ProducedColors);
        Assert.Equal(["W"], unfilled.AsBackFace()!.ProducedColors);

        var spell = Assert.Single(cards, c => c.OracleId == shatterskull.OracleId);
        Assert.True(spell.IsMdfcLand);
        Assert.Null(spell.ProducedColors);
        Assert.Equal(["R"], spell.AsBackFace()!.ProducedColors);
        var land = Assert.Single(cards, c => c.OracleId == pathway.OracleId);
        Assert.True(land.IsPathway);
        Assert.Equal(["W"], land.ProducedColors);
        Assert.Equal(["B"], land.AsBackFace()!.ProducedColors);
    }

    private static Card ModalCard(string front, string frontType, string? frontCost, string[]? frontColors, string back, string[]? backColors)
    {
        var card = TestCards.Minimal(Guid.NewGuid());
        card.Name = $"{front} // {back}";
        card.Layout = "modal_dfc";
        card.TypeLine = $"{frontType} // Land";
        card.CardTypes = [frontType, "Land"];
        card.Faces =
        [
            new CardFace { FaceIndex = 0, Name = front, TypeLine = frontType, ManaCost = frontCost, ProducedColors = frontColors },
            new CardFace { FaceIndex = 1, Name = back, TypeLine = "Land", ProducedColors = backColors },
        ];
        return card;
    }
}
