using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Banlist;
using MtgArchive.Data;
using MtgArchive.Simulation.Engine;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Simulation;

/// <summary>
/// POST /simulate: the tier-2 surface, and the tier-3 surface when the
/// request carries a disruption profile. It resolves the decklist, parses
/// the goal, runs the Monte Carlo batch on the XMage sidecar under the
/// goal-aware pilot and mulligan under the format's game rules, and returns
/// the result envelope. The format defaults to Commander, and the
/// <c>versions</c> block echoes it (ADR 0012). The envelope carries the
/// floor label and the confidence interval as fields and the deck
/// verdict that the deck verdict route returns. An illegal decklist runs;
/// the envelope carries its verdict (ADR 0003, ADR 0012).
/// <para>
/// A profile pairs the batch: every seed runs once without the profile
/// and once with it. The envelope then adds the <see cref="DisruptionReport"/>.
/// </para>
/// <para>
/// The <c>Simulation:Parallelism</c> setting caps the runs a batch plays
/// at once; unset, the sidecar's thread count applies. A value below 1
/// fails startup.
/// </para>
/// <para>
/// A sidecar that cannot be reached is a 503 problem, more than 1% engine
/// errors a 502, and a commander XMage lacks a 422.
/// </para>
/// </summary>
public sealed class SimulateEndpoints(XmageSidecar xmage, IConfiguration configuration) : IEndpointMapper
{
    public const string ParallelismSetting = "Simulation:Parallelism";

    private readonly int? _parallelism = ParallelismFrom(configuration);

    /// <summary>The configured parallelism, or null when unset. A value
    /// below 1 throws.</summary>
    public static int? ParallelismFrom(IConfiguration configuration)
        => configuration.GetValue<int?>(ParallelismSetting) switch
        {
            < 1 and var value => throw new InvalidOperationException($"{ParallelismSetting} is {value}; it must be at least 1."),
            var value => value,
        };

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/simulate", async Task<Results<Ok<SimulationEnvelope>, ProblemHttpResult>> (
            SimulateRequest request,
            ArchiveDbContext db,
            BanlistVersionAxis banlist,
            CancellationToken cancellationToken) =>
        {
            var format = FormatParameter.ResolveDeck(request.Format);
            FormatParameter.RejectBanlistPin(format, request.BanlistVersion);

            var runs = request.Runs ?? MonteCarloOptions.DefaultRuns;
            if (runs < 1)
            {
                return TypedResults.Problem(Problems.ParameterBelowOne("runs", runs));
            }

            if (runs > SimulateRequest.MaxRuns)
            {
                return TypedResults.Problem(Problems.ParameterAboveMaximum("runs", runs, SimulateRequest.MaxRuns));
            }

            var horizon = request.TurnHorizon ?? RunSpec.DefaultTurnHorizon;
            if (horizon < 1)
            {
                return TypedResults.Problem(Problems.ParameterBelowOne("turn_horizon", horizon));
            }

            if (horizon > SimulateRequest.MaxTurnHorizon)
            {
                return TypedResults.Problem(Problems.ParameterAboveMaximum("turn_horizon", horizon, SimulateRequest.MaxTurnHorizon));
            }

            var goalErrors = new List<string>();
            var goal = GoalPredicateParser.Parse(JsonSerializer.SerializeToElement(request.Goal, WireJson.Options), goalErrors);
            if (goal is null)
            {
                return TypedResults.Problem(Problems.InvalidGoal(goalErrors, GoalPredicate.Vocabulary));
            }

            if (goal.LastTurn > horizon)
            {
                return TypedResults.Problem(Problems.InvalidGoal(
                    [$"goal: the goal resolves on turn {goal.LastTurn}, after the turn horizon {horizon}."],
                    GoalPredicate.Vocabulary));
            }

            var mode = DisruptionMode.Abstracted;
            if (request.DisruptionMode is { } modeWire)
            {
                if (!DisruptionProfile.Modes.Contains(modeWire))
                {
                    return TypedResults.Problem(Problems.UnknownDisruptionMode(modeWire, DisruptionProfile.Modes));
                }

                if (request.DisruptionProfile is null)
                {
                    return TypedResults.Problem(Problems.DisruptionModeWithoutProfile(modeWire));
                }

                mode = DisruptionProfile.ModeOf(modeWire);
            }

            var playPolicy = GoalAwarePilot.PolicyName;
            if (request.PlayPolicy is { } policyWire)
            {
                if (!PlayPolicies.Names.Contains(policyWire))
                {
                    return TypedResults.Problem(Problems.UnknownPlayPolicy(policyWire, PlayPolicies.Names));
                }

                if (PlayPolicies.NeedsInteractive(policyWire) && mode != DisruptionMode.Interactive)
                {
                    return TypedResults.Problem(Problems.PlayPolicyNeedsInteractive(policyWire));
                }

                playPolicy = policyWire;
            }

            DisruptionProfile? disruption = null;
            if (request.DisruptionProfile is { } profileWire)
            {
                if (profileWire.Count > SimulateRequest.MaxDisruptionEvents)
                {
                    return TypedResults.Problem(Problems.ParameterAboveMaximum("disruption_profile", profileWire.Count, SimulateRequest.MaxDisruptionEvents));
                }

                var profileErrors = new List<string>();
                disruption = DisruptionProfileParser.Parse(JsonSerializer.SerializeToElement(profileWire, WireJson.Options), profileErrors);
                if (disruption is null)
                {
                    return TypedResults.Problem(Problems.InvalidDisruptionProfile(profileErrors, DisruptionProfile.Vocabulary));
                }

                disruption = disruption with { Mode = mode };
            }

            ResolvedDecklist decklist;
            try
            {
                decklist = await new DecklistResolver(db).ResolveAsync(request.Decklist, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                return TypedResults.Problem(Problems.InvalidDecklist(ex.Message));
            }

            if (decklist.Unresolved.Count > 0)
            {
                return TypedResults.Problem(Problems.UnresolvedDecklist(decklist.Unresolved));
            }

            var deck = await DeckCards.LoadAsync(db, decklist, cancellationToken);
            var missing = goal.CardNames
                .Where(name => !deck.Any(c => c.Answers(name) || c.AsBackFace()?.Answers(name) == true))
                .ToList();
            if (missing.Count > 0)
            {
                var ids = deck.Select(c => c.Id).ToList();
                var faces = await db.Cards
                    .Where(c => ids.Contains(c.Id))
                    .SelectMany(c => c.Faces.Where(f => missing.Contains(f.Name)).Select(f => new { f.Name, c.Layout }))
                    .ToListAsync(cancellationToken);
                return TypedResults.Problem(Problems.InvalidGoal(
                    [.. missing.Select(name => faces.FirstOrDefault(f => f.Name == name) is { } face
                        ? $"goal: \"{name}\" is a face of a {face.Layout ?? "multi-face"} card that the engine cannot play."
                        : $"goal: the decklist has no card named \"{name}\".")],
                    GoalPredicate.Vocabulary));
            }

            var (pin, versions) = await FormatParameter.ResolveDeckVersionsAsync(
                db, banlist, format, request.BanlistVersion, cancellationToken);
            var deckVerdict = await DeckVerdict.ComputeAsync(db, decklist, format, pin, cancellationToken);

            var pilot = PlayPolicies.Create(playPolicy, goal);
            var mulligan = new GoalAwareMulligan(goal);
            var options = new MonteCarloOptions(runs, request.Seed, horizon, goal, disruption);
            XmageEngine engine;
            IPreparedDeck prepared;
            MonteCarloResult result;
            PairedResult? paired;
            try
            {
                engine = await XmageEngine.ConnectAsync(
                    xmage, (cards, token) => CardFeatureProfiles.LoadAsync(db, cards, token), _parallelism, cancellationToken, GameRules.For(format));
                prepared = await engine.PrepareAsync(deck, cancellationToken);
                if (Refusal(engine, prepared, deck, goal) is { } refusal)
                {
                    return TypedResults.Problem(refusal);
                }

                paired = options.Paired ? await MonteCarlo.RunPairedAsync(engine, prepared, pilot, mulligan, options, cancellationToken) : null;
                result = paired?.Baseline ?? await MonteCarlo.RunAsync(engine, prepared, pilot, mulligan, options, cancellationToken);
            }
            catch (XmageUnavailableException ex)
            {
                return TypedResults.Problem(Problems.EngineUnavailable(XmageEngine.EngineName, ex.Message));
            }

            var games = result.Runs * (paired is null ? 1 : 2);
            var engineErrors = result.EngineErrors + (paired?.Disrupted.EngineErrors ?? 0);
            if (engineErrors * 100 > games)
            {
                return TypedResults.Problem(Problems.EngineErrors(engine.Name, engine.Version, engineErrors, games));
            }

            return TypedResults.Ok(SimulationEnvelope.Build(
                result,
                paired,
                engine,
                prepared.Coverage,
                engineErrors,
                deckVerdict,
                new DecklistEcho(decklist.DeckSize, [.. deck.Where(c => c.IsCommander).Select(c => Citation.OfCard(c.OracleId, c.Name))]),
                versions,
                prepared.Responses) with
            {
                NameRewrites = decklist.WireRewrites,
            });
        });
    }

    /// <summary>
    /// A request XMage cannot answer honestly. A card XMage lacks is a blank
    /// card that never casts: a missing commander leaves the deck without
    /// its commander, and a goal that only blanks answer would read 0% and
    /// hide the gap.
    /// </summary>
    private static ProblemDetails? Refusal(XmageEngine engine, IPreparedDeck prepared, IReadOnlyList<DeckCard> deck, GoalPredicate goal)
    {
        var missing = prepared.Coverage.Unimplemented.ToHashSet(StringComparer.Ordinal);
        var commanders = deck.Where(c => c.IsCommander && missing.Contains(c.Name)).Select(c => c.Name).ToList();
        if (commanders.Count > 0)
        {
            return Problems.EngineCannotRun(engine.Name, engine.Version, commanders);
        }

        var blankGoals = goal.CardNames
            .Where(name => deck.Where(c => c.Answers(name) || c.AsBackFace()?.Answers(name) == true).All(c => missing.Contains(c.Name)))
            .ToList();
        return blankGoals.Count == 0
            ? null
            : Problems.InvalidGoal(
                [.. blankGoals.Select(name => $"goal: \"{name}\" is a card {engine.Name} {engine.Version} does not implement; it plays as a blank card and never casts.")],
                GoalPredicate.Vocabulary);
    }
}

/// <summary>
/// POST /simulate body. The goal is required and is the closed
/// goal-predicate vocabulary in its wire form. Runs default to 1,000 and
/// cap at 10,000; the horizon defaults to 10 and caps at 30. The two caps
/// together bound the work one request can ask for. With a disruption
/// profile, runs counts pairs and the request costs twice the execution.
/// A null seed means a random one, reported in the envelope.
/// The request selects the play policy with play_policy (ADR 0014) and no
/// mulligan policy: the envelope names the one implementation of the
/// mulligan.
/// </summary>
public sealed record SimulateRequest
{
    public const int MaxRuns = 10_000;

    public const int MaxTurnHorizon = 30;

    public const int MaxDisruptionEvents = 100;

    public required Decklist Decklist { get; init; }

    public required GoalWire Goal { get; init; }

    [Description("Run count; default 1000, at most 10000. With a disruption_profile, runs counts pairs: every seed runs once without the profile and once with it. The request then plays 2 x runs games. The default and the cap do not change.")]
    public int? Runs { get; init; }

    [Description("Base seed; omitted means random. The envelope reports the seed used.")]
    public long? Seed { get; init; }

    [Description("Last turn every run plays; default 10, at most 30.")]
    public int? TurnHorizon { get; init; }

    [Description("Optional tier-3 schedule of one-shot disruption events; every run faces the same schedule; at most 100 events. Its presence pairs the batch and adds the disruption block to the envelope. Omitted means a tier-2 run. The service supplies no default profile.")]
    public IReadOnlyList<DisruptionEventWire>? DisruptionProfile { get; init; }

    [Description("How the disrupted arm resolves the disruption_profile: abstracted (tier 3, the default) or interactive (a scripted opponent casts each event as a disruption card; ADR 0013). "
        + "A mode needs a disruption_profile; a mode alone or an unknown mode is a 400 invalid-disruption-mode. "
        + "The baseline arm is the same tier-2 run in both modes. Interactive casts Counterspell for counterspell, Atomize for targeted_removal and Rout for board_wipe.")]
    public string? DisruptionMode { get; init; }

    [Description("The play policy of both arms: goal-aware-greedy (the default) or goal-aware-hold (ADR 0014). "
        + "goal-aware-hold holds response cards and a mana reserve, so it needs disruption_mode interactive. "
        + "Without that mode, or with an unknown value, the request is a 400 invalid-play-policy. The envelope's policies.play echoes the policy that ran.")]
    public string? PlayPolicy { get; init; }

    [Description("Banlist version pin (effective_date, yyyy-MM-dd) for the deck verdict; latest when omitted. "
        + "Commander only: a Brawl format has no banlist version, so a pin is a 400.")]
    public string? BanlistVersion { get; init; }

    [Description("The format: commander, brawl, competitive_brawl, or standard_brawl. Commander when omitted, and the versions block echoes it. "
        + "The format fixes the game rules (start life, commander damage, free mulligans, turn-1 draw) and the deck verdict. "
        + "An unknown format is a 400, never a silent fallback; standard is a 400 too, since it has card legality only.")]
    public string? Format { get; init; }
}

/// <summary>
/// The goal predicate on the wire, in the request and echoed in the
/// envelope: <c>{ kind: on_battlefield | cast, card, by_turn }</c> or
/// <c>{ kind: all_of, goals: [...] }</c>. <see cref="GoalPredicateParser"/>
/// validates it against the closed vocabulary.
/// </summary>
public sealed record GoalWire(
    [property: Description("One of on_battlefield, cast, all_of.")] string Kind,
    [property: Description("The card a leaf names. The full decklist name matches the card on either face. A face name matches that face only: the front face of any multi-face card, or the back face of a modal double-faced card. Any other face name is invalid-goal.")] string? Card,
    [property: Description("The turn a leaf is decided at; at least 1.")] int? ByTurn,
    [property: Description("The members of an all_of.")] IReadOnlyList<GoalWire>? Goals)
{
    public static GoalWire Of(GoalPredicate goal) => goal switch
    {
        GoalPredicate.AllOf allOf => new(goal.Kind, null, null, [.. allOf.Goals.Select(Of)]),
        _ => new(goal.Kind, goal.CardName, goal.LastTurn, null),
    };
}

/// <summary>
/// One disruption event on the wire: <c>{ kind, turn }</c>.
/// <see cref="DisruptionProfileParser"/> checks the kind against the
/// closed vocabulary; an unknown kind is a request error.
/// </summary>
public sealed record DisruptionEventWire(
    [property: Description("One of counterspell, targeted_removal, board_wipe.")] string Kind,
    [property: Description("The turn the event unlocks on; at least 1. An event after the horizon stays unused.")] int Turn)
{
    public static DisruptionEventWire Of(DisruptionEvent @event) => new(@event.KindName, @event.Turn);
}

/// <summary>
/// The tier-2 result envelope, and the tier-3 envelope when the request
/// carries a disruption profile. The floor label and the confidence
/// interval are fields, so the schema itself declares the floor. The
/// curve is cumulative per turn. The exemplars replay by (base seed, run
/// index). In tier-3 mode the tier-2 fields describe the baseline arm,
/// the tier-2 run under the same seeds. <see cref="Disruption"/> carries
/// both arms, the resilience delta, the event reports, and the
/// assumptions block. Every envelope names its engine and version, the
/// cards the engine does not fully cover, and the games that ended in an
/// engine error.
/// </summary>
public sealed record SimulationEnvelope(
    GoalWire Goal,
    [property: Description("The tier-2 floor. In tier-3 mode, the baseline arm's floor.")]
    FloorProbability Probability,
    [property: Description("The run count. In tier-3 mode, the number of pairs; each arm plays this many games.")]
    int Runs,
    int Successes,
    long BaseSeed,
    int TurnHorizon,
    PolicyNames Policies,
    [property: Description("The engine that played every run, and its version. Floors from different engines or versions are not comparable.")]
    EngineInfo Engine,
    [property: Description("Per-turn cumulative share of runs whose goal was met by that turn. In tier-3 mode, the baseline arm's curve.")]
    IReadOnlyList<CurvePoint> Curve,
    [property: Description("Up to three replayable runs: fastest success, typical success, first failure. In tier-3 mode, the baseline arm's.")]
    IReadOnlyList<ExemplarLine> Exemplars,
    [property: Description("The disruption profile of the disrupted arm; absent in a tier-2 run.")]
    IReadOnlyList<DisruptionEventWire>? DisruptionProfile,
    [property: Description("The disruption mode of the disrupted arm: abstracted or interactive. Present whenever disruption_profile is; absent in a tier-2 run.")]
    string? DisruptionMode,
    [property: Description("The tier-3 report over paired same-seed runs; absent in a tier-2 run.")]
    DisruptionReport? Disruption,
    [property: Description("The cards the floor does not fully cover: the unimplemented cards run as blank cards; the unprofiled cards run without pilot features.")]
    EngineCoverage EngineCoverage,
    [property: Description("The games that ended in an engine error. Each counts as a failure. In tier-3 mode, both arms together. A request with more than 1% fails.")]
    int EngineErrors,
    [property: Description("The deck verdict of the decklist in the format, as the deck verdict route returns it but with no versions block of its own: card legality at the resolved banlist version in Commander, and deck shape. An illegal deck still runs. The status is unknown until a banlist replay has run, or in a Brawl format until the legality rebuild has run.")]
    DeckVerdict DeckVerdict,
    DecklistEcho Input,
    IReadOnlyList<string> Simplifications,
    VersionsBlock Versions)
{
    [System.ComponentModel.Description("The A- names that resolved only without the prefix, each with the card it resolved to; absent when there is none.")]
    public IReadOnlyList<NameRewrite>? NameRewrites { get; init; }


    /// <summary>In tier-3 mode the result is the baseline arm; the
    /// envelope maps it once and shares the arm with the report. The
    /// engine names itself, its version and its simplifications.
    /// <paramref name="engineErrors"/> counts both arms; the endpoint
    /// computes it once for its 1% gate and passes it here. An interactive
    /// envelope adds the deck's <paramref name="responses"/>: the response
    /// cards in the disruption block, the unprofiled responses in engine
    /// coverage.</summary>
    public static SimulationEnvelope Build(
        MonteCarloResult result,
        PairedResult? paired,
        IGameEngine engine,
        EngineCoverage engineCoverage,
        int engineErrors,
        DeckVerdict deckVerdict,
        DecklistEcho input,
        VersionsBlock versions,
        DeckResponses? responses = null)
    {
        var arm = ArmReport.Of(result);
        if (paired?.Profile.Mode == MtgArchive.Simulation.Engine.DisruptionMode.Interactive)
        {
            engineCoverage = engineCoverage with { UnprofiledResponses = (responses ?? DeckResponses.None).UnprofiledResponses };
        }

        return new(
            GoalWire.Of(result.Goal),
            arm.Probability,
            result.Runs,
            result.Successes,
            result.BaseSeed,
            result.TurnHorizon,
            result.Policies,
            new EngineInfo(engine.Name, engine.Version),
            arm.Curve,
            arm.Exemplars,
            paired?.Profile.Events.Select(DisruptionEventWire.Of).ToList(),
            paired?.Profile.ModeName,
            paired is null ? null : DisruptionReport.Of(paired, arm, responses, result.Policies.Play),
            engineCoverage,
            engineErrors,
            deckVerdict,
            input,
            engine.SimplificationsFor(paired?.Profile.Mode),
            versions);
    }
}

/// <summary>The engine behind a floor: <c>xmage</c>, and its version
/// (glossary: Result envelope).</summary>
public sealed record EngineInfo(
    [property: Description("xmage.")] string Name,
    [property: Description("The sidecar's version: the XMage tag plus the hash of the patch series and the shim.")] string Version);

/// <summary>
/// The tier-3 extension: both arms of the paired batch, the resilience
/// delta, one report per event, and the assumptions block. Both arms
/// run the identical derived seeds.
/// </summary>
public sealed record DisruptionReport(
    [property: Description("The arm without the profile: the tier-2 run under the same seeds.")]
    ArmReport Baseline,
    [property: Description("The arm with the profile.")]
    ArmReport Disrupted,
    [property: Description("The baseline floor minus the disrupted floor. The arms share every seed, so the delta carries no sampling noise between them.")]
    double ResilienceDelta,
    [property: Description("One report per event of the profile, in profile order.")]
    IReadOnlyList<EventReport> Events,
    [property: Description("The modeling assumptions of the disruption mode. Abstracted: no protection, blind pilot, adversarial targeting, no indestructible. Interactive: the disruption cards, the opponent's mana rule, the fire points, adversarial targeting with the can-affect skip, one answer deep, the greedy response rule, the blind pilot, the real rules, effect casts, one-shot events and the unprofiled responses.")]
    IReadOnlyList<string> Assumptions,
    [property: Description("Interactive only: the deck's response cards, sorted by name: the cards whose response feature proves they can answer a disruption card. Absent in abstracted mode.")]
    IReadOnlyList<string>? ResponseCards = null)
{
    /// <param name="baseline">The baseline arm, mapped once by the caller.</param>
    /// <param name="responses">The deck's responses; an interactive report lists its response cards.</param>
    /// <param name="playPolicy">The play policy that ran; the hold policy adds its limits to the interactive assumptions.</param>
    public static DisruptionReport Of(PairedResult paired, ArmReport baseline, DeckResponses? responses = null, string? playPolicy = null)
    {
        var interactive = paired.Profile.Mode == DisruptionMode.Interactive;
        return new(
            baseline,
            ArmReport.Of(paired.Disrupted),
            paired.ResilienceDelta,
            [.. paired.Events.Select(e => EventReport.Of(e, interactive))],
            DisruptionProfile.AssumptionsFor(paired.Profile.Mode, playPolicy),
            interactive ? (responses ?? DeckResponses.None).ResponseCards : null);
    }
}

/// <summary>One arm of a paired batch: its floor with the 95% interval,
/// its success count, its cumulative curve, and its exemplars.</summary>
public sealed record ArmReport(
    FloorProbability Probability,
    int Successes,
    IReadOnlyList<CurvePoint> Curve,
    IReadOnlyList<ExemplarLine> Exemplars)
{
    public static ArmReport Of(MonteCarloResult result)
        => new(FloorProbability.Of(result), result.Successes, CurvePoint.Of(result), ExemplarLine.Of(result));
}

/// <summary>One event over the disrupted arm. Under interactive disruption
/// "fired" means the scripted opponent cast the event's disruption card,
/// and <see cref="NotCastRate"/> is set.</summary>
public sealed record EventReport(
    string Kind,
    int Turn,
    [property: Description("The share of runs the event fires in. Interactive: the share of runs in which the opponent cast the event's disruption card.")]
    double FiredRate,
    [property: Description("The mean fire turn over the runs the event fires in; absent when it never fires. Interactive: the mean turn the opponent cast the card.")]
    double? MeanFiredTurn,
    [property: Description("True when the event fires in no run. Interactive: the opponent cast the card in no run.")]
    bool Unused,
    [property: Description("Interactive only: the share of runs in which our deck answered the disruption card after the opponent cast it. It is at most fired_rate, and a lower bound: an unprofiled response never answers. Absent in abstracted mode.")]
    double? AnsweredRate = null,
    [property: Description("Interactive only: the share of runs in which the disruption card was never cast, for no legal and affectable target or a lock. Fired plus not cast is at most 1. Absent in abstracted mode.")]
    double? NotCastRate = null)
{
    public static EventReport Of(EventSummary summary) => Of(summary, interactive: false);

    public static EventReport Of(EventSummary summary, bool interactive)
        => new(summary.Event.KindName, summary.Event.Turn, summary.FiredRate, summary.MeanFiredTurn, summary.Unused,
            interactive ? summary.AnsweredRate : null,
            interactive ? summary.NotCastRate : null);
}

/// <summary>The floor-labeled probability: the success rate, its label in
/// the exact format ">= X%, greedy policy", and the 95% confidence interval.</summary>
public sealed record FloorProbability(
    [property: Description("The success rate. A floor under the greedy pilot, never a point estimate.")] double Floor,
    [property: Description("Exactly \">= X%, greedy policy\"; X rounds down.")] string Label,
    ConfidenceInterval ConfidenceInterval)
{
    public static FloorProbability Of(MonteCarloResult result)
        => new(result.SuccessRate, GoalAwarePilot.FloorLabel(result.SuccessRate), result.Interval);
}

public sealed record CurvePoint(int Turn, double Cumulative)
{
    public static IReadOnlyList<CurvePoint> Of(MonteCarloResult result)
        => [.. result.Curve.Select((share, index) => new CurvePoint(index + 1, share))];
}

public sealed record ExemplarLine(
    ExemplarKind Kind,
    int RunIndex,
    int Seed,
    bool Success,
    int? MetTurn,
    IReadOnlyList<LogLine> Log,
    [property: Description("The fired events of the run as (turn, kind, victim), in fire order; absent when the run has no profile.")]
    IReadOnlyList<ExemplarEventLine>? Events)
{
    public static IReadOnlyList<ExemplarLine> Of(MonteCarloResult result)
        => [.. result.Exemplars.Select(e => new ExemplarLine(
            e.Kind, e.RunIndex, e.Seed, e.Success, e.MetTurn,
            [.. e.Log.Select(l => new LogLine(l.Turn, l.Step, l.Message))],
            e.Events?.Select(x => new ExemplarEventLine(x.Turn, x.Kind, x.Victim, x.Hit, x.AnsweredBy)).ToList()))];
}

/// <summary>One fired event in an exemplar: a board wipe with several
/// victims is several entries on one turn.</summary>
public sealed record ExemplarEventLine(
    int Turn,
    string Kind,
    string Victim,
    [property: Description("Interactive only: true when the disruption card hit this victim. Absent in abstracted mode.")]
    bool? Hit = null,
    [property: Description("Interactive only: the cards our deck answered the disruption card with, from the service's own respond replies; the same on every entry of the event, and empty when the card hit or left the stack without our response. Absent in abstracted mode.")]
    IReadOnlyList<string>? AnsweredBy = null);

public sealed record LogLine(int Turn, TurnStep Step, string Message);

/// <summary>The decklist as simulated: its library size and its commanders.</summary>
public sealed record DecklistEcho(int DeckSize, IReadOnlyList<Citation> Commanders);
