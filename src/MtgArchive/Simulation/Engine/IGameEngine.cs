using System.ComponentModel;

namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The simulation engine behind the batch runner (XMage). It names itself
/// for the envelope, says how many runs it can play at once, and prepares
/// a deck once per batch.
/// </summary>
public interface IGameEngine
{
    /// <summary>The engine name every floor records ("xmage").</summary>
    string Name { get; }

    /// <summary>The engine version every floor records.</summary>
    string Version { get; }

    /// <summary>The most runs the batch runner plays at once, at least 1.</summary>
    int Parallelism { get; }

    /// <summary>What the engine does not model: the envelope's
    /// <c>simplifications</c>, next to every floor it plays.</summary>
    IReadOnlyList<string> Simplifications { get; }

    /// <summary>The simplifications of a run whose disrupted arm plays in
    /// <paramref name="mode"/>; null is a tier-2 run. An engine whose
    /// simplifications do not depend on the mode keeps its list.</summary>
    IReadOnlyList<string> SimplificationsFor(DisruptionMode? mode) => Simplifications;

    /// <summary>Builds what does not change between the runs of a batch.</summary>
    Task<IPreparedDeck> PrepareAsync(IReadOnlyList<DeckCard> deck, CancellationToken cancellationToken);
}

/// <summary>A deck an engine prepared for a batch. It plays one run per
/// call; the runs share it, so it holds no state of a run.</summary>
public interface IPreparedDeck
{
    /// <summary>The cards the engine cannot run or the pilot cannot read.</summary>
    EngineCoverage Coverage { get; }

    /// <summary>The deck's response cards and the responses the archive
    /// could not profile. An interactive envelope reports them (ADR 0013).</summary>
    DeckResponses Responses => DeckResponses.None;

    /// <summary>Plays one run to its end, or to an early stop.</summary>
    Task<RunRecord> PlayAsync(RunSpec spec, IPilotPolicy pilot, IMulliganPolicy mulligan, CancellationToken cancellationToken);
}

/// <summary>
/// One run of a batch. <see cref="Seed"/> is
/// <c>RunSeeds.Derive(baseSeed, RunIndex)</c>. The goal, when set, is the
/// one the run evaluates. A disruption profile schedules its events into
/// the run; the pilot never sees it. With <see cref="EarlyStop"/> the run
/// may end once its goal is settled (<see cref="GoalProgress.IsSettled"/>).
/// A run under a disruption profile never stops early: a later event can
/// still fire, and the envelope reports the fired events. With
/// <see cref="CaptureLog"/> the record carries the run's log.
/// </summary>
public sealed record RunSpec(
    int RunIndex,
    int Seed,
    int TurnHorizon,
    GoalPredicate? Goal,
    DisruptionProfile? Disruption,
    bool EarlyStop,
    bool CaptureLog)
{
    public const int DefaultTurnHorizon = 10;

    public int TurnHorizon { get; } = ValidHorizon(TurnHorizon);

    public bool EarlyStop { get; } = EarlyStop && Disruption is not null
        ? throw new ArgumentException("A run under a disruption profile never stops early.", nameof(EarlyStop))
        : EarlyStop;

    /// <summary>The one horizon rule: at least 1.</summary>
    public static int ValidHorizon(int turnHorizon)
        => turnHorizon >= 1
            ? turnHorizon
            : throw new ArgumentOutOfRangeException(nameof(turnHorizon), turnHorizon, "The turn horizon is at least 1.");
}

/// <summary>
/// What one run reports to the batch runner. <see cref="Success"/> is the
/// goal verdict and <see cref="MetTurn"/> the goal's met turn. The type
/// holds the invariants the batch relies on: a success has a met turn, so
/// the curve at the horizon is at least the success rate, and a run that
/// ended in an engine error never succeeds. <see cref="Log"/> is set only
/// when the spec asked for it. <see cref="Disruption"/> holds one outcome
/// per profile event in profile order, and is null without a profile.
/// <see cref="Digest"/> is the engine's game digest, when it has one.
/// </summary>
public sealed record RunRecord(
    bool Success,
    int? MetTurn,
    bool EngineError,
    IReadOnlyList<GameLogEntry>? Log,
    IReadOnlyList<DisruptionOutcome>? Disruption)
{
    public bool Success { get; } = !Success || (MetTurn is not null && !EngineError)
        ? Success
        : throw new ArgumentException("A success has a met turn and no engine error.", nameof(Success));

    /// <summary>The engine's digest of the game's log with object ids
    /// stripped, when the engine has one (XMage's <c>game_over.digest</c>);
    /// null for an engine error. The determinism and
    /// leak gates compare it across processes (spec §6.3, §6.4).</summary>
    public string? Digest { get; init; }
}

/// <summary>
/// The cards of a deck the floor does not fully cover.
/// <see cref="Unimplemented"/> lists the cards the engine
/// cannot run; <see cref="Unprofiled"/> lists the cards without a card
/// feature profile. Both are sorted by name.
/// </summary>
public sealed record EngineCoverage(
    [property: Description("The cards the engine cannot run, sorted by name. XMage plays each as a blank card.")]
    IReadOnlyList<string> Unimplemented,
    [property: Description("The cards without a full card feature profile, sorted by name. The engine runs them; the pilot reads no feature the profile cannot prove.")]
    IReadOnlyList<string> Unprofiled)
{
    [Description("Interactive only: the cards with a response the archive could not profile, sorted by name. They never answer, so the answered rate is a lower bound. Absent in tier 2 and in abstracted mode.")]
    public IReadOnlyList<string>? UnprofiledResponses { get; init; }
}

/// <summary>
/// A deck's responses (ADR 0013): its response cards (glossary), and the
/// cards whose response the archive could not profile. Both are full
/// decklist names, sorted by name.
/// </summary>
public sealed record DeckResponses(IReadOnlyList<string> ResponseCards, IReadOnlyList<string> UnprofiledResponses)
{
    /// <summary>No response card and no unprofiled response.</summary>
    public static readonly DeckResponses None = new([], []);
}
