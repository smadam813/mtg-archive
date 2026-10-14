using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// The goal-aware mulligan on a pilot view built by hand: the
/// XMage adapter builds its views this way. The policy keeps a seven or a
/// six only when the lands are in the window and the hand holds a goal
/// piece or a tutor for one. The view gives the commander, the card
/// feature profiles, and what each tutor can find. (Ported from the
/// goldfish-state tests at cutover.)
/// </summary>
public sealed class GoalAwareMulliganViewTests
{
    private static readonly DeckCard Swamp = TestDeckCards.Land("Swamp", "B");
    private static readonly DeckCard Island = TestDeckCards.Land("Island", "U");
    private static readonly DeckCard Mountain = TestDeckCards.Land("Mountain", "R");
    private static readonly DeckCard Needle = TestDeckCards.Spell("Pithing Needle", "{1}", "Artifact");
    private static readonly DeckCard Tutor = TestDeckCards.Spell("Artifact Tutor", "{B}");
    private static readonly DeckCard Ogre = TestDeckCards.Spell("Gray Ogre", "{2}{R}", "Creature");
    private static readonly DeckCard Rock = TestDeckCards.Spell("Mind Stone", "{2}", "Artifact") with { ProducedColors = ["C"] };
    private static readonly DeckCard Demonic = TestDeckCards.Spell("Demonic Tutor", "{1}{B}");
    private static readonly DeckCard Ritual = TestDeckCards.Spell("Dark Ritual", "{B}", "Instant");
    private static readonly DeckCard Filler = TestDeckCards.Spell("Filler", "{7}", "Creature");
    private static readonly DeckCard SpiritGuide = TestDeckCards.Spell("Simian Spirit Guide", "{2}{R}", "Creature");
    private static readonly DeckCard SolRing = TestDeckCards.Spell("Sol Ring", "{1}", "Artifact");
    private static readonly DeckCard Seeker = TestDeckCards.Spell("Artifact Seeker", "{2}");
    private static readonly DeckCard Summoner = TestDeckCards.Spell("Artifact Summoner", "{B}");
    private static readonly DeckCard ArtifactTopTutor = TestDeckCards.Spell("Artifact Top Tutor", "{B}");
    private static readonly DeckCard CreatureTopTutor = TestDeckCards.Spell("Creature Top Tutor", "{B}");
    private static readonly DeckCard StripMine = NonbasicLand("Strip Mine", "C");
    private static readonly DeckCard BasicFetch = NonbasicLand("Basic Fetch", "C") with { ProducedColors = [] };
    private static readonly DeckCard SlowFetch = NonbasicLand("Slow Fetch", "C") with { ProducedColors = [] };
    private static readonly DeckCard Sheoldred = TestDeckCards.Commander("Sheoldred, the Apocalypse", "{2}{B}{B}");

    private static readonly GoalPredicate NeedleByOne = new GoalPredicate.OnBattlefield("Pithing Needle", 1);
    private static readonly GoalPredicate NeedleByTwo = new GoalPredicate.OnBattlefield("Pithing Needle", 2);
    private static readonly GoalPredicate NeedleByThree = new GoalPredicate.OnBattlefield("Pithing Needle", 3);
    private static readonly GoalPredicate SheoldredByFour = new GoalPredicate.Cast("Sheoldred, the Apocalypse", 4);

    private static TutorFeature SearchVia(TutorDestination destination) => new(destination, false, TutorVia.Cast, null);

    private static TutorFeature Fetch(bool entersTapped) => new(TutorDestination.Battlefield, entersTapped, TutorVia.ActivateOnBattlefield, null);

    private static readonly IReadOnlyDictionary<string, CardFeatureProfile> Profiles = new Dictionary<string, CardFeatureProfile>
    {
        ["Swamp"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{B}"] },
        ["Island"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{U}"] },
        ["Mountain"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{R}"] },
        ["Strip Mine"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{C}"], HasActivatedAbility = true },
        ["Basic Fetch"] = CardFeatureProfile.None with { Develops = true, Tutor = Fetch(entersTapped: false), HasActivatedAbility = true },
        ["Slow Fetch"] = CardFeatureProfile.None with { Develops = true, Tutor = Fetch(entersTapped: true), HasActivatedAbility = true },
        ["Artifact Tutor"] = CardFeatureProfile.None with { Tutor = SearchVia(TutorDestination.Hand) },
        ["Demonic Tutor"] = CardFeatureProfile.None with { Tutor = SearchVia(TutorDestination.Hand) },
        ["Artifact Seeker"] = CardFeatureProfile.None with { Tutor = SearchVia(TutorDestination.Hand) },
        ["Artifact Summoner"] = CardFeatureProfile.None with { Tutor = SearchVia(TutorDestination.Battlefield) },
        ["Artifact Top Tutor"] = CardFeatureProfile.None with { Tutor = SearchVia(TutorDestination.LibraryTop) },
        ["Creature Top Tutor"] = CardFeatureProfile.None with { Tutor = SearchVia(TutorDestination.LibraryTop) },
        ["Dark Ritual"] = CardFeatureProfile.None with { Develops = true, SpellMana = "{B}{B}{B}" },
        ["Simian Spirit Guide"] = CardFeatureProfile.None with { Develops = true, HandMana = ["{R}"], HasActivatedAbility = true },
        ["Sol Ring"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{C}{C}"], HasActivatedAbility = true },
    };

    /// <summary>What each search finds in the library of the old test decks:
    /// an artifact search finds Pithing Needle, a creature search finds the
    /// Ogre, Demonic Tutor finds any card, and a fetch finds a basic.</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<DeckCard>> LibraryFinds = new Dictionary<string, IReadOnlyList<DeckCard>>
    {
        ["Artifact Tutor"] = [Needle],
        ["Artifact Seeker"] = [Needle],
        ["Artifact Summoner"] = [Needle],
        ["Artifact Top Tutor"] = [Needle],
        ["Creature Top Tutor"] = [Ogre],
        ["Demonic Tutor"] = [Needle, Ogre, StripMine, Swamp, Island, Mountain],
        ["Basic Fetch"] = [Swamp, Island, Mountain],
        ["Slow Fetch"] = [Swamp, Island, Mountain],
    };

    private static DeckCard NonbasicLand(string name, string color)
        => TestDeckCards.Land(name, color) with { IsBasic = false, Supertypes = [] };

    private static CardFeatureProfile Features(DeckCard face) => Profiles.GetValueOrDefault(face.Name) ?? CardFeatureProfile.None;

    /// <summary>The turn-1 view of the mulligan: Sheoldred in the command
    /// zone, nothing in play, and the finds given.</summary>
    private static PilotView Dealt(IReadOnlyList<DeckCard> hand, IReadOnlyDictionary<string, IReadOnlyList<DeckCard>>? finds = null)
        => new(
            Turn: 1,
            TurnStep.Main,
            Life: 40,
            LandDropUsed: false,
            new ManaBoard(new ManaPool(), 40, [], []),
            hand,
            [],
            [],
            [],
            [Sheoldred],
            [],
            finds ?? LibraryFinds,
            [],
            Features);

    private static bool Keeps(GoalPredicate goal, DeckCard[] hand, int keepSize = 7, IReadOnlyDictionary<string, IReadOnlyList<DeckCard>>? finds = null)
        => new GoalAwareMulligan(goal).KeepHand(Dealt(hand, finds), new MulliganChoice(hand, 7 - keepSize, keepSize));

    [Fact]
    public void Keeps_a_seven_with_a_goal_piece_and_lands_in_the_window()
    {
        Assert.True(Keeps(NeedleByThree, [Swamp, Swamp, Swamp, Needle, Ogre, Ogre, Rock]));
        Assert.False(Keeps(NeedleByThree, [Swamp, Needle, Ogre, Ogre, Ogre, Ogre, Rock]), "one land is below the window");
        Assert.False(Keeps(NeedleByThree, [Swamp, Swamp, Swamp, Swamp, Swamp, Swamp, Needle]), "six lands are above the window");
    }

    [Fact]
    public void Mulligans_a_seven_without_a_goal_piece_or_a_tutor_even_with_lands_in_the_window()
    {
        DeckCard[] hand = [Swamp, Swamp, Swamp, Ogre, Ogre, Ogre, Rock];

        Assert.False(Keeps(NeedleByThree, hand));
        Assert.True(new LandWindowMulligan().KeepHand(Dealt(hand), new MulliganChoice(hand, 0, 7)));
    }

    [Fact]
    public void A_commander_goal_keeps_by_the_land_window_alone()
    {
        Assert.True(Keeps(SheoldredByFour, [Swamp, Swamp, Swamp, Ogre, Ogre, Ogre, Rock]));
        Assert.True(Keeps(SheoldredByFour, [Swamp, Swamp, Swamp, Ogre, Ogre, Ogre, Rock], keepSize: 6));
        Assert.False(Keeps(SheoldredByFour, [Swamp, Ogre, Ogre, Ogre, Ogre, Ogre, Rock]), "one land is below the window");
        Assert.False(Keeps(SheoldredByFour, [Swamp, Swamp, Swamp, Swamp, Swamp, Swamp, Ogre]), "six lands are above the window");
    }

    [Fact]
    public void An_all_of_over_the_commander_and_a_library_card_keeps_on_the_commander_alone()
    {
        var both = new GoalPredicate.AllOf([SheoldredByFour, NeedleByThree]);

        Assert.True(Keeps(both, [Swamp, Swamp, Swamp, Ogre, Ogre, Ogre, Rock]));
        Assert.False(Keeps(both, [Swamp, Ogre, Ogre, Ogre, Ogre, Ogre, Rock]), "one land is below the window");
    }

    [Fact]
    public void Keeps_a_hand_with_a_tutor_that_can_find_the_goal_piece()
    {
        DeckCard[] hand = [Swamp, Swamp, Swamp, Tutor, Ogre, Ogre, Rock];
        Assert.True(Keeps(NeedleByThree, hand));

        var noNeedleLeft = new Dictionary<string, IReadOnlyList<DeckCard>>(LibraryFinds) { ["Artifact Tutor"] = [] };
        Assert.False(Keeps(NeedleByThree, hand, finds: noNeedleLeft), "the tutor finds nothing the goal wants");
    }

    /// <summary>The mulligan runs on turn 1. A card the tutor
    /// puts on top reaches hand at the turn-2 draw.</summary>
    [Fact]
    public void A_library_top_tutor_keeps_only_when_the_goal_piece_reaches_hand_in_time()
    {
        Assert.False(Keeps(NeedleByOne, [Swamp, Swamp, Swamp, ArtifactTopTutor, Ogre, Ogre, Rock]));
        Assert.True(Keeps(NeedleByTwo, [Swamp, Swamp, Swamp, ArtifactTopTutor, Ogre, Ogre, Rock]));
        Assert.True(Keeps(NeedleByOne, [Swamp, Swamp, Swamp, Tutor, Ritual, Ogre, Rock]), "a tutor to hand is in time");
    }

    /// <summary>An all_of times a library-top tutor by the leaf
    /// it can find.</summary>
    [Fact]
    public void An_all_of_times_a_library_top_tutor_by_the_leaf_it_can_find()
    {
        var goal = new GoalPredicate.AllOf([NeedleByOne, new GoalPredicate.OnBattlefield("Gray Ogre", 3)]);

        Assert.False(Keeps(goal, [Swamp, Swamp, Swamp, ArtifactTopTutor, Rock, Rock, Rock]), "the tutor finds only the turn-1 leaf");
        Assert.True(Keeps(goal, [Swamp, Swamp, Mountain, CreatureTopTutor, Rock, Rock, Rock]), "the tutor finds the turn-3 leaf");
    }

    [Fact]
    public void A_tutor_the_hand_cannot_pay_for_by_the_deadline_does_not_count()
    {
        // Demonic Tutor and Pithing Needle cost 3 mana; one land drop makes 1 on turn 1.
        Assert.False(Keeps(NeedleByOne, [Swamp, Swamp, Island, Demonic, Filler, Filler, Filler]));
        Assert.False(Keeps(NeedleByOne, [Swamp, Swamp, Island, Demonic, Filler, Filler, Filler], keepSize: 6));
    }

    [Fact]
    public void A_ritual_pays_for_a_turn_one_tutor_and_the_goal_piece()
    {
        Assert.True(Keeps(NeedleByOne, [Swamp, Swamp, Island, Demonic, Ritual, Filler, Filler]));
        Assert.True(Keeps(NeedleByOne, [Swamp, Swamp, Island, Demonic, Ritual, Filler, Filler], keepSize: 6));
        Assert.False(Keeps(NeedleByOne, [Island, Island, Island, Demonic, Ritual, Filler, Filler]), "no land casts the ritual");
    }

    /// <summary>Dark Ritual and Sol Ring tie in the bottom
    /// order, so Dark Ritual goes under first and the six cannot pay.</summary>
    [Fact]
    public void A_six_pays_for_the_tutor_from_the_cards_the_bottom_leaves()
    {
        Assert.True(Keeps(NeedleByOne, [Swamp, Island, Island, Island, Demonic, Ritual, SolRing]));
        Assert.False(Keeps(NeedleByOne, [Swamp, Island, Island, Island, Demonic, Ritual, SolRing], keepSize: 6));
    }

    [Fact]
    public void A_card_that_makes_mana_from_hand_pays_for_a_turn_one_tutor()
        => Assert.True(Keeps(NeedleByOne, [Swamp, Swamp, Island, Demonic, SpiritGuide, SpiritGuide, Filler]));

    [Fact]
    public void A_mana_rock_the_hand_can_cast_pays_for_a_tutor()
    {
        // Island casts Sol Ring: with a second land that makes 3 mana for Artifact Seeker and Pithing Needle.
        Assert.True(Keeps(NeedleByTwo, [Island, Island, Seeker, SolRing, Filler, Filler, Filler]));
        Assert.False(Keeps(NeedleByTwo, [Island, Island, Seeker, Filler, Filler, Filler, Filler]));
    }

    [Fact]
    public void A_tutor_to_the_battlefield_pays_its_own_cost_only()
    {
        Assert.True(Keeps(NeedleByOne, [Swamp, Swamp, Island, Summoner, Filler, Filler, Filler]));
        Assert.False(Keeps(NeedleByOne, [Swamp, Swamp, Island, Tutor, Filler, Filler, Filler]), "a tutor to hand also pays for Pithing Needle");
    }

    [Fact]
    public void A_fetch_land_adds_the_colors_it_can_find_when_the_land_enters_untapped()
    {
        Assert.True(Keeps(NeedleByOne, [BasicFetch, Island, Island, Demonic, Ritual, Filler, Filler]));
        Assert.False(Keeps(NeedleByOne, [SlowFetch, Island, Island, Demonic, Ritual, Filler, Filler]), "the fetched land enters tapped");
    }

    [Fact]
    public void A_goal_piece_that_is_a_land_takes_the_land_drop_of_its_turn()
    {
        var stripMineByOne = new GoalPredicate.OnBattlefield("Strip Mine", 1);
        var stripMineByTwo = new GoalPredicate.OnBattlefield("Strip Mine", 2);

        Assert.False(Keeps(stripMineByOne, [Swamp, Swamp, Island, Demonic, Ritual, Filler, Filler]), "the Swamp takes the only land drop");
        Assert.True(Keeps(stripMineByTwo, [Swamp, Swamp, Island, Demonic, Ritual, Filler, Filler]));
    }

    [Fact]
    public void Colors_count_toward_a_payable_tutor()
        => Assert.False(Keeps(NeedleByThree, [Island, Island, Island, Demonic, Filler, Filler, Filler]));

    [Fact]
    public void A_later_deadline_pays_for_the_tutor_and_the_goal_piece_from_land_drops()
    {
        // Demonic Tutor on turn 2 with two lands, Pithing Needle on turn 3.
        Assert.True(Keeps(NeedleByThree, [Swamp, Swamp, Island, Demonic, Filler, Filler, Filler]));
        Assert.False(Keeps(NeedleByTwo, [Swamp, Swamp, Island, Demonic, Filler, Filler, Filler]), "turn 1 cannot pay the tutor, turn 2 cannot pay both");
    }

    [Fact]
    public void An_all_of_counts_a_tutor_through_the_leaf_whose_deadline_it_can_pay_by()
    {
        // Demonic Tutor and Gray Ogre need {1}{B} on one turn and {2}{R} on a later one.
        var laterOgre = new GoalPredicate.AllOf([NeedleByOne, new GoalPredicate.OnBattlefield("Gray Ogre", 3)]);
        var earlyOgre = new GoalPredicate.AllOf([NeedleByOne, new GoalPredicate.OnBattlefield("Gray Ogre", 2)]);

        Assert.True(Keeps(laterOgre, [Swamp, Swamp, Mountain, Demonic, Filler, Filler, Filler]));
        Assert.False(Keeps(earlyOgre, [Swamp, Swamp, Mountain, Demonic, Filler, Filler, Filler]));
    }

    [Fact]
    public void Always_keeps_five_or_fewer()
    {
        Assert.True(Keeps(NeedleByThree, [Ogre, Ogre, Ogre, Ogre, Ogre, Ogre, Ogre], keepSize: 5));
        Assert.False(Keeps(NeedleByThree, [Swamp, Swamp, Swamp, Ogre, Ogre, Ogre, Rock], keepSize: 6));
        Assert.True(Keeps(NeedleByThree, [Swamp, Swamp, Swamp, Needle, Ogre, Ogre, Rock], keepSize: 6));
    }

    [Fact]
    public void The_bottom_sheds_lands_above_the_window_then_the_least_useful_cards()
    {
        var policy = new GoalAwareMulligan(NeedleByThree);

        // A keep of five allows three lands: two Swamps go under before any spell.
        DeckCard[] lands = [Swamp, Swamp, Swamp, Swamp, Swamp, Needle, Ogre];
        Assert.Equal([Swamp, Swamp], policy.ChooseBottom(Dealt(lands), new BottomChoice(lands, 2)));

        // A keep of six allows four: one Swamp goes under, the Ogre stays.
        Assert.Equal([Swamp], policy.ChooseBottom(Dealt(lands), new BottomChoice(lands, 1)));

        // Lands in the window: the dearest spells go first, the tutor and the goal piece stay.
        DeckCard[] spells = [Swamp, Swamp, Tutor, Needle, Ogre, Rock, Ogre];
        Assert.Equal([Ogre, Ogre, Rock], policy.ChooseBottom(Dealt(spells), new BottomChoice(spells, 3)));
    }
}
