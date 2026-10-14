using System.Text.Json;
using MtgArchive.Api;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// The engine-neutral batch runner (spec section 3.3): runs play in
/// parallel up to the engine's parallelism, records aggregate by run index,
/// batch runs stop early while replays and disrupted runs never do, and
/// the same request gives the same envelope at any parallelism.
/// </summary>
public sealed class BatchRunnerTests
{
    private static readonly GoalPredicate NeedleByThree = new GoalPredicate.OnBattlefield("Pithing Needle", 3);

    private static readonly DisruptionProfile Profile = new([new DisruptionEvent(DisruptionKind.Counterspell, 2), new DisruptionEvent(DisruptionKind.TargetedRemoval, 3)]);

    /// <summary>The envelope the endpoint would serialize for a result.</summary>
    private static string Envelope(MonteCarloResult result)
        => JsonSerializer.Serialize(
            SimulationEnvelope.Build(result, null, new FakeEngine(parallelism: 1, runs: 0), new EngineCoverage([], []), result.EngineErrors, new DeckVerdict(DeckVerdictStatus.Legal, [], [], 0, 100), new DecklistEcho(0, []), new VersionsBlock()),
            WireJson.Options);

    [Fact]
    public async Task Records_aggregate_by_run_index_whatever_order_the_runs_finish_in()
    {
        var options = new MonteCarloOptions(Runs: 40, BaseSeed: 9, TurnHorizon: 4, Goal: NeedleByThree);
        var serial = new FakeEngine(parallelism: 1, runs: 40);
        var parallel = new FakeEngine(parallelism: 8, runs: 40);

        var first = await MonteCarlo.RunAsync(serial, serial, new GoalAwarePilot(NeedleByThree), options: options);
        var second = await MonteCarlo.RunAsync(parallel, parallel, new GoalAwarePilot(NeedleByThree), options: options);

        Assert.Equal(Envelope(first), Envelope(second));
        Assert.Equal(14, first.Successes);
    }

    [Fact]
    public async Task Batch_runs_stop_early_but_replays_and_disrupted_runs_never_do()
    {
        var engine = new FakeEngine(parallelism: 4, runs: 20);
        var options = new MonteCarloOptions(Runs: 20, BaseSeed: 5, TurnHorizon: 4, Goal: NeedleByThree, Disruption: Profile);

        var paired = await MonteCarlo.RunPairedAsync(engine, engine, new GoalAwarePilot(NeedleByThree), options: options);

        var batch = engine.Specs.Where(s => !s.CaptureLog).ToList();
        Assert.Equal(40, batch.Count);
        Assert.All(batch.Where(s => s.Disruption is null), s => Assert.True(s.EarlyStop));
        Assert.All(batch.Where(s => s.Disruption is not null), s => Assert.False(s.EarlyStop));
        Assert.Equal(20, batch.Count(s => s.Disruption is not null));

        var replays = engine.Specs.Where(s => s.CaptureLog).ToList();
        Assert.Equal(paired.Baseline.Exemplars.Count + paired.Disrupted.Exemplars.Count, replays.Count);
        Assert.All(replays, s => Assert.False(s.EarlyStop));
        Assert.All(engine.Specs, s => Assert.Equal(RunSeeds.Derive(5, s.RunIndex), s.Seed));
        Assert.All(paired.Baseline.Exemplars, e => Assert.Equal($"run {e.RunIndex}", Assert.Single(e.Log).Message));
    }

    [Fact]
    public async Task A_batch_has_one_goal_that_the_options_and_the_policies_agree_on()
    {
        var engine = new FakeEngine(parallelism: 2, runs: 3);
        var other = new GoalPredicate.Cast("Grizzly Bears", 2);

        Assert.Equal(NeedleByThree, MonteCarlo.GoalOf(new GoalAwarePilot(NeedleByThree), new LandWindowMulligan(), null));
        Assert.Throws<ArgumentException>(() => MonteCarlo.GoalOf(new GoalAwarePilot(NeedleByThree), new GoalAwareMulligan(other), null));
        await Assert.ThrowsAsync<ArgumentException>(() => MonteCarlo.RunAsync(engine, engine, new ScriptedViewPilot(), options: new MonteCarloOptions(Runs: 3, BaseSeed: 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => MonteCarlo.RunPairedAsync(engine, engine, new GoalAwarePilot(NeedleByThree), options: new MonteCarloOptions(Runs: 3, BaseSeed: 1)));
    }

    [Fact]
    public async Task A_cancelled_batch_plays_no_run_and_throws()
    {
        var engine = new FakeEngine(parallelism: 2, runs: 50);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MonteCarlo.RunAsync(
            engine, engine, new GoalAwarePilot(NeedleByThree), options: new MonteCarloOptions(Runs: 50, BaseSeed: 1, Goal: NeedleByThree), cancellationToken: cancel.Token));
        Assert.Empty(engine.Specs);
    }

    [Fact]
    public async Task An_engine_error_is_a_failure_and_the_result_counts_it()
    {
        var engine = new FakeEngine(parallelism: 4, runs: 30, errorEvery: 5);

        var result = await MonteCarlo.RunAsync(engine, engine, new GoalAwarePilot(NeedleByThree),
            options: new MonteCarloOptions(Runs: 30, BaseSeed: 2, TurnHorizon: 4, Goal: NeedleByThree));

        Assert.Equal(6, result.EngineErrors); // runs 0, 5, 10, 15, 20 and 25
        Assert.Equal(Enumerable.Range(0, 30).Count(i => i % 3 == 0 && i % 5 != 0), result.Successes);
    }

    [Fact]
    public void Engine_errors_never_exceed_the_failures_and_are_never_negative()
    {
        Assert.Throws<ArgumentException>(() => Result(runs: 10, successes: 4, engineErrors: 7));
        Assert.Throws<ArgumentException>(() => Result(runs: 10, successes: 4, engineErrors: -1));
        Result(runs: 10, successes: 4, engineErrors: 6);
    }

    private static MonteCarloResult Result(int runs, int successes, int engineErrors)
        => new(NeedleByThree, new PolicyNames("pilot", "mulligan"), TurnHorizon: 4, BaseSeed: 1, runs, successes,
            ConfidenceInterval.Wilson(successes, runs), Curve: new double[4], Exemplars: [], EngineErrors: engineErrors);
}
