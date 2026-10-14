using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// The Monte Carlo runner. The tests cover the SplitMix64-style
/// per-run seeds, the success count with its 95% confidence interval, the
/// per-turn cumulative curve, the turn horizon on every run, and the one to
/// three exemplars. <see cref="FakeEngine"/> plays the runs.
/// </summary>
public sealed class MonteCarloTests
{
    private static readonly GoalPredicate NeedleByTwo = new GoalPredicate.OnBattlefield("Pithing Needle", 2);

    private static Task<MonteCarloResult> RunAsync(MonteCarloOptions options, FakeEngine? engine = null)
    {
        engine ??= new FakeEngine(parallelism: 4, runs: options.Runs);
        return MonteCarlo.RunAsync(engine, engine, new GoalAwarePilot(NeedleByTwo), options: options with { Goal = NeedleByTwo });
    }

    [Fact]
    public void Per_run_seeds_are_derived_not_sequential()
    {
        var seeds = Enumerable.Range(0, 1000).Select(i => RunSeeds.Derive(42, i)).ToList();

        Assert.Equal(1000, seeds.Distinct().Count());
        Assert.DoesNotContain(seeds.Zip(seeds.Skip(1)), pair => pair.Second == pair.First + 1);
        Assert.Equal(RunSeeds.Derive(42, 7), RunSeeds.Derive(42, 7));
        Assert.NotEqual(RunSeeds.Derive(42, 7), RunSeeds.Derive(43, 7));
        Assert.NotEqual(RunSeeds.Derive(42, 7), RunSeeds.Derive(42, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => RunSeeds.Derive(42, -1));
    }

    [Fact]
    public async Task Exemplars_are_replays_of_the_runs_they_report()
    {
        var engine = new FakeEngine(parallelism: 4, runs: 40);
        var result = await RunAsync(new MonteCarloOptions(Runs: 40, BaseSeed: 5), engine);

        Assert.InRange(result.Exemplars.Count, 1, MonteCarlo.MaxExemplars);
        Assert.Equal(result.Exemplars.Count, result.Exemplars.Select(e => e.RunIndex).Distinct().Count());
        foreach (var exemplar in result.Exemplars)
        {
            Assert.Equal(RunSeeds.Derive(5, exemplar.RunIndex), exemplar.Seed);
            Assert.Equal($"run {exemplar.RunIndex}", Assert.Single(exemplar.Log).Message);
            Assert.Contains(engine.Specs, s => s.CaptureLog && s.RunIndex == exemplar.RunIndex && s.Seed == exemplar.Seed);
        }

        Assert.Contains(result.Exemplars, e => e.Kind == ExemplarKind.Fastest && e.Success);
        Assert.Contains(result.Exemplars, e => e.Kind == ExemplarKind.Failure && !e.Success);
        Assert.Equal(1, result.Exemplars.Single(e => e.Kind == ExemplarKind.Fastest).MetTurn);
    }

    [Fact]
    public async Task Every_exemplar_kind_needs_a_run_of_its_outcome()
    {
        var alwaysFails = await RunAsync(new MonteCarloOptions(Runs: 20, BaseSeed: 1), new FakeEngine(parallelism: 4, runs: 20, errorEvery: 1));

        var failure = Assert.Single(alwaysFails.Exemplars);
        Assert.Equal(ExemplarKind.Failure, failure.Kind);
        Assert.Null(failure.MetTurn);
        Assert.Equal(0, alwaysFails.Successes);
        Assert.Equal(0, alwaysFails.Interval.Lower);
    }

    [Fact]
    public async Task The_base_seed_is_random_unless_supplied_and_always_reported()
    {
        var supplied = await RunAsync(new MonteCarloOptions(Runs: 5, BaseSeed: 1234));
        Assert.Equal(1234, supplied.BaseSeed);

        var seeds = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            seeds.Add((await RunAsync(new MonteCarloOptions(Runs: 2))).BaseSeed);
        }

        Assert.True(seeds.Distinct().Count() > 1);
    }

    [Fact]
    public async Task Runs_default_to_a_thousand_and_are_configurable()
    {
        Assert.Equal(1000, new MonteCarloOptions().Runs);
        Assert.Equal(1000, MonteCarloOptions.DefaultRuns);
        Assert.Equal(25, (await RunAsync(new MonteCarloOptions(Runs: 25, BaseSeed: 1))).Runs);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonteCarloOptions(Runs: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonteCarloOptions(TurnHorizon: 0));
    }

    [Fact]
    public async Task The_result_reports_the_success_rate_with_a_wilson_interval()
    {
        var result = await RunAsync(new MonteCarloOptions(Runs: 60, BaseSeed: 3));

        Assert.Equal(20, result.Successes);
        Assert.Equal(20 / 60.0, result.SuccessRate);
        Assert.Equal(0.95, result.Interval.Level);
        Assert.Equal(ConfidenceInterval.Wilson(20, 60), result.Interval);
        Assert.True(result.Interval.Lower < result.SuccessRate);
        Assert.True(result.SuccessRate < result.Interval.Upper);
    }

    [Fact]
    public void The_wilson_interval_matches_the_closed_form()
    {
        var interval = ConfidenceInterval.Wilson(successes: 87, runs: 100);
        Assert.Equal(0.790, interval.Lower, 3);
        Assert.Equal(0.922, interval.Upper, 3);

        var none = ConfidenceInterval.Wilson(0, 10);
        Assert.Equal(0, none.Lower);
        Assert.True(none.Upper > 0);

        var all = ConfidenceInterval.Wilson(10, 10);
        Assert.Equal(1, all.Upper);
        Assert.True(all.Lower < 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => ConfidenceInterval.Wilson(11, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfidenceInterval.Wilson(0, 0));
    }

    /// <summary>Run i of the fake engine succeeds on turn 1 + i mod 4 when
    /// i is a multiple of 3: of 24 runs, runs 0 and 12 on turn 1, 9 and 21
    /// on turn 2, 6 and 18 on turn 3, 3 and 15 on turn 4.</summary>
    [Fact]
    public async Task The_curve_is_cumulative_over_turns_and_spans_the_horizon()
    {
        var result = await RunAsync(new MonteCarloOptions(Runs: 24, BaseSeed: 8, TurnHorizon: 5));

        Assert.Equal(5, result.TurnHorizon);
        Assert.Equal([2 / 24.0, 4 / 24.0, 6 / 24.0, 8 / 24.0, 8 / 24.0], result.Curve);
        Assert.Equal(result.SuccessRate, result.Curve[^1]);
    }

    [Fact]
    public async Task The_horizon_defaults_to_ten_and_reaches_every_run()
    {
        Assert.Equal(10, new MonteCarloOptions().TurnHorizon);
        var engine = new FakeEngine(parallelism: 4, runs: 30);

        var result = await RunAsync(new MonteCarloOptions(Runs: 30, BaseSeed: 2, TurnHorizon: 3), engine);

        Assert.Equal(3, result.Curve.Count);
        Assert.All(engine.Specs, s => Assert.Equal(3, s.TurnHorizon));
    }

    [Fact]
    public async Task The_result_names_the_goal_and_the_policies()
    {
        var engine = new FakeEngine(parallelism: 2, runs: 3);

        var result = await MonteCarlo.RunAsync(engine, engine, new GoalAwarePilot(NeedleByTwo), new GoalAwareMulligan(NeedleByTwo), new MonteCarloOptions(Runs: 3, BaseSeed: 1));

        Assert.Equal(NeedleByTwo, result.Goal);
        Assert.Equal(new PolicyNames("goal-aware-greedy", "goal-aware"), result.Policies);
    }
}
