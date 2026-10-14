namespace MtgArchive.Simulation.Engine;

/// <summary>
/// Per-run seeds, SplitMix64-style. The seed of a run is a hash of the
/// base seed and the run index, not the next value of one stream. So the
/// runs are independent and can execute in any order or in parallel.
/// (base seed, run index) replays one run.
/// </summary>
public static class RunSeeds
{
    private const ulong Golden = 0x9E3779B97F4A7C15;

    /// <summary>The seed of run <paramref name="runIndex"/> under <paramref name="baseSeed"/>.</summary>
    public static int Derive(long baseSeed, int runIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(runIndex);
        var z = unchecked((ulong)baseSeed + ((ulong)runIndex + 1) * Golden);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        z ^= z >> 31;
        return unchecked((int)(z ^ (z >> 32)));
    }

    /// <summary>A fresh base seed, for a request that names none.</summary>
    public static long RandomBase() => Random.Shared.NextInt64();
}

/// <summary>
/// The batch settings. Runs default to 1,000; the simulate endpoint
/// enforces the hard cap. A null base seed means a random one. The horizon,
/// the goal, and the disruption profile go to every run as its
/// <see cref="RunSpec"/>, so every run faces the same schedule.
/// </summary>
public sealed record MonteCarloOptions(
    int Runs = MonteCarloOptions.DefaultRuns,
    long? BaseSeed = null,
    int TurnHorizon = RunSpec.DefaultTurnHorizon,
    GoalPredicate? Goal = null,
    DisruptionProfile? Disruption = null)
{
    public const int DefaultRuns = 1000;

    public int Runs { get; } = Runs >= 1
        ? Runs
        : throw new ArgumentOutOfRangeException(nameof(Runs), Runs, "The run count is at least 1.");

    public int TurnHorizon { get; } = RunSpec.ValidHorizon(TurnHorizon);

    /// <summary>True when the batch runs paired: a profile is set. The one
    /// tier-2 or tier-3 decision; the endpoint and the runner read it.</summary>
    public bool Paired => Disruption is not null;
}

/// <summary>A two-sided confidence interval on the success rate.</summary>
public sealed record ConfidenceInterval(double Lower, double Upper, double Level)
{
    /// <summary>z for the two-sided 95% level.</summary>
    private const double Z95 = 1.959963984540054;

    /// <summary>
    /// The 95% Wilson score interval. It stays inside [0, 1]. At 0 and
    /// at n successes it keeps a width, where the simpler interval has none.
    /// </summary>
    public static ConfidenceInterval Wilson(int successes, int runs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(runs, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(successes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(successes, runs);

        double n = runs;
        var p = successes / n;
        var z2 = Z95 * Z95;
        var denominator = 1 + z2 / n;
        var centre = (p + z2 / (2 * n)) / denominator;
        var half = Z95 * Math.Sqrt(p * (1 - p) / n + z2 / (4 * n * n)) / denominator;
        // The bounds are exact at 0 and at n successes; rounding must not move them.
        var lower = successes == 0 ? 0 : Math.Max(0, centre - half);
        var upper = successes == runs ? 1 : Math.Min(1, centre + half);
        return new ConfidenceInterval(lower, upper, 0.95);
    }
}

public enum ExemplarKind
{
    /// <summary>The success with the earliest met turn.</summary>
    Fastest,

    /// <summary>The success at the median met turn among the successes.</summary>
    Typical,

    /// <summary>The first run that missed the goal.</summary>
    Failure,
}

/// <summary>
/// One exemplar: a run the batch points at by index. <see cref="Seed"/> is
/// the seed the index derives to. <see cref="MetTurn"/> is the run's
/// <see cref="GoalProgress.MetTurn"/>. <see cref="Log"/> is the run's log.
/// </summary>
public sealed record Exemplar(
    ExemplarKind Kind,
    int RunIndex,
    int Seed,
    bool Success,
    int? MetTurn,
    IReadOnlyList<GameLogEntry> Log,
    IReadOnlyList<ExemplarEvent>? Events = null);

/// <summary>One fired event in an exemplar run, inline as (turn, kind,
/// victim). A board wipe with several victims is several entries on one
/// turn. Null <see cref="Exemplar.Events"/> means the run has no profile;
/// an empty list means no event fired. Under interactive disruption
/// <see cref="Hit"/> says whether the disruption card hit the victim, and
/// <see cref="AnsweredBy"/> names the cards our deck answered it with, at
/// the event level (empty when it hit or left the stack without our
/// response); both are null under abstracted
/// disruption.</summary>
public sealed record ExemplarEvent(int Turn, string Kind, string Victim, bool? Hit = null, IReadOnlyList<string>? AnsweredBy = null)
{
    /// <summary>Compares <see cref="AnsweredBy"/> by content. The generated
    /// record equality compares it by reference.</summary>
    public bool Equals(ExemplarEvent? other)
        => other is not null
            && Turn == other.Turn
            && Kind == other.Kind
            && Victim == other.Victim
            && Hit == other.Hit
            && (AnsweredBy is null ? other.AnsweredBy is null : other.AnsweredBy is not null && AnsweredBy.SequenceEqual(other.AnsweredBy));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Turn);
        hash.Add(Kind);
        hash.Add(Victim);
        hash.Add(Hit);
        foreach (var card in AnsweredBy ?? [])
        {
            hash.Add(card);
        }

        hash.Add(AnsweredBy?.Count ?? -1);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One event over a batch. <see cref="FiredRuns"/> counts the runs it
/// fires in. <see cref="MeanFiredTurn"/> is the mean fire turn over those
/// runs, null when the event never fires. <see cref="NotCastRuns"/> counts
/// the interactive runs whose disruption card was never cast
/// (<see cref="DisruptionOutcome.NotCast"/>). <see cref="AnsweredRuns"/>
/// counts the interactive runs whose deck answered the card
/// (<see cref="DisruptionOutcome.Answered"/>). Invariants: a run counts when
/// its <see cref="DisruptionOutcome.FiredTurn"/> is set, a fire turn is
/// never before <see cref="DisruptionEvent.Turn"/>, fired plus not cast is
/// at most the runs, and answered is at most fired.
/// </summary>
public sealed record EventSummary(DisruptionEvent Event, int Runs, int FiredRuns, double? MeanFiredTurn, int NotCastRuns = 0, int AnsweredRuns = 0)
{
    public int NotCastRuns { get; } = NotCastRuns >= 0 && FiredRuns + NotCastRuns <= Runs
        ? NotCastRuns
        : throw new ArgumentException("Fired plus not-cast runs are at most the runs.", nameof(NotCastRuns));

    public int AnsweredRuns { get; } = AnsweredRuns >= 0 && AnsweredRuns <= FiredRuns
        ? AnsweredRuns
        : throw new ArgumentException("Answered runs are at least 0 and at most the fired runs.", nameof(AnsweredRuns));

    public double FiredRate => (double)FiredRuns / Runs;

    public double NotCastRate => (double)NotCastRuns / Runs;

    public double AnsweredRate => (double)AnsweredRuns / Runs;

    public bool Unused => FiredRuns == 0;
}

/// <summary>
/// The result of one batch. <see cref="Successes"/> counts the runs that
/// met the goal. <see cref="Curve"/> is the per-turn cumulative curve.
/// Index 0 is turn 1. The value is the share of runs whose
/// <see cref="GoalProgress.MetTurn"/> is that turn or an earlier one. Every
/// success counts by its last leaf's turn, so the curve at the horizon is
/// at least the success rate. It can be more: a leaf can hold on one turn
/// and fail its deadline later. The success rate is a floor under the
/// greedy pilot (ADR 0003); the envelope adds the label.
/// <see cref="EngineErrors"/> counts the runs that ended in an engine
/// error; each counts as a failure (spec §4.5).
/// </summary>
public sealed record MonteCarloResult(
    GoalPredicate Goal,
    PolicyNames Policies,
    int TurnHorizon,
    long BaseSeed,
    int Runs,
    int Successes,
    ConfidenceInterval Interval,
    IReadOnlyList<double> Curve,
    IReadOnlyList<Exemplar> Exemplars,
    DisruptionProfile? Disruption = null,
    IReadOnlyList<EventSummary>? Events = null,
    int EngineErrors = 0)
{
    public double SuccessRate => (double)Successes / Runs;

    public int EngineErrors { get; } = EngineErrors >= 0 && EngineErrors <= Runs - Successes
        ? EngineErrors
        : throw new ArgumentException("Engine errors are at least 0 and at most the failures.", nameof(EngineErrors));
}

/// <summary>
/// A tier-3 batch: the same derived seeds run twice, once without the
/// profile (<see cref="Baseline"/>) and once with it (<see cref="Disrupted"/>).
/// Run <c>i</c> of each arm is (base seed, <c>i</c>), so the delta carries
/// no sampling noise between the arms. <see cref="Runs"/> counts pairs.
/// The constructor holds the invariant: the baseline arm has no profile,
/// the disrupted arm has one, and the arms share the base seed and the
/// run count.
/// </summary>
public sealed record PairedResult
{
    public PairedResult(MonteCarloResult baseline, MonteCarloResult disrupted)
    {
        if (baseline.Disruption is not null)
        {
            throw new ArgumentException("The baseline arm runs without a profile.", nameof(baseline));
        }

        if (baseline.BaseSeed != disrupted.BaseSeed || baseline.Runs != disrupted.Runs)
        {
            throw new ArgumentException("The arms share the base seed and the run count.", nameof(disrupted));
        }

        Baseline = baseline;
        Disrupted = disrupted;
        Profile = disrupted.Disruption ?? throw new ArgumentException("The disrupted arm runs with a profile.", nameof(disrupted));
        Events = disrupted.Events ?? throw new ArgumentException("The disrupted arm carries its event summaries.", nameof(disrupted));
    }

    public MonteCarloResult Baseline { get; }

    public MonteCarloResult Disrupted { get; }

    public int Runs => Baseline.Runs;

    public long BaseSeed => Baseline.BaseSeed;

    public DisruptionProfile Profile { get; }

    /// <summary>One summary per event of the profile, in profile order.</summary>
    public IReadOnlyList<EventSummary> Events { get; }

    /// <summary>The baseline floor minus the disrupted floor.</summary>
    public double ResilienceDelta => Baseline.SuccessRate - Disrupted.SuccessRate;
}

/// <summary>
/// The engine-neutral batch runner. A batch is N runs of one engine under
/// derived seeds (<see cref="RunSeeds.Derive"/>). The runs play in parallel,
/// up to <see cref="IGameEngine.Parallelism"/>, and the runner keeps one
/// record per run index, so completion order cannot change a number. It
/// picks the exemplar indexes from the records, then replays those runs
/// for their logs. A batch run without a disruption profile may stop early
/// once its goal is settled; a replay and a run under a profile never do.
/// </summary>
public static class MonteCarlo
{
    /// <summary>The most exemplars a result carries.</summary>
    public const int MaxExemplars = 3;

    /// <summary>A tier-2 batch, or one arm of a tier-3 batch when the
    /// options carry a profile. The mulligan policy defaults to the land
    /// window.</summary>
    public static Task<MonteCarloResult> RunAsync(
        IGameEngine engine,
        IPreparedDeck deck,
        IPilotPolicy pilot,
        IMulliganPolicy? mulligan = null,
        MonteCarloOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MonteCarloOptions();
        return RunAsync(engine, deck, pilot, mulligan ?? new LandWindowMulligan(), options, options.BaseSeed ?? RunSeeds.RandomBase(), cancellationToken);
    }

    /// <summary>
    /// A tier-3 batch. The options must carry a profile. Both arms take the
    /// one base seed, so run <c>i</c> of each arm derives the identical
    /// seed through <see cref="RunSeeds.Derive"/>; either arm replays alone.
    /// The arms play one after the other, each at the engine's parallelism.
    /// </summary>
    public static async Task<PairedResult> RunPairedAsync(
        IGameEngine engine,
        IPreparedDeck deck,
        IPilotPolicy pilot,
        IMulliganPolicy? mulligan = null,
        MonteCarloOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MonteCarloOptions();
        if (!options.Paired)
        {
            throw new ArgumentException("A paired batch needs a disruption profile.", nameof(options));
        }

        mulligan ??= new LandWindowMulligan();
        var baseSeed = options.BaseSeed ?? RunSeeds.RandomBase();
        var baseline = await RunAsync(engine, deck, pilot, mulligan, options with { Disruption = null }, baseSeed, cancellationToken).ConfigureAwait(false);
        var disrupted = await RunAsync(engine, deck, pilot, mulligan, options, baseSeed, cancellationToken).ConfigureAwait(false);
        return new PairedResult(baseline, disrupted);
    }

    /// <summary>The one goal the options and the policies name, or null.
    /// Two different goals throw, so the pilot never plays toward one goal
    /// while the batch measures another.</summary>
    public static GoalPredicate? GoalOf(IPilotPolicy pilot, IMulliganPolicy? mulligan, GoalPredicate? goal)
        => GoalPredicate.SingleOf(
        [
            ("the options", goal),
            ("the play policy", (pilot as IGoalDirected)?.Goal),
            ("the mulligan policy", (mulligan as IGoalDirected)?.Goal),
        ]);

    /// <summary>The fired events of a run as (turn, kind, victim) entries
    /// in fire order, or null when the run has no profile.</summary>
    public static IReadOnlyList<ExemplarEvent>? EventsOf(IReadOnlyList<DisruptionOutcome>? outcomes)
        => outcomes?
            .Where(o => !o.Unused)
            .OrderBy(o => o.FiredTurn)
            .SelectMany(o => o.Hits is { } hits
                ? hits.Select(h => new ExemplarEvent(o.FiredTurn!.Value, o.Event.KindName, h.Card, h.Hit, o.AnsweredBy ?? []))
                : o.Victims.Select(v => new ExemplarEvent(o.FiredTurn!.Value, o.Event.KindName, v)))
            .ToList();

    private static async Task<MonteCarloResult> RunAsync(
        IGameEngine engine,
        IPreparedDeck deck,
        IPilotPolicy pilot,
        IMulliganPolicy mulligan,
        MonteCarloOptions options,
        long baseSeed,
        CancellationToken cancellationToken)
    {
        var goal = GoalOf(pilot, mulligan, options.Goal)
            ?? throw new ArgumentException("A batch needs a goal: the options, the play policy, or the mulligan policy names one.");

        RunSpec Spec(int index, bool replay)
            => new(index, RunSeeds.Derive(baseSeed, index), options.TurnHorizon, goal, options.Disruption, EarlyStop: !replay && options.Disruption is null, CaptureLog: replay);

        var records = new RunRecord[options.Runs];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, options.Runs),
            new ParallelOptions { MaxDegreeOfParallelism = engine.Parallelism, CancellationToken = cancellationToken },
            async (index, token) => records[index] = await deck.PlayAsync(Spec(index, replay: false), pilot, mulligan, token).ConfigureAwait(false))
            .ConfigureAwait(false);

        var summaries = records.Select((r, index) => new RunSummary(index, r.Success, r.MetTurn)).ToList();
        var curve = new double[options.TurnHorizon];
        for (var turn = 1; turn <= options.TurnHorizon; turn++)
        {
            curve[turn - 1] = summaries.Count(s => s.MetTurn <= turn) / (double)options.Runs;
        }

        var successes = summaries.Count(s => s.Success);
        var exemplars = new List<Exemplar>();
        foreach (var (kind, run) in PickExemplars(summaries))
        {
            var replay = await deck.PlayAsync(Spec(run.Index, replay: true), pilot, mulligan, cancellationToken).ConfigureAwait(false);
            exemplars.Add(new Exemplar(kind, run.Index, RunSeeds.Derive(baseSeed, run.Index), run.Success, run.MetTurn, replay.Log ?? [], EventsOf(replay.Disruption)));
        }

        var events = options.Disruption is null ? null : Summaries(options, records);
        var engineErrors = records.Count(r => r.EngineError);
        return new MonteCarloResult(goal, new PolicyNames(pilot.Name, mulligan.Name), options.TurnHorizon, baseSeed, options.Runs, successes, ConfidenceInterval.Wilson(successes, options.Runs), curve, exemplars, options.Disruption, events, engineErrors);
    }

    /// <summary>One summary per profile event, in profile order. Every
    /// record keeps one outcome per profile event in profile order; a run
    /// that breaks that order throws instead of counting under the wrong
    /// event.</summary>
    private static List<EventSummary> Summaries(MonteCarloOptions options, IReadOnlyList<RunRecord> records)
    {
        var fired = options.Disruption!.Events.Select(e => new EventTally(e)).ToList();
        foreach (var record in records)
        {
            var outcomes = record.Disruption ?? throw new InvalidOperationException("Every run of a disrupted batch reports its events.");
            if (outcomes.Count != fired.Count)
            {
                throw new InvalidOperationException("Every run of a batch keeps one outcome per profile event.");
            }

            foreach (var (outcome, entry) in outcomes.Zip(fired))
            {
                if (outcome.Event != entry.Event)
                {
                    throw new InvalidOperationException("Every run of a batch keeps its outcomes in profile order.");
                }

                if (outcome.FiredTurn is { } turn)
                {
                    entry.Turns.Add(turn);
                    if (outcome.Answered)
                    {
                        entry.Answered++;
                    }
                }
                else if (outcome.NotCast)
                {
                    entry.NotCast++;
                }
            }
        }

        return [.. fired.Select(e => new EventSummary(e.Event, options.Runs, e.Turns.Count, e.Turns.Count == 0 ? null : e.Turns.Average(), e.NotCast, e.Answered))];
    }

    /// <summary>One event's counts over a batch, as <see cref="Summaries"/>
    /// adds them up.</summary>
    private sealed class EventTally(DisruptionEvent @event)
    {
        public DisruptionEvent Event { get; } = @event;

        public List<int> Turns { get; } = [];

        public int NotCast { get; set; }

        public int Answered { get; set; }
    }

    /// <summary>The fastest success, the typical success, and the first
    /// failure, each when a run of that kind exists. One run stands for
    /// one kind only, so a result carries one to three exemplars.</summary>
    private static List<(ExemplarKind Kind, RunSummary Run)> PickExemplars(IReadOnlyList<RunSummary> summaries)
    {
        var successes = summaries.Where(s => s.Success).OrderBy(s => s.MetTurn).ThenBy(s => s.Index).ToList();
        var picks = new List<(ExemplarKind, RunSummary)>();
        if (successes.Count > 0)
        {
            picks.Add((ExemplarKind.Fastest, successes[0]));
            var typical = successes[successes.Count / 2];
            if (typical.Index != successes[0].Index)
            {
                picks.Add((ExemplarKind.Typical, typical));
            }
        }

        if (summaries.FirstOrDefault(s => !s.Success) is { } failure)
        {
            picks.Add((ExemplarKind.Failure, failure));
        }

        return picks;
    }

    private sealed record RunSummary(int Index, bool Success, int? MetTurn);
}
