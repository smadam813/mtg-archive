using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;
using Npgsql;

namespace MtgArchive.Tests;

/// <summary>
/// Corpus card schema. The fixture applies the migrations to a
/// fresh pgvector/pgvector:pg18 database; the tests exercise the resulting
/// schema through the EF model and raw SQL.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CorpusSchemaTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Card_roundtrips_every_typed_column_and_the_latest_raw_record()
    {
        var oracleId = Guid.NewGuid();
        await using (var db = fixture.CreateCorpusContext())
        {
            db.Cards.Add(new Card
            {
                OracleId = oracleId,
                Name = "Halana and Alena, Partners",
                ManaCost = "{2}{R}{G}",
                ManaValue = 4m,
                TypeLine = "Legendary Creature — Human Ranger",
                Supertypes = ["Legendary"],
                CardTypes = ["Creature"],
                Subtypes = ["Human", "Ranger"],
                OracleText = "First strike, reach\nFriends forever",
                Colors = ["R", "G"],
                ColorIdentity = ["R", "G"],
                Keywords = ["First strike", "Reach", "Friends forever"],
                Power = "2",
                Toughness = "3",
                ProducedMana = null,
                GameChanger = false,
                EdhrecRank = 4321,
                ReleasedAt = new DateOnly(2021, 11, 19),
                Raw = JsonDocument.Parse("""{"name":"Halana and Alena, Partners"}"""),
                CanBeCommander = true,
                HasFriendsForever = true,
                CopyLimit = 1,
            });
            await db.SaveChangesAsync();
        }

        await using var reader = fixture.CreateCorpusContext();
        var card = await reader.Cards.SingleAsync(c => c.OracleId == oracleId);

        Assert.Equal("Halana and Alena, Partners", card.Name);
        Assert.Equal("{2}{R}{G}", card.ManaCost);
        Assert.Equal(4m, card.ManaValue);
        Assert.Equal("Legendary Creature — Human Ranger", card.TypeLine);
        Assert.Equal(["Legendary"], card.Supertypes);
        Assert.Equal(["Creature"], card.CardTypes);
        Assert.Equal(["Human", "Ranger"], card.Subtypes);
        Assert.Equal("First strike, reach\nFriends forever", card.OracleText);
        Assert.Equal(["R", "G"], card.Colors);
        Assert.Equal(["R", "G"], card.ColorIdentity);
        Assert.Equal(["First strike", "Reach", "Friends forever"], card.Keywords);
        Assert.Equal("2", card.Power);
        Assert.Equal("3", card.Toughness);
        Assert.Null(card.Loyalty);
        Assert.Null(card.ProducedMana);
        Assert.False(card.GameChanger);
        Assert.Equal(4321, card.EdhrecRank);
        Assert.Equal(new DateOnly(2021, 11, 19), card.ReleasedAt);
        Assert.Equal("Halana and Alena, Partners", card.Raw.RootElement.GetProperty("name").GetString());
        Assert.True(card.CanBeCommander);
        Assert.False(card.HasPartner);
        Assert.False(card.HasPartnerWith);
        Assert.True(card.HasFriendsForever);
        Assert.False(card.HasBackground);
        Assert.False(card.HasDoctorsCompanion);
        Assert.Equal(1, card.CopyLimit);
    }

    // W=16, U=8, B=4, R=2, G=1 — the mask reads as WUBRG binary.
    [Theory]
    [InlineData(new string[0], 0)]
    [InlineData(new[] { "W" }, 16)]
    [InlineData(new[] { "U" }, 8)]
    [InlineData(new[] { "B" }, 4)]
    [InlineData(new[] { "R" }, 2)]
    [InlineData(new[] { "G" }, 1)]
    [InlineData(new[] { "W", "U" }, 24)]
    [InlineData(new[] { "U", "B", "R" }, 14)]
    [InlineData(new[] { "W", "U", "B", "R", "G" }, 31)]
    public async Task Color_identity_bitmask_is_generated_from_the_readable_array(string[] identity, int mask)
    {
        await using var db = fixture.CreateCorpusContext();
        var card = TestCards.Minimal(Guid.NewGuid(), identity);
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        await db.Entry(card).ReloadAsync();
        Assert.Equal(mask, card.ColorIdentityMask);
    }

    [Fact]
    public async Task Deck_identity_subset_filter_is_one_bitwise_and_against_the_mask()
    {
        var cards = new Dictionary<string, string[]>
        {
            ["colorless"] = [],
            ["mono-white"] = ["W"],
            ["azorius"] = ["W", "U"],
            ["dimir"] = ["U", "B"],
            ["gruul"] = ["R", "G"],
            ["five-color"] = ["W", "U", "B", "R", "G"],
        };
        var oracleIds = cards.ToDictionary(c => c.Key, _ => Guid.NewGuid());
        await using var db = fixture.CreateCorpusContext();
        db.Cards.AddRange(cards.Select(c => TestCards.Minimal(oracleIds[c.Key], c.Value)));
        await db.SaveChangesAsync();

        const int deckMask = 16 | 8 | 1; // WUG (Bant)
        var inPool = await db.Database
            .SqlQuery<Guid>($"SELECT oracle_id AS \"Value\" FROM card WHERE color_identity_mask & ~{deckMask} = 0")
            .ToListAsync();

        string[] subsets = ["colorless", "mono-white", "azorius"];
        Assert.All(subsets, name => Assert.Contains(oracleIds[name], inPool));
        string[] escapes = ["dimir", "gruul", "five-color"];
        Assert.All(escapes, name => Assert.DoesNotContain(oracleIds[name], inPool));
    }

    [Fact]
    public async Task Copy_limit_defaults_to_singleton_and_null_means_unlimited()
    {
        var singleton = Guid.NewGuid();
        var unlimited = Guid.NewGuid();
        await using (var db = fixture.CreateCorpusContext())
        {
            db.Cards.Add(TestCards.Minimal(singleton));
            var basic = TestCards.Minimal(unlimited);
            basic.CopyLimit = null;
            db.Cards.Add(basic);
            await db.SaveChangesAsync();
        }

        await using var reader = fixture.CreateCorpusContext();
        Assert.Equal(1, (await reader.Cards.SingleAsync(c => c.OracleId == singleton)).CopyLimit);
        Assert.Null((await reader.Cards.SingleAsync(c => c.OracleId == unlimited)).CopyLimit);
    }

    [Fact]
    public async Task Copy_limit_default_reaches_inserts_that_bypass_the_model()
    {
        var oracleId = Guid.NewGuid();
        await using var db = fixture.CreateCorpusContext();
        await db.Database.ExecuteSqlAsync($$"""
            INSERT INTO card (oracle_id, name, mana_value, type_line, supertypes, card_types,
                              subtypes, color_identity, keywords, released_at, raw, game_changer,
                              can_be_commander, has_partner, has_partner_with, has_friends_forever,
                              has_background, has_doctors_companion)
            VALUES ({{oracleId}}, 'Raw Loader Card', 0, 'Artifact', '{}', ARRAY['Artifact'],
                    '{}', '{}', '{}', '2020-01-01', '{}'::jsonb, false,
                    false, false, false, false, false, false)
            """);

        Assert.Equal(1, (await db.Cards.SingleAsync(c => c.OracleId == oracleId)).CopyLimit);
    }

    [Fact]
    public async Task Multi_faced_card_carries_per_face_cost_type_and_text()
    {
        var oracleId = Guid.NewGuid();
        await using (var db = fixture.CreateCorpusContext())
        {
            var card = TestCards.Minimal(oracleId, "B");
            card.TypeLine = "Legendary Creature — Human Wizard // Legendary Planeswalker — Liliana";
            card.Faces =
            [
                new CardFace
                {
                    FaceIndex = 0,
                    Name = "Liliana, Heretical Healer",
                    ManaCost = "{1}{B}{B}",
                    ManaValue = 3m,
                    TypeLine = "Legendary Creature — Human Wizard",
                    OracleText = "Lifelink\nWhenever a nontoken creature you control dies, exile Liliana, then return her to the battlefield transformed under her owner's control.",
                    Colors = ["B"],
                    Power = "2",
                    Toughness = "3",
                },
                new CardFace
                {
                    FaceIndex = 1,
                    Name = "Liliana, Defiant Necromancer",
                    TypeLine = "Legendary Planeswalker — Liliana",
                    OracleText = "[+2]: Each player discards a card.",
                    Colors = ["B"],
                    Loyalty = "3",
                },
            ];
            db.Cards.Add(card);
            await db.SaveChangesAsync();
        }

        await using var reader = fixture.CreateCorpusContext();
        var faces = await reader.Cards.Where(c => c.OracleId == oracleId)
            .SelectMany(c => c.Faces.OrderBy(f => f.FaceIndex))
            .ToListAsync();

        Assert.Equal(2, faces.Count);
        var front = faces[0];
        Assert.Equal("Liliana, Heretical Healer", front.Name);
        Assert.Equal("{1}{B}{B}", front.ManaCost);
        Assert.Equal(3m, front.ManaValue);
        Assert.Equal("Legendary Creature — Human Wizard", front.TypeLine);
        Assert.StartsWith("Lifelink", front.OracleText);
        Assert.Equal(["B"], front.Colors);
        Assert.Equal("2", front.Power);
        Assert.Equal("3", front.Toughness);
        var back = faces[1];
        Assert.Null(back.ManaCost);
        Assert.Null(back.ManaValue);
        Assert.Equal("3", back.Loyalty);
    }

    [Fact]
    public async Task Rulings_and_oracle_tags_hang_off_the_card_and_die_with_it()
    {
        var oracleId = Guid.NewGuid();
        int cardId;
        await using (var db = fixture.CreateCorpusContext())
        {
            var card = TestCards.Minimal(oracleId, "G");
            card.Rulings =
            [
                new CardRuling
                {
                    Source = "wotc",
                    PublishedAt = new DateOnly(2021, 3, 19),
                    Comment = "The ability triggers once for each time it happens.",
                },
            ];
            card.OracleTags = [new CardOracleTag { Tag = "ramp" }, new CardOracleTag { Tag = "removal" }];
            db.Cards.Add(card);
            await db.SaveChangesAsync();
            cardId = card.Id;
        }

        await using (var reader = fixture.CreateCorpusContext())
        {
            var card = await reader.Cards
                .Include(c => c.Rulings)
                .Include(c => c.OracleTags)
                .SingleAsync(c => c.OracleId == oracleId);
            var ruling = Assert.Single(card.Rulings);
            Assert.Equal("wotc", ruling.Source);
            Assert.Equal(new DateOnly(2021, 3, 19), ruling.PublishedAt);
            Assert.Equal("The ability triggers once for each time it happens.", ruling.Comment);
            Assert.Equal(["ramp", "removal"], card.OracleTags.Select(t => t.Tag).Order());

            reader.Cards.Remove(card);
            await reader.SaveChangesAsync();
        }

        await using var check = fixture.CreateCorpusContext();
        Assert.False(await check.CardRulings.AnyAsync(r => r.CardId == cardId));
        Assert.False(await check.CardOracleTags.AnyAsync(t => t.CardId == cardId));
    }

    [Fact]
    public async Task Oracle_id_is_the_unique_card_identity()
    {
        var oracleId = Guid.NewGuid();
        await using var db = fixture.CreateCorpusContext();
        db.Cards.Add(TestCards.Minimal(oracleId));
        await db.SaveChangesAsync();

        await using var duplicate = fixture.CreateCorpusContext();
        duplicate.Cards.Add(TestCards.Minimal(oracleId));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());

        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
    }
}

/// <summary>Minimal valid corpus cards for schema tests.</summary>
internal static class TestCards
{
    public static Card Minimal(Guid oracleId, params string[] colorIdentity) => new()
    {
        OracleId = oracleId,
        Name = $"Test Card {oracleId:N}",
        ManaValue = 0m,
        TypeLine = "Artifact",
        Supertypes = [],
        CardTypes = ["Artifact"],
        Subtypes = [],
        ColorIdentity = colorIdentity,
        Keywords = [],
        ReleasedAt = new DateOnly(2020, 1, 1),
        Raw = JsonDocument.Parse("{}"),
    };
}
