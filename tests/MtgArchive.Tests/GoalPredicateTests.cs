using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// Goal predicates: the closed vocabulary and its wire parsing,
/// point-in-time <c>on_battlefield</c>, resolved-casts-only <c>cast</c>,
/// <c>all_of</c> composition, and the per-turn evaluation that feeds the
/// curve and the run's verdict.
/// </summary>
public sealed class GoalPredicateTests
{
    private static readonly DeckCard Needle = TestDeckCards.Spell("Pithing Needle", "{1}", "Artifact");
    private static readonly DeckCard Ritual = TestDeckCards.Spell("Dark Ritual", "{B}", "Instant");

    private static GoalPredicate Parse(string json)
    {
        var errors = new List<string>();
        var goal = GoalPredicateParser.Parse(json, errors);
        Assert.Empty(errors);
        return goal!;
    }

    private static IReadOnlyList<string> Errors(string json)
    {
        var errors = new List<string>();
        Assert.Null(GoalPredicateParser.Parse(json, errors));
        Assert.NotEmpty(errors);
        return errors;
    }

    [Fact]
    public void The_vocabulary_is_closed_at_on_battlefield_cast_and_all_of()
    {
        Assert.Equal(["on_battlefield", "cast", "all_of"], GoalPredicate.Vocabulary);

        Assert.Equal(new GoalPredicate.OnBattlefield("Pithing Needle", 1),
            Parse("""{ "kind": "on_battlefield", "card": "Pithing Needle", "by_turn": 1 }"""));
        Assert.Equal(new GoalPredicate.Cast("Dark Ritual", 3),
            Parse("""{ "kind": "cast", "card": "Dark Ritual", "by_turn": 3 }"""));

        var composed = Parse("""
            { "kind": "all_of", "goals": [
                { "kind": "on_battlefield", "card": "Pithing Needle", "by_turn": 2 },
                { "kind": "cast", "card": "Dark Ritual", "by_turn": 4 } ] }
            """);
        Assert.Equal(new GoalPredicate.AllOf([new GoalPredicate.OnBattlefield("Pithing Needle", 2), new GoalPredicate.Cast("Dark Ritual", 4)]), composed);
        Assert.Equal(4, composed.LastTurn);
        Assert.Equal(["Pithing Needle", "Dark Ritual"], composed.CardNames);
        Assert.Equal("all_of(on_battlefield(\"Pithing Needle\", by_turn 2), cast(\"Dark Ritual\", by_turn 4))", composed.ToString());
    }

    [Fact]
    public void An_unknown_predicate_name_is_a_hard_failure_that_names_the_vocabulary()
    {
        var error = Assert.Single(Errors("""{ "kind": "in_graveyard", "card": "Dark Ritual", "by_turn": 3 }"""));
        Assert.Equal("goal: $.kind: 'in_graveyard' is not one of: on_battlefield, cast, all_of", error);

        var nested = Assert.Single(Errors("""{ "kind": "all_of", "goals": [ { "kind": "cast", "card": "Dark Ritual", "by_turn": 3 }, { "kind": "any_of", "goals": [] } ] }"""));
        Assert.StartsWith("goal: $.goals[1].kind: 'any_of' is not one of", nested);
    }

    [Fact]
    public void Malformed_predicates_report_every_problem_with_its_path()
    {
        // The messages come from the shared shape checks.
        Assert.Equal(["goal: $: must be an object"], Errors("[]"));
        Assert.Equal(["goal: $: missing required property 'kind'"], Errors("""{ "card": "Dark Ritual" }"""));
        Assert.Equal(
            [
                "goal: $: unknown property 'turn' (allowed: by_turn, card, kind)",
                "goal: $.card: must be a non-empty string",
                "goal: $: missing required property 'by_turn'",
            ],
            Errors("""{ "kind": "cast", "card": "", "turn": 2 }"""));
        Assert.Equal(["goal: $.by_turn: must be an integer >= 1"], Errors("""{ "kind": "cast", "card": "Dark Ritual", "by_turn": 0 }"""));
        Assert.Equal(["goal: $.goals: must not be empty"], Errors("""{ "kind": "all_of", "goals": [] }"""));
        Assert.Equal(["goal: $.goals[0]: must be an object"], Errors("""{ "kind": "all_of", "goals": [ 1 ] }"""));
        Assert.StartsWith("goal: invalid JSON", Assert.Single(Errors("{ not json")));
    }

    [Fact]
    public void The_met_turn_is_the_turn_the_last_leaf_first_held()
    {
        var ring = TestDeckCards.Spell("Sol Ring", "{1}", "Artifact");
        var progress = new GoalProgress(new GoalPredicate.AllOf([new GoalPredicate.OnBattlefield("Pithing Needle", 1), new GoalPredicate.OnBattlefield("Sol Ring", 3)]));

        progress.EndOfTurn(new FakeBoard(1, [Needle], []), _ => { });
        Assert.Null(progress.MetTurn);

        progress.EndOfTurn(new FakeBoard(2, [], []), _ => { });
        progress.EndOfTurn(new FakeBoard(3, [ring], []), _ => { });

        Assert.True(progress.Verdict);
        Assert.Equal([false, false, false], progress.Curve);
        Assert.Equal(3, progress.MetTurn);
    }

    /// <summary>An engine-neutral board: the goal reads only these three members.</summary>
    private sealed record FakeBoard(int Turn, IEnumerable<DeckCard> BattlefieldCards, IReadOnlyList<ResolvedCast> ResolvedCasts) : IGoalBoard;

    [Fact]
    public void A_goal_reads_any_goal_board()
    {
        var goal = new GoalPredicate.AllOf([new GoalPredicate.OnBattlefield("Pithing Needle", 2), new GoalPredicate.Cast("Dark Ritual", 2)]);

        Assert.False(goal.HoldsNow(new FakeBoard(1, [Needle], [])));
        Assert.True(goal.HoldsNow(new FakeBoard(1, [Needle], [new ResolvedCast(Ritual, 1)])));
        Assert.Equal(Ritual, goal.WantedFace(new FakeBoard(1, [Needle], []), Ritual));
        Assert.False(goal.Wants(new FakeBoard(1, [Needle], []), Needle));
    }

    [Fact]
    public void The_goal_is_settled_once_the_verdict_and_the_met_turn_are_both_set()
    {
        var ring = TestDeckCards.Spell("Sol Ring", "{1}", "Artifact");
        var progress = new GoalProgress(new GoalPredicate.AllOf([new GoalPredicate.OnBattlefield("Pithing Needle", 1), new GoalPredicate.OnBattlefield("Sol Ring", 3)]));
        var lines = new List<string>();

        progress.EndOfTurn(new FakeBoard(1, [Needle], []), lines.Add);
        Assert.Null(progress.Verdict);
        Assert.False(progress.IsSettled);

        progress.EndOfTurn(new FakeBoard(3, [Needle, ring], []), lines.Add);
        Assert.True(progress.Verdict);
        Assert.Equal(3, progress.MetTurn);
        Assert.True(progress.IsSettled);
        Assert.Contains("Goal met: " + progress.Goal + ".", lines);
    }

    [Fact]
    public void A_missed_goal_is_settled_only_once_every_leaf_has_held()
    {
        // The verdict fails on turn 1, but the curve still counts a later cast.
        var progress = new GoalProgress(new GoalPredicate.Cast("Dark Ritual", 1));

        progress.EndOfTurn(new FakeBoard(1, [], []), _ => { });
        Assert.False(progress.Verdict);
        Assert.Null(progress.MetTurn);
        Assert.False(progress.IsSettled);

        progress.EndOfTurn(new FakeBoard(2, [], [new ResolvedCast(Ritual, 2)]), _ => { });
        Assert.Equal(2, progress.MetTurn);
        Assert.True(progress.IsSettled);
    }
}
