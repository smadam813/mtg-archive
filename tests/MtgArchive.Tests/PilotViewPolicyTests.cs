using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// The goal-aware policies on a pilot view built by hand, with no engine
/// behind it: the XMage adapter builds its views this way. The pilot ranks
/// the listed actions by the card feature profiles and the finds alone.
/// </summary>
public sealed class PilotViewPolicyTests
{
    private static readonly DeckCard Swamp = TestDeckCards.Land("Swamp", "B");
    private static readonly DeckCard Island = TestDeckCards.Land("Island", "U");
    private static readonly DeckCard Needle = TestDeckCards.Spell("Pithing Needle", "{1}", "Artifact");
    private static readonly DeckCard Tutor = TestDeckCards.Spell("Demonic Tutor", "{1}{B}");
    private static readonly DeckCard Ritual = TestDeckCards.Spell("Dark Ritual", "{B}", "Instant");
    private static readonly DeckCard Filler = TestDeckCards.Spell("Filler", "{7}", "Creature");
    private static readonly DeckCard Fetch = TestDeckCards.Land("Basic Fetch", "C") with { IsBasic = false, Supertypes = [], ProducedColors = [] };

    private static readonly DeckCard SolRing = TestDeckCards.Spell("Sol Ring", "{1}", "Artifact");
    private static readonly DeckCard GrayOgre = TestDeckCards.Spell("Gray Ogre", "{2}{R}", "Creature");
    private static readonly DeckCard Forest = TestDeckCards.Land("Forest", "G");
    private static readonly DeckCard Bears = TestDeckCards.Spell("Grizzly Bears", "{1}{G}", "Creature");

    private const string ShatterskullName = "Shatterskull Smashing // Shatterskull, the Hammer Pass";
    private const string PathwayName = "Brightclimb Pathway // Grimclimb Pathway";

    private static readonly DeckCard ShatterskullFront = TestDeckCards.Spell(ShatterskullName, "{X}{R}{R}");

    private static readonly DeckCard Shatterskull = ShatterskullFront with
    {
        FrontFaceName = "Shatterskull Smashing",
        BackFace = NonbasicLand("Shatterskull, the Hammer Pass", "R") with { OracleId = ShatterskullFront.OracleId },
    };

    private static readonly DeckCard PathwayFront = NonbasicLand(PathwayName, "W");

    private static readonly DeckCard Pathway = PathwayFront with
    {
        FrontFaceName = "Brightclimb Pathway",
        BackFace = NonbasicLand("Grimclimb Pathway", "B") with { OracleId = PathwayFront.OracleId },
    };

    private static readonly GoalPredicate NeedleByOne = new GoalPredicate.OnBattlefield("Pithing Needle", 1);
    private static readonly GoalPredicate NeedleByTwo = new GoalPredicate.OnBattlefield("Pithing Needle", 2);

    private static readonly IReadOnlyDictionary<string, CardFeatureProfile> Profiles = new Dictionary<string, CardFeatureProfile>
    {
        ["Swamp"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{B}"] },
        ["Island"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{U}"] },
        ["Demonic Tutor"] = CardFeatureProfile.None with { Tutor = new TutorFeature(TutorDestination.Hand, false, TutorVia.Cast, null) },
        ["Dark Ritual"] = CardFeatureProfile.None with { Develops = true, SpellMana = "{B}{B}{B}" },
        ["Sol Ring"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{C}{C}"] },
        ["Basic Fetch"] = CardFeatureProfile.None with { Develops = true, Tutor = new TutorFeature(TutorDestination.Battlefield, false, TutorVia.ActivateOnBattlefield, null), HasActivatedAbility = true },
    };

    private static CardFeatureProfile Features(DeckCard face) => Profiles.GetValueOrDefault(face.Name) ?? CardFeatureProfile.None;

    private static PilotView View(
        IReadOnlyList<DeckCard> hand,
        IReadOnlyList<BoardManaSource>? sources = null,
        IReadOnlyDictionary<string, IReadOnlyList<DeckCard>>? finds = null,
        IReadOnlyList<EngineAction>? actions = null)
        => new(
            Turn: 1,
            TurnStep.Main,
            Life: 40,
            LandDropUsed: false,
            new ManaBoard(new ManaPool(), 40, sources ?? [], []),
            hand,
            [.. (sources ?? []).Select(s => new ViewPermanent(s.Id, s.Options[0] == "{B}" ? Swamp : Island, false))],
            [],
            [],
            [],
            [],
            finds ?? new Dictionary<string, IReadOnlyList<DeckCard>>(),
            actions ?? [],
            Features);

    private static EngineAction Cast(string id, DeckCard card) => new(id, EngineActionKind.Cast, card, ManaCost.Parse(card.ManaCost), null, Features(card), [], false);

    private static DeckCard NonbasicLand(string name, string color)
        => TestDeckCards.Land(name, color) with { IsBasic = false, Supertypes = [] };

    /// <summary>The land drop of one face: the whole card for a front face,
    /// <see cref="DeckCard.AsBackFace"/> for a back face.</summary>
    private static EngineAction PlayLand(string id, DeckCard face)
        => new(id, EngineActionKind.PlayLand, face, ManaCost.Parse(null), null, Features(face), [.. (face.ProducedColors ?? []).Select(c => $"{{{c}}}")], false);

    /// <summary>A pick whose options are <paramref name="cards"/>, each with
    /// its name as id, and then <paramref name="extra"/>.</summary>
    private static PickChoice Pick(PickPurpose purpose, IReadOnlyList<DeckCard> cards, IReadOnlyList<DeckCard>? seen, params string[] extra)
        => new(purpose, [.. cards.Select(c => new PickOption(c.Name, c.Name, c)), .. extra.Select(e => new PickOption(e, e, null))], seen);

    [Fact]
    public void The_pilot_ranks_the_listed_actions_by_their_features_and_the_finds()
    {
        IReadOnlyList<BoardManaSource> swamps = [new("x1", ["{B}"]), new("x2", ["{B}"])];
        var tutor = Cast("t", Tutor);
        var land = new EngineAction("l", EngineActionKind.PlayLand, Swamp, ManaCost.Parse(null), null, Features(Swamp), ["{B}"], false);
        var pilot = new GoalAwarePilot(NeedleByOne);

        var withNeedle = View([Tutor, Swamp], swamps, new Dictionary<string, IReadOnlyList<DeckCard>> { ["Demonic Tutor"] = [Needle] }, [land, tutor]);
        Assert.Equal("t", pilot.ChooseAction(withNeedle));

        var without = withNeedle with { Finds = new Dictionary<string, IReadOnlyList<DeckCard>> { ["Demonic Tutor"] = [Swamp] } };
        Assert.Equal("l", pilot.ChooseAction(without));
        Assert.Null(pilot.ChooseAction(without with { Actions = [] }));
    }

    [Fact]
    public void The_goal_aware_mulligan_pays_a_tutor_with_the_colors_a_fetch_finds_in_the_view()
    {
        var mulligan = new GoalAwareMulligan(NeedleByOne);
        DeckCard[] hand = [Fetch, Island, Island, Tutor, Ritual, Filler, Filler];
        var choice = new MulliganChoice(hand, 0, 7);

        var findsSwamp = View(hand, finds: new Dictionary<string, IReadOnlyList<DeckCard>> { ["Basic Fetch"] = [Swamp, Island], ["Demonic Tutor"] = [Needle] });
        Assert.True(mulligan.KeepHand(findsSwamp, choice));

        var findsIsland = findsSwamp with { Finds = new Dictionary<string, IReadOnlyList<DeckCard>> { ["Basic Fetch"] = [Island], ["Demonic Tutor"] = [Needle] } };
        Assert.False(mulligan.KeepHand(findsIsland, choice));
    }

    /// <summary>The priority order on a hand-built view: goal, tutor that
    /// finds a goal piece, land drop, ramp, draw, other (ported from the
    /// goldfish-state test at cutover).</summary>
    [Fact]
    public void The_priority_order_is_goal_then_tutor_then_land_then_ramp_then_draw_then_other()
    {
        var cantrip = TestDeckCards.Spell("Ponder", "{U}", "Sorcery");
        var profiles = new Dictionary<string, CardFeatureProfile>(Profiles) { ["Ponder"] = CardFeatureProfile.None with { Draws = true } };
        CardFeatureProfile Features(DeckCard face) => profiles.GetValueOrDefault(face.Name) ?? CardFeatureProfile.None;
        EngineAction Cast(string id, DeckCard card) => new(id, EngineActionKind.Cast, card, ManaCost.Parse(card.ManaCost), null, Features(card), card.Name == "Dark Ritual" ? ["{B}{B}{B}"] : [], false);
        var goal = Cast("g", Needle);
        var tutor = Cast("t", Tutor);
        var land = new EngineAction("l", EngineActionKind.PlayLand, Swamp, ManaCost.Parse(null), null, Features(Swamp), ["{B}"], false);
        var ramp = Cast("r", Ritual);
        var draw = Cast("d", cantrip);
        var other = Cast("o", Filler);
        var finds = new Dictionary<string, IReadOnlyList<DeckCard>> { ["Demonic Tutor"] = [Needle] };
        var built = View([Needle, Tutor, Swamp, Ritual, cantrip, Filler], [new("x1", ["{B}"]), new("x2", ["{U}"])], finds, [other, draw, ramp, land, tutor, goal]);
        var view = built with { FeatureLookup = Features };

        var tiers = view.Actions.Select(a => GoalHeuristics.Tier(view, NeedleByOne, a)).ToList();

        Assert.Equal(new ActionTier?[] { ActionTier.Other, ActionTier.Draw, ActionTier.Ramp, ActionTier.LandDrop, ActionTier.Tutor, ActionTier.Goal }, tiers);
        Assert.Equal("g", new GoalAwarePilot(NeedleByOne).ChooseAction(view));
    }

    /// <summary>Sol Ring and Dark Ritual tie in the ramp tier at
    /// mana value 1. Sol Ring taps the only Swamp, and Demonic Tutor needs
    /// {B}, so the pilot casts the Ritual in either action order. An all_of
    /// whose other leaf needs a color no source makes keeps the same line.
    /// (Ported from the goldfish-state tests at cutover.)</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Within_a_tier_the_pilot_keeps_the_color_a_tutor_in_hand_needs(bool solRingFirst)
    {
        var ring = Cast("ring", SolRing) with { ManaAdded = ["{C}{C}"] };
        var ritual = Cast("ritual", Ritual) with { ManaAdded = ["{B}{B}{B}"] };
        EngineAction[] ramp = solRingFirst ? [ring, ritual] : [ritual, ring];
        var finds = new Dictionary<string, IReadOnlyList<DeckCard>> { ["Demonic Tutor"] = [Needle] };
        var allOf = new GoalPredicate.AllOf([NeedleByOne, new GoalPredicate.OnBattlefield("Gray Ogre", 3)]);

        var view = View([SolRing, Ritual, Tutor], [new("x1", ["{B}"])], finds, ramp);
        var withOgre = View([SolRing, Ritual, GrayOgre, Tutor], [new("x1", ["{B}"])], finds, ramp);

        Assert.Equal("ritual", new GoalAwarePilot(NeedleByOne).ChooseAction(view));
        Assert.Equal("ritual", new GoalAwarePilot(allOf).ChooseAction(withOgre));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void With_no_needed_color_a_tier_keeps_the_mana_value_then_the_action_order(bool solRingFirst)
    {
        var ring = Cast("ring", SolRing) with { ManaAdded = ["{C}{C}"] };
        var ritual = Cast("ritual", Ritual) with { ManaAdded = ["{B}{B}{B}"] };
        EngineAction[] ramp = solRingFirst ? [ring, ritual] : [ritual, ring];

        var view = View([SolRing, Ritual], [new("x1", ["{B}"])], actions: ramp);

        Assert.Equal(ramp[0].Id, new GoalAwarePilot(NeedleByTwo).ChooseAction(view));
    }

    /// <summary>An MDFC land plays its land face alone, and a
    /// basic first when the hand holds one.</summary>
    [Fact]
    public void An_MDFC_land_plays_its_land_face_only_with_no_other_land_in_hand()
    {
        var goal = new GoalPredicate.OnBattlefield("Grizzly Bears", 6);
        var pilot = new GoalAwarePilot(goal);
        var landFace = PlayLand("mdfc", Shatterskull.AsBackFace()!);

        Assert.Equal("mdfc", pilot.ChooseAction(View([Shatterskull, Bears], actions: [landFace])));
        Assert.Equal("forest", pilot.ChooseAction(View([Shatterskull, Forest], actions: [landFace, PlayLand("forest", Forest)])));
    }

    /// <summary>While the goal wants the front face, the land
    /// face is never played.</summary>
    [Fact]
    public void A_goal_that_wants_the_front_face_never_plays_the_land_face()
    {
        var front = new GoalPredicate.Cast("Shatterskull Smashing", 6);
        GoalPredicate[] goals = [front, new GoalPredicate.AllOf([new GoalPredicate.OnBattlefield("Forest", 2), front]), new GoalPredicate.OnBattlefield(ShatterskullName, 6)];
        foreach (var goal in goals)
        {
            var landFace = PlayLand("mdfc", Shatterskull.AsBackFace()!);
            var view = View([Shatterskull], actions: [landFace]);

            Assert.Null(GoalHeuristics.Tier(view, goal, landFace));
            Assert.Null(new GoalAwarePilot(goal).ChooseAction(view));
        }
    }

    /// <summary>A goal that wants the land face plays it over a basic.</summary>
    [Fact]
    public void A_goal_that_wants_the_land_face_plays_it_over_a_basic()
    {
        var land = new GoalPredicate.OnBattlefield("Shatterskull, the Hammer Pass", 6);
        foreach (var goal in new GoalPredicate[] { land, new GoalPredicate.AllOf([new GoalPredicate.OnBattlefield("Grizzly Bears", 3), land]) })
        {
            var view = View([Forest, Shatterskull], actions: [PlayLand("forest", Forest), PlayLand("mdfc", Shatterskull.AsBackFace()!)]);

            Assert.Equal("mdfc", new GoalAwarePilot(goal).ChooseAction(view));
        }
    }

    /// <summary>A pathway plays the face that makes a needed
    /// color, the front face on a tie, and the face a goal names.</summary>
    [Theory]
    [InlineData("{1}{B}", "back")]
    [InlineData("{1}{W}", "front")]
    [InlineData("{W}{B}", "front")]
    [InlineData("{1}{G}", "front")]
    public void A_pathway_plays_the_face_with_a_needed_color_and_the_front_on_a_tie(string goalCost, string played)
    {
        var goalCard = TestDeckCards.Spell("Goal Piece", goalCost, "Creature");
        EngineAction[] faces = [PlayLand("front", Pathway), PlayLand("back", Pathway.AsBackFace()!)];

        Assert.Equal(played, new GoalAwarePilot(new GoalPredicate.OnBattlefield("Goal Piece", 6)).ChooseAction(View([Pathway, goalCard], actions: faces)));
        Assert.Equal("back", new GoalAwarePilot(new GoalPredicate.OnBattlefield("Grimclimb Pathway", 6)).ChooseAction(View([Pathway], actions: faces)));
    }

    /// <summary>The land window counts an MDFC land as a land.</summary>
    [Fact]
    public void The_land_window_counts_an_MDFC_land_as_a_land()
    {
        var mulligan = new LandWindowMulligan();
        var view = View([]);
        DeckCard[] spells = [Bears, Bears, Bears, Bears];

        Assert.True(mulligan.KeepHand(view, new MulliganChoice([Forest, Forest, Shatterskull, .. spells], 0, 7)));
        Assert.True(mulligan.KeepHand(view, new MulliganChoice([Forest, Shatterskull, Bears, .. spells], 0, 7)));
        Assert.False(mulligan.KeepHand(view, new MulliganChoice([Forest, Forest, Forest, Forest, Forest, Shatterskull, Bears], 0, 7)));

        // A keep of five allows three lands, so the bottom takes the first two lands.
        Assert.Equal([Shatterskull, Forest], mulligan.ChooseBottom(view, new BottomChoice([Shatterskull, Forest, Forest, Forest, Forest, Bears, Bears], 2)));
    }

    /// <summary>A look keeps the goal card on top for the draw.
    /// A scry bottoms the rest and keeps the goal card. An order of the top
    /// puts the other cards under first, so the goal card ends on top. The
    /// shuffle answer is no while the seen cards hold the goal card.</summary>
    [Fact]
    public void A_look_keeps_the_goal_card_on_top_for_the_draw()
    {
        var pilot = new GoalAwarePilot(NeedleByTwo);
        var view = View([]);

        Assert.Equal(PickOption.KeepRestId, pilot.Pick(view, Pick(PickPurpose.Scry, [Needle], [Needle], PickOption.KeepRestId)));
        Assert.Equal("Sol Ring", pilot.Pick(view, Pick(PickPurpose.Scry, [SolRing, Needle], [SolRing, Needle], PickOption.KeepRestId)));

        var top = new List<DeckCard> { Forest, Swamp, Needle };
        var under = new List<string>();
        while (top.Count > 1)
        {
            var picked = pilot.Pick(view, Pick(PickPurpose.OrderTop, top, null));
            under.Add(picked);
            top.RemoveAll(c => c.Name == picked);
        }

        Assert.Equal(["Forest", "Swamp"], under);
        Assert.Equal(PickOption.NoShuffleId, pilot.Pick(view, Pick(PickPurpose.Shuffle, [], [Forest, Swamp, Needle], PickOption.ShuffleId, PickOption.NoShuffleId)));
        Assert.Equal(PickOption.ShuffleId, pilot.Pick(view, Pick(PickPurpose.Shuffle, [], [Forest, Swamp, Island], PickOption.ShuffleId, PickOption.NoShuffleId)));
    }

    // The facts below are ported from the goldfish-state pilot tests at cutover.
    private static readonly DeckCard Vampiric = TestDeckCards.Spell("Vampiric Tutor", "{B}", "Instant");
    private static readonly DeckCard MindStone = TestDeckCards.Spell("Mind Stone", "{2}", "Artifact");
    private static readonly DeckCard SignInBlood = TestDeckCards.Spell("Sign in Blood", "{B}{B}");
    private static readonly DeckCard NeedleField = TestDeckCards.Land("Needle Field", "B") with { IsBasic = false, Supertypes = [] };
    private static readonly DeckCard Wilds = TestDeckCards.Land("Evolving Wilds", "C") with { IsBasic = false, Supertypes = [], ProducedColors = [] };

    private static readonly IReadOnlyDictionary<string, CardFeatureProfile> MoreProfiles = new Dictionary<string, CardFeatureProfile>
    {
        ["Vampiric Tutor"] = CardFeatureProfile.None with { Tutor = new TutorFeature(TutorDestination.LibraryTop, false, TutorVia.Cast, null) },
        ["Mind Stone"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{C}"], HasActivatedAbility = true },
        ["Sol Ring"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{C}{C}"], HasActivatedAbility = true },
        ["Needle Field"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{B}"], Tutor = new TutorFeature(TutorDestination.Hand, false, TutorVia.ActivateOnBattlefield, null), HasActivatedAbility = true },
        ["Shatterskull, the Hammer Pass"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{R}"] },
    };

    private static CardFeatureProfile AllFeatures(DeckCard face)
        => MoreProfiles.GetValueOrDefault(face.Name) ?? Features(face);

    /// <summary>A main-phase view with <paramref name="battlefield"/> in
    /// play, each permanent untapped, and every profile of this file.</summary>
    private static PilotView Board(
        IReadOnlyList<DeckCard> battlefield,
        IReadOnlyList<DeckCard> hand,
        IReadOnlyDictionary<string, IReadOnlyList<DeckCard>>? finds = null,
        IReadOnlyList<EngineAction>? actions = null,
        int turn = 1)
    {
        var permanents = battlefield.Select((c, i) => new ViewPermanent($"p{i}", c, false)).ToList();
        IReadOnlyList<BoardManaSource> sources = [.. permanents.Where(p => AllFeatures(p.Card).TapMana.Count > 0).Select(p => new BoardManaSource(p.Id, AllFeatures(p.Card).TapMana))];
        return View(hand, sources, finds, actions) with { Turn = turn, Battlefield = permanents, FeatureLookup = AllFeatures };
    }

    private static EngineAction CastOf(string id, DeckCard card)
        => new(id, EngineActionKind.Cast, card, ManaCost.Parse(card.ManaCost), null, AllFeatures(card), [.. AllFeatures(card).TapMana], false);

    private static EngineAction PlayOf(string id, DeckCard land)
        => new(id, EngineActionKind.PlayLand, land, ManaCost.Parse(null), null, AllFeatures(land), [.. AllFeatures(land).TapMana], false);

    /// <summary>A card a library-top tutor finds reaches hand at
    /// the next draw. The tutor is late on its leaf's deadline turn only.</summary>
    [Fact]
    public void A_library_top_tutor_ranks_as_a_tutor_except_on_the_deadline_turn_of_the_leaf_it_finds()
    {
        var finds = new Dictionary<string, IReadOnlyList<DeckCard>> { ["Vampiric Tutor"] = [Needle, Forest] };
        var cast = CastOf("v", Vampiric);
        var turnOne = Board([Swamp, Swamp], [Vampiric], finds, [cast]);

        // Turn 1: Needle reaches hand at the turn-2 draw, after the deadline.
        Assert.NotEqual(ActionTier.Tutor, GoalHeuristics.Tier(turnOne, NeedleByOne, cast));
        Assert.NotEqual(ActionTier.Tutor, GoalHeuristics.Classify(turnOne, NeedleByOne, Vampiric));

        // Turn 1 with a turn-2 deadline: the turn-2 draw is in time.
        Assert.Equal(ActionTier.Tutor, GoalHeuristics.Tier(turnOne, NeedleByTwo, cast));

        // Turn 3 after the turn-1 goal failed: Needle still counts for the curve.
        var turnThree = turnOne with { Turn = 3 };
        Assert.Equal(ActionTier.Tutor, GoalHeuristics.Tier(turnThree, NeedleByOne, cast));
        Assert.Equal(ActionTier.Tutor, GoalHeuristics.Classify(turnThree, NeedleByOne, Vampiric));
    }

    /// <summary>A Saga's chapter search resolves DelayTurns after the land
    /// drop. Played on turn 1, Urza's Saga finds Needle on turn 3: late for
    /// a turn-1 or turn-2 leaf, in time for a turn-3 leaf, and still a
    /// tutor for the curve after the deadline.</summary>
    [Fact]
    public void A_saga_chapter_tutor_ranks_as_a_tutor_only_when_its_chapter_resolves_in_time()
    {
        var saga = TestDeckCards.Land("Urza's Saga", "C") with { IsBasic = false, Supertypes = [] };
        var chapter = new TutorFeature(TutorDestination.Battlefield, false, TutorVia.SagaChapter, null, DelayTurns: 2);
        var profile = CardFeatureProfile.None with { Develops = true, TapMana = ["{C}"], Tutor = chapter };
        var finds = new Dictionary<string, IReadOnlyList<DeckCard>> { ["Urza's Saga"] = [Needle] };
        var play = new EngineAction("s", EngineActionKind.PlayLand, saga, ManaCost.Parse(null), null, profile, ["{C}"], false);
        var turnOne = Board([], [saga], finds, [play]) with { FeatureLookup = face => face.Name == "Urza's Saga" ? profile : AllFeatures(face) };

        Assert.NotEqual(ActionTier.Tutor, GoalHeuristics.Tier(turnOne, NeedleByOne, play));
        Assert.NotEqual(ActionTier.Tutor, GoalHeuristics.Tier(turnOne, NeedleByTwo, play));
        Assert.Equal(ActionTier.Tutor, GoalHeuristics.Tier(turnOne, new GoalPredicate.OnBattlefield("Pithing Needle", 3), play));

        // Turn 2 after the turn-1 goal failed: Needle still counts for the curve.
        Assert.Equal(ActionTier.Tutor, GoalHeuristics.Tier(turnOne with { Turn = 2 }, NeedleByOne, play));
    }

    /// <summary>An all_of times a library-top tutor by the
    /// leaves it can find.</summary>
    [Fact]
    public void An_all_of_times_a_library_top_tutor_by_the_leaves_it_can_find()
    {
        var goal = new GoalPredicate.AllOf([NeedleByOne, new GoalPredicate.OnBattlefield("Sol Ring", 3)]);

        // Sol Ring is already on the battlefield, so only the turn-1 Needle leaf is open.
        var ringHeld = Board([Swamp, SolRing], [Vampiric], new Dictionary<string, IReadOnlyList<DeckCard>> { ["Vampiric Tutor"] = [Needle, Forest] });
        Assert.NotEqual(ActionTier.Tutor, GoalHeuristics.Classify(ringHeld, goal, Vampiric));

        // Sol Ring is in the library: the turn-3 leaf makes Vampiric a tutor on turn 1.
        var ringInLibrary = Board([Swamp], [Vampiric], new Dictionary<string, IReadOnlyList<DeckCard>> { ["Vampiric Tutor"] = [Needle, SolRing] });
        Assert.Equal(ActionTier.Tutor, GoalHeuristics.Classify(ringInLibrary, goal, Vampiric));
    }

    [Fact]
    public void A_tutor_that_can_find_a_goal_piece_beats_ramp_and_the_land_drop()
    {
        var pilot = new GoalAwarePilot(NeedleByTwo);
        EngineAction[] actions = [PlayOf("swamp", Swamp), CastOf("stone", MindStone), CastOf("tutor", Tutor)];
        DeckCard[] hand = [Swamp, MindStone, Tutor];

        var withNeedle = Board([Swamp, Swamp, Swamp], hand, new Dictionary<string, IReadOnlyList<DeckCard>> { ["Demonic Tutor"] = [Needle, Forest] }, actions);
        Assert.Equal("tutor", pilot.ChooseAction(withNeedle));

        var noNeedle = Board([Swamp, Swamp, Swamp], hand, new Dictionary<string, IReadOnlyList<DeckCard>> { ["Demonic Tutor"] = [Forest] }, actions);
        Assert.Equal("swamp", pilot.ChooseAction(noNeedle));
    }

    [Fact]
    public void A_land_that_can_tutor_for_a_goal_piece_outranks_the_plain_land_drop()
    {
        var finds = new Dictionary<string, IReadOnlyList<DeckCard>> { ["Needle Field"] = [Needle] };
        var field = PlayOf("field", NeedleField);
        var view = Board([], [Swamp, NeedleField], finds, [PlayOf("swamp", Swamp), field]);

        Assert.Equal(ActionTier.Tutor, GoalHeuristics.Tier(view, NeedleByTwo, field));
        Assert.Equal("field", new GoalAwarePilot(NeedleByTwo).ChooseAction(view));
    }

    [Fact]
    public void A_search_picks_the_goal_piece_then_a_land_of_a_needed_color()
    {
        var pilot = new GoalAwarePilot(new GoalPredicate.Cast("Sign in Blood", 3));
        var lands = Pick(PickPurpose.Tutor, [Forest, Island, Swamp], null, PickOption.FindNothingId);

        // Sign in Blood in hand needs {B}{B}, and only a Forest is out.
        Assert.Equal("Swamp", pilot.Pick(Board([Forest], [SignInBlood, Wilds]), lands));

        var tutor = Pick(PickPurpose.Tutor, [Forest, SignInBlood, SolRing], null);
        Assert.Equal("Sign in Blood", pilot.Pick(Board([Swamp, Swamp], [Tutor]), tutor));
    }

    [Fact]
    public void A_search_reads_the_colors_a_goal_piece_in_the_command_zone_needs()
    {
        // The commander is never in hand. Its pips still decide the land a fetch finds.
        var pilot = new GoalAwarePilot(new GoalPredicate.Cast("Sign in Blood", 3));
        var view = Board([Forest], [Wilds]) with { CommandZone = [SignInBlood with { IsCommander = true }] };

        Assert.Equal("Swamp", pilot.Pick(view, Pick(PickPurpose.Tutor, [Forest, Island, Swamp], null)));
    }

    [Fact]
    public void The_cleanup_discard_and_put_back_keep_goal_pieces_and_shed_the_spell_that_costs_most()
    {
        var pilot = new GoalAwarePilot(NeedleByTwo);
        var hand = Board([], [Swamp, GrayOgre, Needle, SignInBlood, Tutor]);

        Assert.Equal([GrayOgre, SignInBlood], pilot.ChooseDiscards(hand, 2));

        // Brainstorm drew three Swamps: the put-back takes the Ogre, then a Swamp on top of it.
        var options = new PickChoice(PickPurpose.PutBack, [new("o", "Gray Ogre", GrayOgre), new("s1", "Swamp", Swamp), new("s2", "Swamp", Swamp), new("s3", "Swamp", Swamp)], null);
        var afterBrainstorm = Board([Island, Island], [GrayOgre, Swamp, Swamp, Swamp]);
        Assert.Equal("o", pilot.Pick(afterBrainstorm, options));

        var rest = options with { Options = options.Options.Skip(1).ToList() };
        Assert.Equal("s1", pilot.Pick(afterBrainstorm with { Hand = [Swamp, Swamp, Swamp] }, rest));
    }

    [Fact]
    public void Simultaneous_triggers_resolve_the_most_useful_source_first()
    {
        // Two upkeep triggers, one on a goal piece: the goal piece's trigger
        // resolves first whatever its position on the battlefield.
        var plain = TestDeckCards.Spell("Plain Drawer", "{1}", "Artifact");
        var pilot = new GoalAwarePilot(new GoalPredicate.Cast("Sign in Blood", 3));
        var view = Board([plain, SignInBlood], []) with { Step = TurnStep.Upkeep };

        Assert.Equal("Sign in Blood", pilot.Pick(view, Pick(PickPurpose.TriggerOrder, [plain, SignInBlood], null)));
    }

    [Fact]
    public void The_pilot_never_activates_a_mana_ability_on_its_own()
    {
        var ring = new EngineAction("ring", EngineActionKind.Activate, SolRing, ManaCost.Parse(null), "p0", CardFeatureProfile.None, ["{C}{C}"], true);
        var swamp = new EngineAction("swamp", EngineActionKind.Activate, Swamp, ManaCost.Parse(null), "p1", CardFeatureProfile.None, ["{B}"], true);
        var view = Board([SolRing, Swamp], [], actions: [ring, swamp]);

        Assert.Null(GoalHeuristics.Tier(view, NeedleByTwo, ring));
        Assert.Null(new GoalAwarePilot(NeedleByTwo).ChooseAction(view));

        // A fetch's search is no mana ability: it ranks as ramp.
        var wilds = new EngineAction("wilds", EngineActionKind.Activate, Wilds, ManaCost.Parse(null), "p2", CardFeatureProfile.None with { Develops = true }, [], true);
        Assert.Equal(ActionTier.Ramp, GoalHeuristics.Tier(view with { Actions = [ring, swamp, wilds] }, NeedleByTwo, wilds));
    }

    [Fact]
    public void Needle_names_a_card_whose_activated_abilities_the_lock_cannot_touch()
    {
        var pilot = new GoalAwarePilot(NeedleByTwo);
        var view = Board([Swamp], []);

        Assert.Equal("Gray Ogre", pilot.Pick(view, Pick(PickPurpose.Name, [SolRing, Fetch, GrayOgre], null)));
        Assert.Equal("Sol Ring", pilot.Pick(view, Pick(PickPurpose.Name, [SolRing, Fetch], null)));
    }

    /// <summary>A scry of one land keeps it only when the hand
    /// holds no land.</summary>
    [Theory]
    [InlineData(false, PickOption.KeepRestId)]
    [InlineData(true, "Forest")]
    public void A_scry_keeps_one_land_only_when_the_hand_holds_none(bool landInHand, string picked)
    {
        var view = Board([Island], landInHand ? [Swamp] : []);

        Assert.Equal(picked, new GoalAwarePilot(NeedleByTwo).Pick(view, Pick(PickPurpose.Scry, [Forest], [Forest], PickOption.KeepRestId)));
    }

    /// <summary>Sign in Blood needs {B}{B} and only an Island is
    /// out, so a scry of two lands bottoms the Forest and keeps the Swamp.</summary>
    [Fact]
    public void A_scry_keeps_one_of_two_lands_and_the_needed_color_first()
    {
        var pilot = new GoalAwarePilot(new GoalPredicate.Cast("Sign in Blood", 3));
        var view = Board([Island], [SignInBlood]);

        Assert.Equal("Forest", pilot.Pick(view, Pick(PickPurpose.Scry, [Forest, Swamp], [Forest, Swamp], PickOption.KeepRestId)));
        Assert.Equal(PickOption.KeepRestId, pilot.Pick(view, Pick(PickPurpose.Scry, [Swamp], [Swamp], PickOption.KeepRestId)));
    }

    /// <summary>The spell face of an MDFC land reads its own
    /// profile, not the land face's mana.</summary>
    [Fact]
    public void The_spell_face_of_an_MDFC_land_is_not_ramp()
    {
        var goal = new GoalPredicate.OnBattlefield("Grizzly Bears", 6);
        var view = Board([], [Shatterskull]);

        Assert.True(AllFeatures(Shatterskull.AsBackFace()!).Develops);
        Assert.NotEqual(ActionTier.Ramp, GoalHeuristics.Classify(view, goal, Shatterskull));
        Assert.NotEqual(ActionTier.Ramp, GoalHeuristics.Tier(view, goal, CastOf("smash", Shatterskull)));
    }
}
