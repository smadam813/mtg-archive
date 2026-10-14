using MtgArchive.Data;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// The phase-2 loader over stored card feature profiles: one profile per
/// face name, the back face of a modal double-faced card under its own name,
/// and the unprofiled list for engine coverage. A deck card with no stored
/// row, or a modal back face with none, is unprofiled, never a card with no
/// features.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CardFeatureProfilesTests(PostgresFixture postgres)
{
    private const string AwakeningSentence =
        "Put any number of cards from your hand on the bottom of your library, then draw that many cards plus one";

    [Fact]
    public async Task Load_reads_each_face_under_its_name_and_lists_unprofiled_cards()
    {
        var ring = TestCards.Minimal(Guid.NewGuid());
        var pathway = Modal("Brightclimb Pathway", "Land", "Grimclimb Pathway", "Land");
        var awakening = Modal("Valakut Awakening", "Instant", "Valakut Stoneforge", "Land");
        var unseen = TestCards.Minimal(Guid.NewGuid());
        var halfSeen = Modal("Sink into Stupor", "Instant", "Soporific Springs", "Land");
        await using (var db = postgres.CreateCorpusContext())
        {
            db.Cards.AddRange(ring, pathway, awakening, unseen, halfSeen);
            await db.SaveChangesAsync();
            db.CardFeatureProfileRows.AddRange(
                TapRow(ring.Id, 0, "{C}{C}"),
                TapRow(pathway.Id, 0, "{W}"),
                TapRow(pathway.Id, 1, "{B}"),
                new CardFeatureProfileRow { CardId = awakening.Id, FaceIndex = 0, Unprofiled = true, UnmatchedSentence = AwakeningSentence },
                TapRow(awakening.Id, 1, "{R}"),
                new CardFeatureProfileRow { CardId = halfSeen.Id, FaceIndex = 0 });
            await db.SaveChangesAsync();
        }

        await using (var db = postgres.CreateCorpusContext())
        {
            var deck = await DeckCards.LoadAsync(db, new ResolvedDecklist
            {
                Entries = [new(ring.OracleId, 1), new(pathway.OracleId, 1), new(awakening.OracleId, 1), new(unseen.OracleId, 1), new(halfSeen.OracleId, 1)],
            });

            var profiles = await CardFeatureProfiles.LoadAsync(db, deck, CancellationToken.None);

            var card = deck.ToDictionary(c => c.OracleId);
            Assert.Equal("develops tap={C}{C} activated", ProfileText.Of(profiles.Of(card[ring.OracleId])));
            Assert.Equal("develops tap={W} activated", ProfileText.Of(profiles.Of(card[pathway.OracleId])));
            Assert.Equal("develops tap={B} activated", ProfileText.Of(profiles.Of(card[pathway.OracleId].AsBackFace()!)));
            Assert.Equal("", ProfileText.Of(profiles.Of(card[awakening.OracleId])));
            Assert.Equal("develops tap={R} activated", ProfileText.Of(profiles.Of(card[awakening.OracleId].AsBackFace()!)));
            Assert.Same(CardFeatureProfile.None, profiles.Of(card[unseen.OracleId]));
            Assert.Same(CardFeatureProfile.None, profiles.Of(card[halfSeen.OracleId].AsBackFace()!));
            Assert.Equal(new[] { awakening.Name, unseen.Name, halfSeen.Name }.Order(StringComparer.Ordinal), profiles.Unprofiled);
        }
    }

    // A spell counter whose reach covers a disruption card is a response
    // card; an ability counter, a creature-only counter and a
    // red-only counter answer none. A grant counts only when it answers a
    // disruption card: Heroic Intervention does, protection
    // from red does not. A response-unprofiled face lists its
    // card apart from the unprofiled cards. Both lists sort by name.
    [Fact]
    public async Task Load_lists_the_response_cards_and_the_unprofiled_responses()
    {
        var negate = Named("Zz Negate");
        var counterspell = Named("Aa Counterspell");
        var stifle = Named("Stifle");
        var snake = Named("Mystic Snake");
        var halfSeen = Named("Test Half Profiled");
        var scatter = Named("Essence Scatter");
        var hydroblast = Named("Hydroblast");
        var heroic = Named("Heroic Intervention");
        var redWard = Named("Test Protection From Red");
        await using (var db = postgres.CreateCorpusContext())
        {
            db.Cards.AddRange(negate, counterspell, stifle, snake, halfSeen, scatter, hydroblast, heroic, redWard);
            await db.SaveChangesAsync();
            db.CardFeatureProfileRows.AddRange(
                new CardFeatureProfileRow { CardId = negate.Id, FaceIndex = 0, ResponseCounterReach = CounterReach.Spell, ResponseCounterExcludedTypes = ["Creature"] },
                new CardFeatureProfileRow { CardId = counterspell.Id, FaceIndex = 0, ResponseCounterReach = CounterReach.Spell },
                new CardFeatureProfileRow { CardId = stifle.Id, FaceIndex = 0, ResponseCounterReach = CounterReach.Ability },
                new CardFeatureProfileRow { CardId = scatter.Id, FaceIndex = 0, ResponseCounterReach = CounterReach.Spell, ResponseCounterTypes = ["Creature"] },
                new CardFeatureProfileRow { CardId = hydroblast.Id, FaceIndex = 0, ResponseCounterReach = CounterReach.Spell, ResponseCounterColors = ["R"] },
                new CardFeatureProfileRow { CardId = heroic.Id, FaceIndex = 0, ResponseGrants = ["hexproof", "indestructible"], ResponseGrantScopes = ["permanents", "permanents"] },
                new CardFeatureProfileRow { CardId = redWard.Id, FaceIndex = 0, ResponseGrants = ["protection_from_red"], ResponseGrantScopes = ["target_creature"] },
                new CardFeatureProfileRow { CardId = snake.Id, FaceIndex = 0, ResponseUnprofiled = true, ResponseUnmatchedSentence = "When this creature enters, counter target spell" },
                new CardFeatureProfileRow
                {
                    CardId = halfSeen.Id,
                    FaceIndex = 0,
                    ResponseCounterReach = CounterReach.Spell,
                    ResponseCounterUnlessPays = "{2}",
                    ResponseUnprofiled = true,
                    ResponseUnmatchedSentence = "Counter target spell",
                });
            await db.SaveChangesAsync();
        }

        await using (var db = postgres.CreateCorpusContext())
        {
            var deck = await DeckCards.LoadAsync(db, new ResolvedDecklist
            {
                Entries = [.. new[] { negate, counterspell, stifle, snake, halfSeen, scatter, hydroblast, heroic, redWard }.Select(card => new ResolvedEntry(card.OracleId, 1))],
            });

            var profiles = await CardFeatureProfiles.LoadAsync(db, deck, CancellationToken.None);

            var card = deck.ToDictionary(c => c.OracleId);
            Assert.Equal("counter=spell/not=Creature", ProfileText.Of(profiles.Of(card[negate.OracleId])));
            Assert.Equal("counter=ability", ProfileText.Of(profiles.Of(card[stifle.OracleId])));
            Assert.NotNull(profiles.Of(card[scatter.OracleId]).Response);
            Assert.NotNull(profiles.Of(card[hydroblast.OracleId]).Response);
            Assert.Equal("grants=protection_from_red:target_creature", ProfileText.Of(profiles.Of(card[redWard.OracleId])));
            Assert.Equal([counterspell.Name, heroic.Name, halfSeen.Name, negate.Name], profiles.ResponseCards);
            Assert.Equal([snake.Name, halfSeen.Name], profiles.UnprofiledResponses);
            Assert.Empty(profiles.Unprofiled);
        }
    }

    private static Card Named(string name)
    {
        var card = TestCards.Minimal(Guid.NewGuid());
        card.Name = $"{name} {card.OracleId:N}";
        return card;
    }

    private static CardFeatureProfileRow TapRow(int cardId, int faceIndex, string tapMana) => new()
    {
        CardId = cardId,
        FaceIndex = faceIndex,
        Develops = true,
        TapMana = [tapMana],
        HasActivatedAbility = true,
    };

    private static Card Modal(string front, string frontType, string back, string backType)
    {
        var card = TestCards.Minimal(Guid.NewGuid());
        card.Name = $"{front} // {back}";
        card.Layout = "modal_dfc";
        card.TypeLine = $"{frontType} // {backType}";
        card.CardTypes = [.. new[] { frontType, backType }.Distinct()];
        card.Faces =
        [
            new CardFace { FaceIndex = 0, Name = front, TypeLine = frontType },
            new CardFace { FaceIndex = 1, Name = back, TypeLine = backType },
        ];
        return card;
    }
}
