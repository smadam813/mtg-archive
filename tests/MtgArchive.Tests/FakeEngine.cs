using System.Collections.Concurrent;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// An engine that plays no game: run i succeeds when i is a multiple
/// of 3, on turn 1 + i mod 4. With <paramref name="errorEvery"/> above
/// 0, run i ends in an engine error when i is a multiple of it, and an
/// engine error never succeeds. Later runs finish first, so the order
/// the runs finish in differs from the run order.
/// </summary>
internal sealed class FakeEngine(int parallelism, int runs, int errorEvery = 0) : IGameEngine, IPreparedDeck
{
    public string Name => "fake";

    public string Version => "0";

    public int Parallelism => parallelism;

    public IReadOnlyList<string> Simplifications => [];

    public EngineCoverage Coverage { get; } = new([], []);

    public ConcurrentQueue<RunSpec> Specs { get; } = new();

    public Task<IPreparedDeck> PrepareAsync(IReadOnlyList<DeckCard> deck, CancellationToken cancellationToken)
        => Task.FromResult<IPreparedDeck>(this);

    public async Task<RunRecord> PlayAsync(RunSpec spec, IPilotPolicy pilot, IMulliganPolicy mulligan, CancellationToken cancellationToken)
    {
        Specs.Enqueue(spec);
        await Task.Delay(runs - spec.RunIndex, cancellationToken);
        var error = errorEvery > 0 && spec.RunIndex % errorEvery == 0;
        var success = !error && spec.RunIndex % 3 == 0;
        return new RunRecord(
            success,
            success ? 1 + (spec.RunIndex % 4) : null,
            EngineError: error,
            spec.CaptureLog ? [new GameLogEntry(1, TurnStep.Main, $"run {spec.RunIndex}")] : null,
            spec.Disruption?.Events.Select(e => new DisruptionOutcome(e)).ToList());
    }
}
