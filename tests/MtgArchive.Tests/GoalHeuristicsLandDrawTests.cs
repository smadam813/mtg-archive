using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>Phase 3E: the pilot does not spend a land to draw a card until the goal is met (Fiery Islet on XMage).</summary>
public sealed class GoalHeuristicsLandDrawTests
{
    private static readonly GoalPredicate ObekaByFour = new GoalPredicate.Cast("Obeka, Brute Chronologist", 4);

    private static readonly DeckCard FieryIslet = TestDeckCards.Land("Fiery Islet", "U") with { ProducedColors = ["U", "R"] };

    private static PilotView View(int turn, params EngineAction[] actions) => new(
        turn, TurnStep.Main, 40, false, new ManaBoard(new ManaPool(), 40, [], []), [], [], [], [], [], [], new Dictionary<string, IReadOnlyList<DeckCard>>(), actions, _ => CardFeatureProfile.None);

    private static EngineAction Activation(DeckCard face, CardFeatureProfile features, bool spendsSource = true) =>
        new("a:1", EngineActionKind.Activate, face, ManaCost.Parse("{1}"), "p:1", features, [], spendsSource);

    private static readonly CardFeatureProfile Draws = CardFeatureProfile.None with { Draws = true };

    [Fact]
    public void A_land_draw_before_the_goal_turn_is_no_action()
    {
        var islet = Activation(FieryIslet, Draws);

        Assert.Null(GoalHeuristics.Tier(View(2, islet), ObekaByFour, islet));
    }

    /// <summary>The user's decision: the draw waits until the goal is met.</summary>
    [Fact]
    public void A_land_draw_on_the_goal_turn_waits_for_the_goal()
    {
        var islet = Activation(FieryIslet, Draws);

        Assert.Null(GoalHeuristics.Tier(View(4, islet), ObekaByFour, islet));
    }

    [Fact]
    public void A_land_draw_after_the_goal_is_met_is_a_draw()
    {
        var islet = Activation(FieryIslet, Draws);
        var cast = new ResolvedCast(TestDeckCards.Spell("Obeka, Brute Chronologist", "{2}{U}{R}", "Creature"), 4);
        var view = View(4, islet) with { ResolvedCasts = [cast] };

        Assert.Equal(ActionTier.Draw, GoalHeuristics.Tier(view, ObekaByFour, islet));
    }

    [Fact]
    public void A_land_draw_after_the_goal_turn_is_a_draw()
    {
        var islet = Activation(FieryIslet, Draws);

        Assert.Equal(ActionTier.Draw, GoalHeuristics.Tier(View(5, islet), ObekaByFour, islet));
    }

    [Fact]
    public void A_land_draw_that_keeps_the_land_is_a_draw_before_the_goal_turn()
    {
        var land = Activation(FieryIslet, Draws, spendsSource: false);

        Assert.Equal(ActionTier.Draw, GoalHeuristics.Tier(View(2, land), ObekaByFour, land));
    }

    [Fact]
    public void A_nonland_draw_before_the_goal_turn_is_still_a_draw()
    {
        var stone = TestDeckCards.Spell("Mind Stone", "{2}", "Artifact");
        var draw = Activation(stone, Draws);

        Assert.Equal(ActionTier.Draw, GoalHeuristics.Tier(View(2, draw), ObekaByFour, draw));
    }

    /// <summary>Phase 3E, Review Focus 5: a fetch's search stays a tutor, or ramp, whatever the turn.</summary>
    [Fact]
    public void A_fetch_is_still_a_tutor()
    {
        var fetch = Activation(TestDeckCards.Land("Polluted Delta", "C") with { ProducedColors = [] },
            CardFeatureProfile.None with { Develops = true });

        Assert.Equal(ActionTier.Ramp, GoalHeuristics.Tier(View(2, fetch), ObekaByFour, fetch));
    }
}
