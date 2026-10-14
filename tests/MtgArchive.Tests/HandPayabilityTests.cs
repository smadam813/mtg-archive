using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// The goal-aware mulligan's mana check on a pilot view built by hand: it
/// reads the card feature profiles and the view's finds, and plays the
/// hand on a scratch mana board. No engine stands behind the view.
/// </summary>
public sealed class HandPayabilityTests
{
    private static readonly DeckCard Swamp = TestDeckCards.Land("Swamp", "B");
    private static readonly DeckCard Island = TestDeckCards.Land("Island", "U");
    private static readonly DeckCard Needle = TestDeckCards.Spell("Pithing Needle", "{1}", "Artifact");
    private static readonly DeckCard Tutor = TestDeckCards.Spell("Demonic Tutor", "{1}{B}");
    private static readonly DeckCard Ritual = TestDeckCards.Spell("Dark Ritual", "{B}", "Instant");
    private static readonly DeckCard Guide = TestDeckCards.Spell("Simian Spirit Guide", "{2}{R}", "Creature");
    private static readonly DeckCard Ring = TestDeckCards.Spell("Sol Ring", "{1}", "Artifact");
    private static readonly DeckCard Fetch = TestDeckCards.Land("Basic Fetch", "C") with { IsBasic = false, Supertypes = [], ProducedColors = [] };
    private static readonly DeckCard SlowFetch = TestDeckCards.Land("Slow Fetch", "C") with { IsBasic = false, Supertypes = [], ProducedColors = [] };

    private static readonly TutorFeature ToHand = new(TutorDestination.Hand, false, TutorVia.Cast, null);

    private static readonly IReadOnlyDictionary<string, CardFeatureProfile> Profiles = new Dictionary<string, CardFeatureProfile>
    {
        ["Swamp"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{B}"] },
        ["Island"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{U}"] },
        ["Demonic Tutor"] = CardFeatureProfile.None with { Tutor = ToHand },
        ["Dark Ritual"] = CardFeatureProfile.None with { Develops = true, SpellMana = "{B}{B}{B}" },
        ["Simian Spirit Guide"] = CardFeatureProfile.None with { Develops = true, HandMana = ["{R}"], HasActivatedAbility = true },
        ["Sol Ring"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{C}{C}"], HasActivatedAbility = true },
        ["Basic Fetch"] = CardFeatureProfile.None with { Develops = true, Tutor = new TutorFeature(TutorDestination.Battlefield, false, TutorVia.ActivateOnBattlefield, null), HasActivatedAbility = true },
        ["Slow Fetch"] = CardFeatureProfile.None with { Develops = true, Tutor = new TutorFeature(TutorDestination.Battlefield, true, TutorVia.ActivateOnBattlefield, null), HasActivatedAbility = true },
    };

    private static PilotView View(IReadOnlyList<DeckCard> hand, IReadOnlyDictionary<string, IReadOnlyList<DeckCard>>? finds = null)
        => new(
            Turn: 1,
            TurnStep.Mulligan,
            Life: 40,
            LandDropUsed: false,
            new ManaBoard(new ManaPool(), 40, [], []),
            hand,
            [],
            [],
            [],
            [],
            [],
            finds ?? new Dictionary<string, IReadOnlyList<DeckCard>>(),
            [],
            face => Profiles.GetValueOrDefault(face.Name) ?? CardFeatureProfile.None);

    private static bool Pays(int deadline, params DeckCard[] hand) => Pays(View(hand), deadline, hand);

    private static bool Pays(PilotView view, int deadline, params DeckCard[] hand)
        => HandPayability.CanPay(view, hand, Tutor, ToHand, Needle, deadline);

    /// <summary>
    /// The scratch board starts at the life the mulligan view carries,
    /// which is the format's start life (ADR 0012): 26 life pays thirteen
    /// Phyrexian pips at 40 life (Commander) but not at 25 (Brawl), where
    /// the payment stops before the player reaches 1 life or less.
    /// </summary>
    [Fact]
    public void The_scratch_life_is_the_start_life_the_mulligan_view_carries()
    {
        var phyrexian = TestDeckCards.Spell("Thirteen Pips", string.Concat(Enumerable.Repeat("{U/P}", 13)), "Artifact");
        DeckCard[] hand = [Swamp, Swamp, Tutor];

        bool PaysAt(int life)
        {
            var view = View(hand) with { Life = life, Mana = new ManaBoard(new ManaPool(), life, [], []) };
            return HandPayability.CanPay(view, hand, Tutor, ToHand, phyrexian, deadline: 2);
        }

        Assert.True(PaysAt(GameRules.Commander.StartLife));
        Assert.False(PaysAt(GameRules.Brawl.StartLife));
    }

    [Fact]
    public void A_ritual_pays_for_a_turn_one_tutor_and_the_goal_piece()
    {
        Assert.True(Pays(1, Swamp, Island, Tutor, Ritual));
        Assert.False(Pays(1, Swamp, Island, Tutor));
        Assert.False(Pays(1, Island, Island, Tutor, Ritual));
    }

    /// <summary>A Saga's chapter search waits for lore counters, which
    /// the one- or two-turn plans do not model, so the check never counts
    /// it. The Saga still taps for its own produced mana, not a fetch's.</summary>
    [Fact]
    public void A_saga_chapter_tutor_is_never_payable_and_its_land_taps_for_its_own_mana()
    {
        var saga = TestDeckCards.Land("Urza's Saga", "C") with { IsBasic = false, Supertypes = [] };
        var chapter = new TutorFeature(TutorDestination.Battlefield, false, TutorVia.SagaChapter, null, DelayTurns: 2);
        var profiles = new Dictionary<string, CardFeatureProfile>(Profiles)
        {
            ["Urza's Saga"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{C}"], Tutor = chapter },
        };
        var view = View([saga, Ring]) with { FeatureLookup = face => profiles.GetValueOrDefault(face.Name) ?? CardFeatureProfile.None };

        Assert.False(HandPayability.CanPay(view, [saga, Ring], saga, chapter, Needle, 5));

        // Split plan: the Saga's {C} and the Swamp pay for Demonic Tutor on
        // turn 2, then for Needle on turn 3.
        var tutorView = View([saga, Swamp, Tutor]) with { FeatureLookup = view.FeatureLookup };
        Assert.True(HandPayability.CanPay(tutorView, [saga, Swamp, Tutor], Tutor, ToHand, Needle, 3));
    }

    [Fact]
    public void A_fetch_land_adds_the_colors_its_finds_show_unless_the_found_land_enters_tapped()
    {
        var finds = new Dictionary<string, IReadOnlyList<DeckCard>> { ["Basic Fetch"] = [Swamp, Island], ["Slow Fetch"] = [Swamp] };

        Assert.True(Pays(View([Fetch, Island, Tutor, Ritual], finds), 1, Fetch, Island, Tutor, Ritual));
        Assert.False(Pays(View([SlowFetch, Island, Tutor, Ritual], finds), 1, SlowFetch, Island, Tutor, Ritual));

        var islandsOnly = new Dictionary<string, IReadOnlyList<DeckCard>> { ["Basic Fetch"] = [Island] };
        Assert.False(Pays(View([Fetch, Island, Tutor, Ritual], islandsOnly), 1, Fetch, Island, Tutor, Ritual));
    }

    [Fact]
    public void A_card_that_makes_mana_from_hand_pays_once()
    {
        Assert.True(Pays(1, Swamp, Tutor, Guide, Guide));
        Assert.False(Pays(1, Swamp, Tutor, Guide));
    }

    [Fact]
    public void A_rock_the_hand_can_cast_pays_on_the_turn_it_enters()
        => Assert.True(Pays(2, Island, Swamp, Tutor, Ring));
}
