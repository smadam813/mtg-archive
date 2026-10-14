using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// The goal-aware hold policy (ADR 0014) on pilot views built by
/// hand. The card hold keeps a response card whose only rank is "anything
/// else" in hand. The reserve keeps untapped mana worth the cheapest
/// answering response card, only while the whole goal holds.
/// </summary>
public sealed class GoalAwareHoldPilotTests
{
    private static readonly DeckCard Needle = TestDeckCards.Spell("Pithing Needle", "{1}", "Artifact");
    private static readonly DeckCard Bear = TestDeckCards.Spell("Grizzly Bears", "{1}{G}", "Creature");
    private static readonly DeckCard Rock = TestDeckCards.Spell("Plain Rock", "{2}", "Artifact");
    private static readonly DeckCard Bolt = TestDeckCards.Spell("Plain Bolt", "{1}{G}", "Instant");
    private static readonly DeckCard HeroicIntervention = TestDeckCards.Spell("Heroic Intervention", "{1}{G}", "Instant");
    private static readonly DeckCard CantripVeil = TestDeckCards.Spell("Cantrip Veil", "{G}", "Instant");
    private static readonly DeckCard RampVeil = TestDeckCards.Spell("Ramp Veil", "{G}", "Instant");
    private static readonly DeckCard Counterspell = TestDeckCards.Spell("Counterspell", "{U}{U}", "Instant");
    private static readonly DeckCard Pyroblast = TestDeckCards.Spell("Pyroblast", "{R}", "Instant");
    private static readonly DeckCard RedWard = TestDeckCards.Spell("Red Ward", "{G}", "Instant");
    private static readonly DeckCard SnakeskinVeil = TestDeckCards.Spell("Snakeskin Veil", "{G}", "Instant");

    private static readonly GoalPredicate NeedleByThree = new GoalPredicate.OnBattlefield("Pithing Needle", 3);

    private static ResponseFeature Grants(params (string Grant, string Scope)[] grants)
        => new(null, [.. grants.Select(g => new ProtectionGrant(g.Grant, g.Scope))]);

    private static readonly ResponseFeature TeamProtection = Grants((ProtectionGrant.Hexproof, ProtectionGrant.Permanents), (ProtectionGrant.Indestructible, ProtectionGrant.Permanents));

    private static CardFeatureProfile Features(DeckCard face) => face.Name switch
    {
        "Heroic Intervention" => CardFeatureProfile.None with { Response = TeamProtection },
        "Cantrip Veil" => CardFeatureProfile.None with { Draws = true, Response = TeamProtection },
        "Ramp Veil" => CardFeatureProfile.None with { Develops = true, Response = TeamProtection },
        "Red Ward" => CardFeatureProfile.None with { Response = Grants((ProtectionGrant.ProtectionFrom("R"), ProtectionGrant.TargetCreature)) },
        "Snakeskin Veil" => CardFeatureProfile.None with { Response = Grants((ProtectionGrant.Hexproof, ProtectionGrant.TargetCreature)) },
        "Counterspell" => CardFeatureProfile.None with { Response = new ResponseFeature(new CounterFeature(CounterReach.Spell, [], [], [], null, false), []) },
        "Pyroblast" => CardFeatureProfile.None with { Response = new ResponseFeature(new CounterFeature(CounterReach.Spell, [], [], ["U"], null, false), []) },
        _ => CardFeatureProfile.None,
    };

    private static EngineAction Cast(DeckCard card)
        => new($"a:{card.Name}", EngineActionKind.Cast, card, ManaCost.Parse(card.ManaCost), null, Features(card), [], false);

    /// <summary>Turn 3, <paramref name="lands"/> untapped lands, the hand
    /// <paramref name="hand"/>, and a cast action for each hand card.</summary>
    private static PilotView View(int lands, IReadOnlyList<DeckCard> battlefield, params DeckCard[] hand) => new(
        Turn: 3,
        TurnStep.Main,
        Life: 40,
        LandDropUsed: true,
        new ManaBoard(new ManaPool(), 40, [.. Enumerable.Range(1, lands).Select(i => new BoardManaSource($"l:{i}", ["{G}"]))], []),
        hand,
        [.. battlefield.Select((c, i) => new ViewPermanent($"p:{i}", c, false))],
        [],
        [],
        [],
        [],
        new Dictionary<string, IReadOnlyList<DeckCard>>(),
        [.. hand.Select(Cast)],
        Features);

    private static readonly GoalAwarePilot Greedy = new(NeedleByThree);

    private static readonly GoalAwareHoldPilot Hold = new(NeedleByThree);

    [Fact]
    public void The_policy_names_itself_and_its_goal()
    {
        Assert.Equal("goal-aware-hold", Hold.Name);
        Assert.Equal(NeedleByThree, ((IGoalDirected)Hold).Goal);
        Assert.Equal(["goal-aware-greedy", "goal-aware-hold"], PlayPolicies.Names);
        Assert.IsType<GoalAwareHoldPilot>(PlayPolicies.Create("goal-aware-hold", NeedleByThree));
        Assert.IsType<GoalAwarePilot>(PlayPolicies.Create("goal-aware-greedy", NeedleByThree));
    }

    [Fact]
    public void A_response_card_with_no_rank_above_anything_else_stays_in_hand_from_turn_1()
    {
        var view = View(4, [], HeroicIntervention);

        Assert.Equal("a:Heroic Intervention", Greedy.ChooseAction(view));
        Assert.Null(Hold.ChooseAction(view));
    }

    [Fact]
    public void A_response_card_that_draws_or_ramps_plays_as_the_default_policy_does()
    {
        Assert.Equal("a:Cantrip Veil", Hold.ChooseAction(View(4, [], CantripVeil)));
        Assert.Equal("a:Ramp Veil", Hold.ChooseAction(View(4, [], RampVeil)));
    }

    [Fact]
    public void The_hold_skips_only_the_response_card_and_takes_the_next_best_action()
    {
        var view = View(4, [], HeroicIntervention, Bear);

        Assert.Equal("a:Heroic Intervention", Greedy.ChooseAction(view));
        Assert.Equal("a:Grizzly Bears", Hold.ChooseAction(view));
    }

    [Fact]
    public void A_card_that_answers_no_disruption_card_is_not_held()
    {
        // Protection from red answers none of Counterspell, Atomize and Rout.
        Assert.Equal("a:Red Ward", Hold.ChooseAction(View(4, [], RedWard)));

        // Snakeskin Veil answers Atomize, Counterspell is blue and Pyroblast counters it: all held.
        Assert.Null(Hold.ChooseAction(View(4, [], SnakeskinVeil)));
        Assert.Null(Hold.ChooseAction(View(4, [], Counterspell)));
        Assert.Null(Hold.ChooseAction(View(4, [], Pyroblast)));

        // A card with no response feature plays.
        Assert.Equal("a:Plain Bolt", Hold.ChooseAction(View(4, [], Bolt)));
    }

    /// <summary>Four lands. The Bear costs 2, so two lands stay: exactly the
    /// reserve of a Heroic Intervention (mana value 2).</summary>
    [Fact]
    public void While_the_goal_holds_an_action_must_leave_the_reserve()
    {
        var needle = new[] { Needle };

        Assert.Equal("a:Grizzly Bears", Hold.ChooseAction(View(4, needle, HeroicIntervention, Bear)));
        Assert.Null(Hold.ChooseAction(View(3, needle, HeroicIntervention, Bear)));
        // The default policy taps out.
        Assert.Equal("a:Heroic Intervention", Greedy.ChooseAction(View(3, needle, HeroicIntervention, Bear)));
    }

    [Fact]
    public void The_reserve_is_the_cheapest_answering_card_in_hand()
    {
        var needle = new[] { Needle };

        // Snakeskin Veil covers a creature only, and the goal card is an artifact: no reserve from it.
        Assert.Equal("a:Grizzly Bears", Hold.ChooseAction(View(2, needle, SnakeskinVeil, Bear)));
        // Counterspell (mana value 2) counts as a counter of the removal: it reserves 2.
        Assert.Null(Hold.ChooseAction(View(3, needle, Counterspell, Rock)));
        Assert.Equal("a:Plain Rock", Hold.ChooseAction(View(4, needle, Counterspell, Rock)));
    }

    [Fact]
    public void Before_the_goal_holds_and_after_a_hit_the_pilot_plays_as_the_default_policy()
    {
        // The Needle is not on the battlefield: no reserve, the Bear taps out the lands.
        Assert.Equal("a:Grizzly Bears", Hold.ChooseAction(View(2, [], HeroicIntervention, Bear)));

        // The goal held; a hit took the Needle: the reserve stops.
        Assert.Equal("a:Grizzly Bears", Hold.ChooseAction(View(2, [Bear], HeroicIntervention, Bear)));
    }

    [Fact]
    public void An_all_of_goal_starts_the_reserve_only_when_every_leaf_holds()
    {
        var goal = new GoalPredicate.AllOf([new GoalPredicate.OnBattlefield("Pithing Needle", 3), new GoalPredicate.OnBattlefield("Grizzly Bears", 3)]);
        var hold = new GoalAwareHoldPilot(goal);

        // Only the Needle holds: the Bear in hand is the goal action, and it taps out.
        Assert.Equal("a:Grizzly Bears", hold.ChooseAction(View(2, [Needle], HeroicIntervention, Bear)));
        // Both hold: the reserve starts, and the Rock would leave 0 of 2.
        Assert.Null(hold.ChooseAction(View(2, [Needle, Bear], HeroicIntervention, Rock)));
    }

    [Fact]
    public void With_no_answering_response_card_in_hand_the_pilot_holds_no_reserve()
    {
        var needle = new[] { Needle };

        Assert.Equal("a:Grizzly Bears", Hold.ChooseAction(View(2, needle, Bear)));
        Assert.Equal("a:Grizzly Bears", Hold.ChooseAction(View(2, needle, Pyroblast, Bear)));
    }

    [Fact]
    public void A_goal_whose_leaf_is_due_before_this_turn_reserves_nothing()
    {
        var hold = new GoalAwareHoldPilot(new GoalPredicate.OnBattlefield("Pithing Needle", 2));

        Assert.Equal("a:Grizzly Bears", hold.ChooseAction(View(2, [Needle], HeroicIntervention, Bear)));
    }

    [Fact]
    public void An_action_that_adds_mana_counts_the_mana_it_adds()
    {
        var needle = new[] { Needle };
        var view = View(3, needle, HeroicIntervention, Rock);
        var rock = view.Actions[1] with { ManaAdded = ["{C}{C}"] };
        view = view with { Actions = [view.Actions[0], rock] };

        // 3 lands - 2 for the Rock + 2 it adds = 3, at least the reserve 2.
        Assert.Equal("a:Plain Rock", Hold.ChooseAction(view));
    }

    [Fact]
    public void The_card_an_action_casts_does_not_reserve_mana_for_itself()
    {
        var needle = new[] { Needle };

        // The Veil is the only answer in hand. Once cast, it reserves nothing.
        Assert.Equal("a:Cantrip Veil", Hold.ChooseAction(View(1, needle, CantripVeil)));
        // A second answer stays in hand after the cast, so the reserve is 1 and the last land stays.
        Assert.Null(Hold.ChooseAction(View(1, needle, CantripVeil, CantripVeil)));
    }

    [Fact]
    public void A_response_card_cast_from_the_command_zone_is_held()
    {
        var view = View(4, [], Bear);
        var commander = new EngineAction("c:Heroic Intervention", EngineActionKind.CastCommander, HeroicIntervention, ManaCost.Parse(HeroicIntervention.ManaCost), null, Features(HeroicIntervention), [], false);
        view = view with { Actions = [.. view.Actions, commander] };

        Assert.Equal("a:Grizzly Bears", Hold.ChooseAction(view));
        Assert.Equal("c:Heroic Intervention", Greedy.ChooseAction(view with { Actions = [commander] }));
        Assert.Null(Hold.ChooseAction(view with { Actions = [commander] }));
    }

    [Fact]
    public void Every_other_choice_is_the_default_policys()
    {
        var view = View(4, [], Bear, Rock, HeroicIntervention);

        Assert.Equal(Greedy.ChooseDiscards(view, 2), Hold.ChooseDiscards(view, 2));
        var response = new ResponseChoice(DisruptionCards.Atomize, "s:9", [new ResponseVictim("p:1", Needle)]);
        var onBoard = View(4, [Needle], HeroicIntervention);
        Assert.Equal(Greedy.Respond(onBoard, response), Hold.Respond(onBoard, response));
        Assert.Equal("a:Heroic Intervention", Hold.Respond(onBoard, response));
    }
}
