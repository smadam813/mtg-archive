using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// The engine-neutral disruption schedule (spec section 7): it picks the
/// victims from the pilot view's battlefield and the goal board, keeps the
/// fired bookkeeping, and leaves the moves to the engine adapter. The
/// targeting rule is ADR 0005's: the most expensive goal card, then a name
/// tie-break.
/// </summary>
public sealed class DisruptionScheduleTests
{
    private static readonly DeckCard Needle = TestDeckCards.Spell("Pithing Needle", "{1}", "Artifact");
    private static readonly DeckCard Signet = TestDeckCards.Spell("Orzhov Signet", "{2}", "Artifact");
    private static readonly DeckCard Nighthawk = TestDeckCards.Spell("Vampire Nighthawk", "{1}{B}{B}", "Creature");
    private static readonly DeckCard Bear = TestDeckCards.Spell("Grizzly Bears", "{1}{B}", "Creature");
    private static readonly DeckCard Aardvark = TestDeckCards.Spell("Aardvark", "{1}", "Artifact");
    private static readonly DeckCard Zebra = TestDeckCards.Spell("Zebra", "{1}", "Artifact");
    private static readonly DeckCard Swamp = TestDeckCards.Land("Swamp", "B");

    private static readonly IReadOnlyList<DeckCard> Deck = [Needle, Signet, Nighthawk, Bear, Aardvark, Zebra, Swamp];

    private static DisruptionSchedule Schedule(GoalPredicate? goal, params (DisruptionKind Kind, int Turn)[] events)
        => new(new DisruptionProfile([.. events.Select(e => new DisruptionEvent(e.Kind, e.Turn))]), goal, Deck);

    private static IReadOnlyList<ViewPermanent> Battlefield(params DeckCard[] cards)
        => [.. cards.Select((card, index) => new ViewPermanent($"v{index}", card, false))];

    private sealed record Board(int Turn, IEnumerable<DeckCard> BattlefieldCards, IReadOnlyList<ResolvedCast> ResolvedCasts) : IGoalBoard;

    [Fact]
    public void Removal_takes_the_most_expensive_goal_permanent_then_a_name_tie_break()
    {
        var both = new GoalPredicate.AllOf([new GoalPredicate.OnBattlefield("Pithing Needle", 5), new GoalPredicate.OnBattlefield("Vampire Nighthawk", 5)]);
        var fire = Assert.Single(Schedule(both, (DisruptionKind.TargetedRemoval, 1)).EndOfTurn(1, Battlefield(Needle, Nighthawk, Signet)));
        Assert.Equal(["v1"], fire.Victims.Select(v => v.Id));
        Assert.Equal(["Vampire Nighthawk"], fire.Outcome.Victims);

        var tie = new GoalPredicate.AllOf([new GoalPredicate.OnBattlefield("Zebra", 5), new GoalPredicate.OnBattlefield("Aardvark", 5)]);
        Assert.Equal("Aardvark", Assert.Single(Assert.Single(Schedule(tie, (DisruptionKind.TargetedRemoval, 1)).EndOfTurn(1, Battlefield(Zebra, Aardvark, Signet))).Victims).Card.Name);
    }

    [Fact]
    public void Two_removals_on_one_turn_see_the_battlefield_the_first_one_left()
    {
        var schedule = Schedule(new GoalPredicate.OnBattlefield("Pithing Needle", 5), (DisruptionKind.TargetedRemoval, 1), (DisruptionKind.TargetedRemoval, 1));

        var fires = schedule.EndOfTurn(1, Battlefield(Swamp, Needle, Signet, Nighthawk));

        Assert.Equal(["Pithing Needle", "Vampire Nighthawk"], fires.Select(f => Assert.Single(f.Victims).Card.Name));
        Assert.All(schedule.Events, o => Assert.Equal(1, o.FiredTurn));
    }

    [Fact]
    public void An_event_waits_for_its_turn_and_a_target_and_fires_once()
    {
        var schedule = Schedule(new GoalPredicate.OnBattlefield("Pithing Needle", 5), (DisruptionKind.TargetedRemoval, 2));

        Assert.Empty(schedule.EndOfTurn(1, Battlefield(Needle)));
        Assert.Empty(schedule.EndOfTurn(2, Battlefield(Swamp)));
        Assert.Single(schedule.EndOfTurn(3, Battlefield(Needle)));
        Assert.Empty(schedule.EndOfTurn(4, Battlefield(Needle)));
        Assert.Equal(3, Assert.Single(schedule.Events).FiredTurn);
    }

    [Fact]
    public void A_board_wipe_waits_for_a_goal_creature_when_the_goal_names_one()
    {
        var schedule = Schedule(new GoalPredicate.OnBattlefield("Vampire Nighthawk", 5), (DisruptionKind.BoardWipe, 1));

        Assert.Empty(schedule.EndOfTurn(1, Battlefield(Bear, Signet)));
        var fire = Assert.Single(schedule.EndOfTurn(2, Battlefield(Bear, Signet, Nighthawk)));
        Assert.Equal(["Grizzly Bears", "Vampire Nighthawk"], fire.Outcome.Victims);
    }

    [Fact]
    public void A_counterspell_holds_while_a_goal_card_is_reachable()
    {
        var goal = new GoalPredicate.OnBattlefield("Pithing Needle", 5);
        var board = new Board(2, [], []);

        var holding = Schedule(goal, (DisruptionKind.Counterspell, 1));
        Assert.Null(holding.Counter(2, Signet, board, [Needle]));
        Assert.Equal(["Pithing Needle"], holding.Counter(2, Needle, board, [])!.Victims);

        var nothingLeft = Schedule(goal, (DisruptionKind.Counterspell, 1));
        Assert.Equal(["Orzhov Signet"], nothingLeft.Counter(2, Signet, board, [Signet])!.Victims);

        Assert.Null(Schedule(goal, (DisruptionKind.Counterspell, 3)).Counter(2, Needle, board, []));
    }

    /// <summary>Interactive disruption proposes through the
    /// unchanged tier-3 predicate and spends nothing until the commit.</summary>
    [Fact]
    public void An_interactive_counterspell_proposes_by_the_tier_3_predicate_and_spends_nothing_until_the_commit()
    {
        var goal = new GoalPredicate.OnBattlefield("Pithing Needle", 5);
        var board = new Board(2, [], []);
        var schedule = Schedule(goal, (DisruptionKind.Counterspell, 1));

        Assert.Null(schedule.ProposeCounter(2, Signet, board, [Needle]));
        var proposed = schedule.ProposeCounter(2, Needle, board, [])!;
        Assert.True(proposed.Unused);
        Assert.Same(proposed, schedule.ProposeCounter(2, Needle, board, []));

        schedule.Commit(proposed, 2, [new VictimHit("Pithing Needle", true)]);

        Assert.Equal(2, proposed.FiredTurn);
        Assert.Equal(["Pithing Needle"], proposed.Victims);
        Assert.Equal([new VictimHit("Pithing Needle", true)], proposed.Hits);
        Assert.Null(schedule.ProposeCounter(3, Needle, board, []));
    }

    [Fact]
    public void A_counterspell_that_was_not_cast_stays_armed_and_a_later_commit_clears_not_cast()
    {
        var goal = new GoalPredicate.OnBattlefield("Pithing Needle", 5);
        var board = new Board(2, [], []);
        var schedule = Schedule(goal, (DisruptionKind.Counterspell, 1));
        var proposed = schedule.ProposeCounter(2, Needle, board, [])!;

        schedule.NotCast(proposed, "no_target");

        Assert.True(proposed.NotCast);
        Assert.Equal("no_target", proposed.NotCastReason);
        Assert.Same(proposed, schedule.ProposeCounter(3, Needle, board, []));

        schedule.Commit(proposed, 3, [new VictimHit("Pithing Needle", false)]);

        Assert.False(proposed.NotCast);
        Assert.Equal(3, proposed.FiredTurn);
        Assert.Throws<InvalidOperationException>(() => schedule.NotCast(proposed, "cannot_cast"));
        Assert.Throws<ArgumentException>(() => schedule.Commit(new DisruptionOutcome(new DisruptionEvent(DisruptionKind.Counterspell, 1)), 3, []));
    }

    /// <summary>The removal order extends the tier-3 rule. Goal
    /// permanents come first, then every other nonland permanent, each by
    /// mana value descending, then by name. Its first entry is the tier-3
    /// target.</summary>
    [Fact]
    public void The_removal_order_lists_goal_permanents_then_every_other_nonland_permanent()
    {
        var goal = new GoalPredicate.AllOf([new GoalPredicate.OnBattlefield("Pithing Needle", 5), new GoalPredicate.OnBattlefield("Zebra", 5)]);
        var schedule = Schedule(goal, (DisruptionKind.TargetedRemoval, 1));
        var battlefield = Battlefield(Swamp, Aardvark, Needle, Bear, Zebra, Signet, Nighthawk);

        var order = schedule.RemovalOrder(battlefield);

        Assert.Equal(["Pithing Needle", "Zebra", "Vampire Nighthawk", "Grizzly Bears", "Orzhov Signet", "Aardvark"], order.Select(p => p.Card.Name));
        Assert.Same(order[0], Assert.Single(Assert.Single(Schedule(goal, (DisruptionKind.TargetedRemoval, 1)).EndOfTurn(1, battlefield)).Victims));
        Assert.Equal(["Vampire Nighthawk", "Grizzly Bears", "Orzhov Signet", "Aardvark", "Pithing Needle", "Zebra"],
            Schedule(null, (DisruptionKind.TargetedRemoval, 1)).RemovalOrder(battlefield).Select(p => p.Card.Name));
        Assert.Empty(schedule.RemovalOrder(Battlefield(Swamp)));
    }

    /// <summary>The end-step chain proposes the first armed
    /// removal in profile order that this step has not tried and that has a
    /// candidate. A proposal spends nothing; a not-cast event waits for the
    /// next end step.</summary>
    [Fact]
    public void An_interactive_end_step_proposes_each_armed_removal_once_and_spends_nothing()
    {
        var schedule = Schedule(new GoalPredicate.OnBattlefield("Pithing Needle", 5),
            (DisruptionKind.Counterspell, 1), (DisruptionKind.TargetedRemoval, 3), (DisruptionKind.TargetedRemoval, 2));
        var tried = new HashSet<DisruptionOutcome>();
        var counter = schedule.Events[0];
        var late = schedule.Events[1];
        var early = schedule.Events[2];

        Assert.Null(schedule.ProposeEndOfTurn(1, Battlefield(Needle), tried)); // nothing armed yet; a counterspell never proposes here
        Assert.Null(schedule.ProposeEndOfTurn(2, Battlefield(Swamp), tried)); // no candidate

        var first = schedule.ProposeEndOfTurn(3, Battlefield(Swamp, Signet, Needle), tried)!;
        Assert.Same(late, first.Outcome); // profile order, not unlock order
        Assert.Equal(["Pithing Needle", "Orzhov Signet"], first.Candidates.Select(c => c.Card.Name));
        Assert.True(late.Unused);
        Assert.True(counter.Unused);

        tried.Add(first.Outcome);
        schedule.NotCast(first.Outcome, "no_target");
        var second = schedule.ProposeEndOfTurn(3, Battlefield(Swamp, Signet, Needle), tried)!;
        Assert.Same(early, second.Outcome);
        tried.Add(second.Outcome);
        schedule.Commit(second.Outcome, 3, [new VictimHit("Pithing Needle", true)]);
        Assert.Null(schedule.ProposeEndOfTurn(3, Battlefield(Swamp, Signet), tried));

        // The next end step: the not-cast removal is armed again.
        Assert.Same(late, schedule.ProposeEndOfTurn(4, Battlefield(Signet), new HashSet<DisruptionOutcome>())!.Outcome);
    }

    /// <summary>A board wipe proposes under the unchanged
    /// tier-3 wipe condition, and its candidates are the creatures the wipe
    /// would take. Removal and a wipe due on one turn propose in profile
    /// order.</summary>
    [Fact]
    public void An_interactive_board_wipe_proposes_under_the_tier_3_wipe_condition_in_profile_order()
    {
        var schedule = Schedule(new GoalPredicate.OnBattlefield("Vampire Nighthawk", 5),
            (DisruptionKind.BoardWipe, 1), (DisruptionKind.TargetedRemoval, 1));
        var wipe = schedule.Events[0];
        var removal = schedule.Events[1];
        var tried = new HashSet<DisruptionOutcome>();

        // The goal names a creature, and none is on the battlefield: the wipe waits, the removal goes.
        Assert.Same(removal, schedule.ProposeEndOfTurn(1, Battlefield(Bear, Signet), tried)!.Outcome);

        var first = schedule.ProposeEndOfTurn(2, Battlefield(Bear, Signet, Nighthawk), tried)!;
        Assert.Same(wipe, first.Outcome);
        Assert.Equal(["Grizzly Bears", "Vampire Nighthawk"], first.Candidates.Select(c => c.Card.Name));
        Assert.True(wipe.Unused);

        tried.Add(first.Outcome);
        schedule.Commit(first.Outcome, 2, [new VictimHit("Grizzly Bears", true), new VictimHit("Vampire Nighthawk", true)]);
        Assert.Same(removal, schedule.ProposeEndOfTurn(2, Battlefield(Signet), tried)!.Outcome);

        // A goal that names no creature: the wipe goes with any creature.
        var noCreatureGoal = Schedule(new GoalPredicate.OnBattlefield("Pithing Needle", 5), (DisruptionKind.BoardWipe, 1));
        Assert.Null(noCreatureGoal.ProposeEndOfTurn(1, Battlefield(Needle, Signet), new HashSet<DisruptionOutcome>()));
        Assert.Equal(["Grizzly Bears"], noCreatureGoal.ProposeEndOfTurn(1, Battlefield(Needle, Bear), new HashSet<DisruptionOutcome>())!.Candidates.Select(c => c.Card.Name));
    }

    /// <summary>Fired plus not cast is at most the runs.</summary>
    [Fact]
    public void An_event_summary_holds_fired_plus_not_cast_within_the_runs()
    {
        var @event = new DisruptionEvent(DisruptionKind.Counterspell, 1);

        var summary = new EventSummary(@event, Runs: 10, FiredRuns: 6, MeanFiredTurn: 2, NotCastRuns: 4);

        Assert.Equal(0.4, summary.NotCastRate);
        Assert.Equal(0, new EventSummary(@event, 10, 6, 2).NotCastRuns);
        Assert.Throws<ArgumentException>(() => new EventSummary(@event, Runs: 10, FiredRuns: 6, MeanFiredTurn: 2, NotCastRuns: 5));
        Assert.Throws<ArgumentException>(() => new EventSummary(@event, Runs: 10, FiredRuns: 6, MeanFiredTurn: 2, NotCastRuns: -1));
    }

    /// <summary>The batch counts a run as not cast only when its card was
    /// never cast: a not-cast attempt followed by a cast counts as fired.</summary>
    [Fact]
    public async Task A_batch_counts_not_cast_runs_apart_from_fired_runs()
    {
        var goal = new GoalPredicate.OnBattlefield("Pithing Needle", 5);
        var profile = new DisruptionProfile([new DisruptionEvent(DisruptionKind.Counterspell, 1)], DisruptionMode.Interactive);
        var engine = new NotCastEngine();

        var paired = await MonteCarlo.RunPairedAsync(engine, engine, new GoalAwarePilot(goal), new GoalAwareMulligan(goal),
            new MonteCarloOptions(Runs: 12, BaseSeed: 1, TurnHorizon: 5, Goal: goal, Disruption: profile));

        var summary = Assert.Single(paired.Events);
        Assert.Equal(8, summary.FiredRuns); // runs 0, 1, 3, 4, 6, 7, 9, 10: cast at once or after a not-cast attempt
        Assert.Equal(4, summary.NotCastRuns); // runs 2, 5, 8, 11: never cast
        Assert.True(summary.FiredRuns + summary.NotCastRuns <= summary.Runs);
        Assert.Equal(4, summary.AnsweredRuns); // runs 1, 4, 7, 10
        Assert.True(summary.AnsweredRuns <= summary.FiredRuns);
        var report = EventReport.Of(summary, interactive: true);
        Assert.Equal(4 / 12.0, report.NotCastRate);
        Assert.Equal(4 / 12.0, report.AnsweredRate);
        Assert.Null(EventReport.Of(summary).NotCastRate);
        Assert.Null(EventReport.Of(summary).AnsweredRate);
    }

    /// <summary>Answered is at most fired.</summary>
    [Fact]
    public void An_event_summary_holds_answered_within_fired()
    {
        var @event = new DisruptionEvent(DisruptionKind.Counterspell, 1);

        var summary = new EventSummary(@event, Runs: 10, FiredRuns: 6, MeanFiredTurn: 2, NotCastRuns: 1, AnsweredRuns: 6);

        Assert.Equal(0.6, summary.AnsweredRate);
        Assert.Equal(0, new EventSummary(@event, 10, 6, 2).AnsweredRuns);
        Assert.Throws<ArgumentException>(() => new EventSummary(@event, Runs: 10, FiredRuns: 6, MeanFiredTurn: 2, AnsweredRuns: 7));
        Assert.Throws<ArgumentException>(() => new EventSummary(@event, Runs: 10, FiredRuns: 6, MeanFiredTurn: 2, AnsweredRuns: -1));
    }

    /// <summary>Only the commit records an answer; a hit records none.</summary>
    [Fact]
    public void A_commit_records_answered_by_only_when_answered()
    {
        var goal = new GoalPredicate.OnBattlefield("Pithing Needle", 5);
        var board = new Board(2, [], []);
        var hit = Schedule(goal, (DisruptionKind.Counterspell, 1));
        var answered = Schedule(goal, (DisruptionKind.Counterspell, 1));
        var hitOutcome = hit.ProposeCounter(2, Needle, board, [])!;
        var answeredOutcome = answered.ProposeCounter(2, Needle, board, [])!;
        Assert.Null(answeredOutcome.AnsweredBy);

        hit.Commit(hitOutcome, 2, [new VictimHit("Pithing Needle", true)]);
        answered.Commit(answeredOutcome, 2, [new VictimHit("Pithing Needle", false)], ["Counterspell"]);

        Assert.False(hitOutcome.Answered);
        Assert.Null(hitOutcome.AnsweredBy);
        Assert.True(answeredOutcome.Answered);
        Assert.Equal(["Counterspell"], answeredOutcome.AnsweredBy);
    }

    /// <summary>Run i: i mod 3 = 0 is cast at once and hits, 1 is not cast
    /// and then cast and answered, 2 is never cast.</summary>
    private sealed class NotCastEngine : IGameEngine, IPreparedDeck
    {
        public string Name => "fake";

        public string Version => "0";

        public int Parallelism => 2;

        public IReadOnlyList<string> Simplifications => [];

        public EngineCoverage Coverage { get; } = new([], []);

        public Task<IPreparedDeck> PrepareAsync(IReadOnlyList<DeckCard> deck, CancellationToken cancellationToken)
            => Task.FromResult<IPreparedDeck>(this);

        public Task<RunRecord> PlayAsync(RunSpec spec, IPilotPolicy pilot, IMulliganPolicy mulligan, CancellationToken cancellationToken)
        {
            var outcomes = spec.Disruption?.Events.Select(e => new DisruptionOutcome(e)).ToList();
            if (outcomes is [var outcome])
            {
                var schedule = spec.RunIndex % 3;
                if (schedule >= 1)
                {
                    outcome.MarkNotCast("no_target");
                }

                if (schedule == 0)
                {
                    outcome.Commit(2, [new VictimHit("Pithing Needle", true)]);
                }
                else if (schedule == 1)
                {
                    outcome.Commit(2, [new VictimHit("Pithing Needle", false)], ["Counterspell"]);
                }
            }

            return Task.FromResult(new RunRecord(false, null, EngineError: false, null, outcomes));
        }
    }
}
