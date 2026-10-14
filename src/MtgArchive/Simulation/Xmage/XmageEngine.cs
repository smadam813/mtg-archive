using System.Text.Json;
using System.Text.Json.Nodes;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Simulation.Xmage;

/// <summary>
/// The XMage sidecar behind <see cref="IGameEngine"/> (glossary: Engine
/// adapter; spec §3). The service drives the batch; the sidecar plays one
/// game per <c>start_game</c> and asks the pilot through the wire view.
/// <see cref="Version"/> is the version the sidecar reports in its hello,
/// so every floor names the build that played it.
/// </summary>
public sealed class XmageEngine : IGameEngine
{
    public const string EngineName = "xmage";

    /// <summary>What an XMage run under the game rules does not model;
    /// the envelope echoes it next to the floor. The game rules give the
    /// start life, the draw line, and the free mulligans line (ADR 0012).</summary>
    public static IReadOnlyList<string> SimplificationsFor(GameRules rules) => SimplificationsFor(rules, null);

    /// <summary>The simplifications of a run in a disruption mode. Under
    /// interactive disruption a "Scripted opponent" line replaces the "Idle
    /// opponent" line for both arms, and the sorcery-speed line names the
    /// response window (ADR 0013); every other mode keeps the tier-2 list.</summary>
    public static IReadOnlyList<string> SimplificationsFor(GameRules rules, DisruptionMode? mode) =>
    [
        mode == DisruptionMode.Interactive
            ? $"Scripted opponent: one opponent at {rules.StartLife} life takes no action of its own, blocks nothing and cannot lose. In the disrupted arm it casts the disruption cards the profile schedules, inside our turn; the baseline arm plays the idle opponent."
            : $"Idle opponent: one opponent at {rules.StartLife} life takes no action, blocks nothing and cannot lose. Only a disruption profile the caller schedules answers a card.",
        rules.StartingPlayerDraws
            ? "Turn-1 draw: the player draws on turn 1 (CR 103.8c, multiplayer)."
            : "On the play: no turn-1 draw (CR 103.8a).",
        rules.FreeMulligans switch
        {
            0 => "Free mulligans: none. Each mulligan puts one more card on the bottom.",
            1 => "Free mulligans: 1. The first mulligan puts no card on the bottom.",
            var n => $"Free mulligans: {n}. The first {n} mulligans put no card on the bottom.",
        },
        mode == DisruptionMode.Interactive
            ? "Sorcery speed: the pilot acts only in its upkeep, its first main phase, its end step and the response window, and casts instants there like sorceries. In the disrupted arm the response window opens once per disruption card, while the card is on top of the stack."
            : SorcerySpeed,
        .. SharedSimplifications,
    ];

    private const string SorcerySpeed = "Sorcery speed: the pilot acts only in its upkeep, its first main phase and its end step, and casts instants there like sorceries.";

    private static readonly IReadOnlyList<string> SharedSimplifications =
    [
        "No combat: the player declares no attackers.",
        "Engine choices: XMage pays mana and chooses targets, modes and X. The pilot orders the cards put back on top of the library, picks Brainstorm's cards, and decides optional shuffles; a scry and a mulligan put their cards on the bottom in pick order.",
        "Loop guard: one ability activates at most twice per turn.",
        "Blank cards: a card XMage does not implement keeps its library slot, has no cost and no abilities, and is never cast. It is an instant, so a search for an instant can find it, and a missing land never plays as a land. engine_coverage.unimplemented lists it.",
        "Unprofiled cards: XMage runs them in full, but the pilot reads no feature their card feature profile cannot prove. engine_coverage.unprofiled lists them.",
        "A win ends the run: a goal leaf due after the win fails.",
    ];

    private readonly XmageSidecar _sidecar;

    private readonly Func<IReadOnlyList<DeckCard>, CancellationToken, Task<DeckFeatureProfiles>> _profiles;

    private XmageEngine(XmageSidecar sidecar, WireHello hello, Func<IReadOnlyList<DeckCard>, CancellationToken, Task<DeckFeatureProfiles>> profiles, int? parallelism, GameRules rules)
    {
        _sidecar = sidecar;
        _profiles = profiles;
        Version = hello.Version;
        Parallelism = parallelism ?? hello.Threads;
        ArgumentOutOfRangeException.ThrowIfLessThan(Parallelism, 1, nameof(parallelism));
        Rules = rules;
        Simplifications = SimplificationsFor(rules);
    }

    public string Name => EngineName;

    public string Version { get; }

    public int Parallelism { get; }

    /// <summary>The game rules every run of this engine plays under.</summary>
    public GameRules Rules { get; }

    public IReadOnlyList<string> Simplifications { get; }

    public IReadOnlyList<string> SimplificationsFor(DisruptionMode? mode)
        => mode == DisruptionMode.Interactive ? SimplificationsFor(Rules, mode) : Simplifications;

    /// <summary>The engine for one request. It says hello once per sidecar
    /// and takes the sidecar's version and thread count.</summary>
    /// <param name="profiles">Loads a deck's card feature profiles; the endpoint reads them from the database.</param>
    /// <param name="parallelism">The most runs at once; the sidecar's game threads when null.</param>
    /// <param name="rules">The format's game rules; Commander when null.</param>
    public static async Task<XmageEngine> ConnectAsync(
        XmageSidecar sidecar,
        Func<IReadOnlyList<DeckCard>, CancellationToken, Task<DeckFeatureProfiles>> profiles,
        int? parallelism,
        CancellationToken cancellationToken,
        GameRules? rules = null)
        => new(sidecar, await sidecar.HelloAsync(cancellationToken).ConfigureAwait(false), profiles, parallelism, rules ?? GameRules.Commander);

    /// <summary>Loads the deck into the sidecar and its profiles, once per batch.</summary>
    public async Task<IPreparedDeck> PrepareAsync(IReadOnlyList<DeckCard> deck, CancellationToken cancellationToken)
    {
        var profiles = await _profiles(deck, cancellationToken).ConfigureAwait(false);
        var reply = await ExchangeAsync(XmageRequests.LoadDeck(deck), cancellationToken).ConfigureAwait(false);
        if (reply.Type != "deck")
        {
            var error = reply.Type == "game_over" && reply.Json.TryGetProperty("error", out var e) ? e.GetString() : null;
            throw new XmageUnavailableException($"The XMage sidecar refused the deck: {error ?? $"a {reply.Type ?? "reply"} message"}.");
        }

        WireDeck loaded;
        try
        {
            loaded = reply.As<WireDeck>();
        }
        catch (JsonException ex)
        {
            throw new XmageUnavailableException($"The XMage sidecar sent a deck reply the service cannot read: {ex.Message}", ex);
        }

        return new XmageDeck(_sidecar, loaded.DeckId, deck, profiles, loaded.Unimplemented, Rules);
    }

    /// <summary>One request and its reply on a leased connection. A dead
    /// idle connection gets one retry on a new one.</summary>
    private Task<XmageMessage> ExchangeAsync(JsonObject request, CancellationToken cancellationToken)
        => _sidecar.WithConnectionAsync<XmageMessage>(
            async (connection, ct) =>
            {
                await connection.SendAsync(request, ct).ConfigureAwait(false);
                return await connection.ReceiveAsync(ct).ConfigureAwait(false);
            },
            mayRetry: () => true,
            failed: ex => throw new XmageUnavailableException($"The XMage sidecar failed a {(string?)request["type"]}: {ex.Message}", ex),
            cancellationToken);
}

/// <summary>
/// A deck the sidecar loaded: its deck id, the view builder, and its
/// coverage. <see cref="EngineCoverage.Unimplemented"/> lists the cards
/// XMage lacks, which play as blank cards; <see cref="EngineCoverage.Unprofiled"/>
/// lists the cards without a full card feature profile. The runs of a
/// batch share it and play under its game rules; a run holds its own
/// connection.
/// </summary>
public sealed class XmageDeck : IPreparedDeck
{
    private readonly XmageSidecar _sidecar;

    internal XmageDeck(XmageSidecar sidecar, string deckId, IReadOnlyList<DeckCard> deck, DeckFeatureProfiles profiles, IReadOnlyList<string> unimplemented, GameRules rules)
    {
        _sidecar = sidecar;
        Rules = rules;
        DeckId = deckId;
        Views = new XmageViews(deck, profiles, unimplemented);
        Coverage = new EngineCoverage([.. unimplemented.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], profiles.Unprofiled);
        Responses = new DeckResponses(profiles.ResponseCards, profiles.UnprofiledResponses);
    }

    public DeckResponses Responses { get; }

    public string DeckId { get; }

    /// <summary>The game rules every run of the batch plays under.</summary>
    public GameRules Rules { get; }

    public XmageViews Views { get; }

    public EngineCoverage Coverage { get; }

    /// <summary>
    /// Plays one run. A connection that fails, goes silent or breaks the
    /// protocol ends the run in an engine error. A dead idle connection gets
    /// one retry on a new connection before the game starts. No connection
    /// at all throws <see cref="XmageUnavailableException"/>, which fails
    /// the batch.
    /// </summary>
    public async Task<RunRecord> PlayAsync(RunSpec spec, IPilotPolicy pilot, IMulliganPolicy mulligan, CancellationToken cancellationToken)
    {
        // A new game per attempt: a retried game starts clean.
        XmageGame game = null!;
        return await _sidecar.WithConnectionAsync(
            (connection, ct) => (game = new XmageGame(Views, spec, pilot, mulligan, DeckId, Rules)).PlayAsync(connection, ct),
            mayRetry: () => !game.Started,
            failed: ex => game.EngineError(ex.Message),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// One run on the sidecar: <c>start_game</c>, then one reply per decide and
/// event until <c>game_over</c>. It evaluates the goal on each end-of-turn
/// view; with early stop it answers <c>stop</c> once the goal is settled
/// (spec §4.4). A game that ends inside a turn (a win, a loss) is scored on
/// the final view that <c>game_over</c> carries. Under a disruption
/// profile (spec §7) the run's own <see cref="DisruptionSchedule"/> picks
/// each victim: a cast event may get <c>counter</c>, an end of turn may get
/// <c>disrupt</c>, and the goal then reads the after-disruption view. Under
/// interactive disruption (ADR 0013) both answer <c>oppose</c> instead, and
/// the schedule commits on the <c>opposed</c> event.
/// </summary>
internal sealed class XmageGame(XmageViews views, RunSpec spec, IPilotPolicy pilot, IMulliganPolicy mulligan, string deckId, GameRules rules)
{
    /// <summary>The shim's end-of-turn snapshot line (GameCtx.snapshot).
    /// A turn's service lines follow its last one in the run's log.</summary>
    internal const string EndSnapshotPrefix = "END life=";

    private readonly GoalProgress? _goal = spec.Goal is null ? null : new GoalProgress(spec.Goal);

    /// <summary>The batch runner's profile, never the pilot's (ADR 0005).
    /// A retried game is a new <see cref="XmageGame"/>, so it starts fresh.</summary>
    private readonly DisruptionSchedule? _schedule = spec.Disruption is null ? null : new DisruptionSchedule(spec.Disruption, spec.Goal, views.Deck);

    /// <summary>The service's own log lines: each disruption fire and each
    /// goal line, placed after its turn's end-of-turn snapshot.</summary>
    private readonly List<GameLogEntry> _lines = [];

    private PilotView? _last;

    private int _recordedTurn;

    private int _seq;

    /// <summary>The service answered disrupt; the goal waits for this
    /// turn's after-disruption view.</summary>
    private bool _awaitingAfter;

    /// <summary>Interactive disruption: the event the service proposed with
    /// oppose, its disruption card and its victim, until the opposed event
    /// settles it. The schedule commits only then.</summary>
    private Proposal? _pending;

    /// <summary>The card of the service's last respond reply for the
    /// pending disruption card; null for a pass. A later respond decide for
    /// the same card means the shim's activation failed, so it replaces the
    /// earlier one. The shim sends that decide even with no action left, so
    /// a failed card never stays here.</summary>
    private string? _answeredWith;

    /// <summary>Interactive disruption: the end-of-turn events the service
    /// proposed in this end step. The end-step chain proposes each one at
    /// most once per step, so an event that is not cast waits for the next
    /// end step.</summary>
    private readonly HashSet<DisruptionOutcome> _triedThisStep = [];

    /// <summary>A proposal: the event, its disruption card, the victim
    /// names for the log, and whether it belongs to an end-step sequence.</summary>
    private sealed record Proposal(DisruptionOutcome Outcome, DisruptionCard Card, string Victim, bool EndStep = false);

    private bool Interactive => spec.Disruption?.Mode == DisruptionMode.Interactive;

    /// <summary>The sidecar sent the game's first message.</summary>
    public bool Started { get; private set; }

    /// <summary>The shim sends cast events only when the profile holds a counterspell.</summary>
    private bool WatchesCasts => spec.Disruption?.Events.Any(e => e.Kind == DisruptionKind.Counterspell) == true;

    /// <summary>The scripted opponent's cards: the disruption card table
    /// in an interactive disrupted arm (ADR 0013), else null for the idle
    /// opponent. A baseline arm has no profile, so it always seats the idle
    /// opponent.</summary>
    private IReadOnlyList<string>? Opponent => OpponentOf(spec.Disruption);

    internal static IReadOnlyList<string>? OpponentOf(DisruptionProfile? disruption)
        => disruption?.Mode == DisruptionMode.Interactive ? [.. DisruptionCards.All.Select(c => c.Name)] : null;

    public async Task<RunRecord> PlayAsync(XmageConnection connection, CancellationToken cancellationToken)
    {
        await connection.SendAsync(
            XmageRequests.StartGame(deckId, spec.Seed, spec.TurnHorizon, rules, WatchesCasts, log: spec.CaptureLog, Opponent),
            cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var message = await connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            Started = true;
            try
            {
                switch (message.Type)
                {
                    case "decide":
                        await connection.SendAsync(Decide(message.As<WireDecide>()), cancellationToken).ConfigureAwait(false);
                        break;
                    case "event":
                        await connection.SendAsync(Event(message.As<WireEvent>()), cancellationToken).ConfigureAwait(false);
                        break;
                    case "game_over":
                        return Over(message.As<WireGameOver>());
                    default:
                        throw new XmageConnectionException($"A {message.Type ?? "reply-shaped"} message arrived inside a game.");
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                throw new XmageConnectionException($"The sidecar sent a {message.Type} message the service cannot read: {ex.Message}", ex);
            }
        }
    }

    /// <summary>The record of a game that ended in an engine error: a
    /// failure (spec §4.5). Under a profile it keeps one outcome per event,
    /// so the batch still aggregates.</summary>
    public RunRecord EngineError(string error, IReadOnlyList<WireLogEntry>? log = null)
        => new(
            false,
            null,
            EngineError: true,
            spec.CaptureLog ? [.. Log(log) ?? [], new GameLogEntry(_last?.Turn ?? 0, _last?.Step ?? TurnStep.Mulligan, $"Engine error: {error}")] : null,
            _schedule?.Events);

    private JsonObject Decide(WireDecide decide)
    {
        ExpectSeq(decide.Seq);
        var view = views.View(decide.View);
        _last = view;
        return decide.Kind switch
        {
            "keep" => XmageRequests.Keep(decide.Seq, mulligan.KeepHand(view, MulliganOf(decide))),
            "bottom" => XmageRequests.Cards(decide.Seq, mulligan.ChooseBottom(view, new BottomChoice(view.Hand, CountOf(decide))).Select(XmageViews.WireName)),
            "action" => XmageRequests.Action(decide.Seq, pilot.ChooseAction(view)),
            "pick" => XmageRequests.Option(decide.Seq, XmageViews.WireOptionId(pilot.Pick(view, views.Pick(decide.Pick ?? throw Missing("pick", decide.Kind))))),
            "discard" => XmageRequests.Cards(decide.Seq, pilot.ChooseDiscards(view, CountOf(decide)).Select(XmageViews.WireName)),
            "respond" => Respond(decide.Seq, view, decide.Respond ?? throw Missing("respond", decide.Kind)),
            _ => throw new XmageConnectionException($"Decide kind '{decide.Kind}' is not in wire protocol {XmageWire.Protocol}."),
        };
    }

    /// <summary>
    /// The response window of the pending disruption card (ADR 0013). The
    /// threat maps through the disruption card table, never through the
    /// deck's card lookup. The pilot's answer is the reply, and its card is
    /// what <c>answered_by</c> names. A respond decide with no disruption
    /// card pending, or for another card, breaks the protocol.
    /// </summary>
    private JsonObject Respond(int seq, PilotView view, WireRespond respond)
    {
        var pending = _pending ?? throw new XmageConnectionException("A respond decide arrived with no disruption card pending.");
        var threat = DisruptionCards.All.FirstOrDefault(c => c.Name == respond.Threat.Card)
            ?? throw new XmageConnectionException($"A respond decide names {respond.Threat.Card}, which is not a disruption card.");
        if (threat != pending.Card)
        {
            throw new XmageConnectionException($"A respond decide names {threat.Name}, but {pending.Card.Name} was pending.");
        }

        var choice = new ResponseChoice(threat, respond.Threat.Id, [.. respond.Victims.Select(v => new ResponseVictim(v.Id, views.Face(v.Card, v.Face)))]);
        var action = pilot.Respond(view, choice);
        _answeredWith = action is null ? null : view.Actions.FirstOrDefault(a => a.Id == action)?.Face.Whole().Name;
        return XmageRequests.Action(seq, action);
    }

    private JsonObject Event(WireEvent @event)
    {
        ExpectSeq(@event.Seq);
        var view = views.View(@event.View);
        _last = view;
        return @event.Kind switch
        {
            "cast" => Cast(@event.Seq, view, @event.Spell ?? throw Missing("spell", @event.Kind)),
            "end_of_turn" => EndOfTurnEvent(@event.Seq, view, @event.AfterDisruption ?? throw Missing("after_disruption", @event.Kind)),
            "resolved" => XmageRequests.Do(@event.Seq, "continue"),
            "opposed" when Interactive => Opposed(@event.Seq, view, @event),
            _ => throw new XmageConnectionException($"Event kind '{@event.Kind}' is not in wire protocol {XmageWire.Protocol}."),
        };
    }

    /// <summary>Our spell meets an armed counterspell (spec §7): the
    /// schedule decides, reading the cards the deck can still reach. Under
    /// interactive disruption the same predicate proposes, and the service
    /// answers oppose with the cast spell as the only target.</summary>
    private JsonObject Cast(int seq, PilotView view, WireSpell spell)
    {
        if (_schedule is null)
        {
            throw new XmageConnectionException("A cast event arrived in a game that does not watch casts.");
        }

        if (_pending is not null)
        {
            throw new XmageConnectionException($"A cast event arrived while {_pending.Card.Name} was pending.");
        }

        var card = views.Face(spell.Card, spell.Face);
        if (Interactive)
        {
            if (_schedule.ProposeCounter(view.Turn, card, view, views.Reachable(view, card)) is not { } proposed)
            {
                return XmageRequests.Do(seq, "continue");
            }

            _pending = new Proposal(proposed, DisruptionCards.For(proposed.Event.Kind), card.Name);
            _answeredWith = null;
            return XmageRequests.Oppose(seq, _pending.Card.Name, [spell.Id]);
        }

        if (_schedule.Counter(view.Turn, card, view, views.Reachable(view, card)) is not { } fired)
        {
            return XmageRequests.Do(seq, "continue");
        }

        _lines.Add(new GameLogEntry(view.Turn, view.Step, fired.LogLine));
        return XmageRequests.Do(seq, "counter");
    }

    /// <summary>
    /// The fate of the pending disruption card (ADR 0013). The schedule
    /// commits on hit or answered. An answered outcome counts as answered
    /// only after our respond reply; without one, the card fired and took
    /// no victim. A card that was not cast commits
    /// nothing, and its event stays armed for the next fire point. An
    /// opposed event with nothing pending, or for another card, breaks the
    /// protocol.
    /// </summary>
    private JsonObject Opposed(int seq, PilotView view, WireEvent @event)
    {
        var pending = _pending ?? throw new XmageConnectionException("An opposed event arrived with no disruption card pending.");
        if (@event.Card != pending.Card.Name)
        {
            throw new XmageConnectionException($"An opposed event names {@event.Card}, but {pending.Card.Name} was pending.");
        }

        var victims = @event.Victims ?? throw Missing("victims", @event.Kind);
        _pending = null;
        // An end-step card names the candidate the shim took; with none, the candidates the service proposed.
        var on = pending.EndStep && victims.Count > 0 ? string.Join(", ", victims.Select(v => v.Card)) : pending.Victim;
        var line = $"Disruption: {pending.Outcome.Event.KindName} casts {pending.Card.Name} on {on}; ";
        switch (@event.Outcome)
        {
            case "hit" when @event.Reason is null:
                _schedule!.Commit(pending.Outcome, view.Turn, [.. victims.Select(v => new VictimHit(v.Card, v.Hit))]);
                line += "it hits.";
                break;
            case "answered" when @event.Reason is null && _answeredWith is { } card:
                _schedule!.Commit(pending.Outcome, view.Turn, [.. victims.Select(v => new VictimHit(v.Card, v.Hit))], [card]);
                line += $"answered by {card}.";
                break;
            case "answered" when @event.Reason is null:
                // The card took no victim without our response: it fired, but our deck did not answer it.
                _schedule!.Commit(pending.Outcome, view.Turn, [.. victims.Select(v => new VictimHit(v.Card, Hit: false))]);
                line += "it takes no victim.";
                break;
            case "not_cast" when @event.Reason is "no_target" or "cannot_cast":
                _schedule!.NotCast(pending.Outcome, @event.Reason);
                line += $"it cannot be cast ({@event.Reason.Replace('_', ' ')}).";
                break;
            default:
                throw new XmageConnectionException($"An opposed event with outcome '{@event.Outcome}' and reason '{@event.Reason}' is not in wire protocol {XmageWire.Protocol}.");
        }

        _lines.Add(new GameLogEntry(view.Turn, view.Step, line));
        return pending.EndStep && ProposeEndStep(seq, view) is { } next
            ? next
            : XmageRequests.Do(seq, "continue");
    }

    /// <summary>
    /// The end-step chain (ADR 0013): the first armed end-of-turn event in
    /// profile order that this step has not tried, on the battlefield of
    /// the latest settled view, one proposal at a time. The service answers
    /// oppose with its disruption card and the candidates in order; null
    /// when no event is left to propose.
    /// </summary>
    private JsonObject? ProposeEndStep(int seq, PilotView view)
    {
        if (_schedule?.ProposeEndOfTurn(view.Turn, view.Battlefield, _triedThisStep) is not { } proposed)
        {
            return null;
        }

        _triedThisStep.Add(proposed.Outcome);
        var card = DisruptionCards.For(proposed.Outcome.Event.Kind);
        _pending = new Proposal(proposed.Outcome, card, string.Join(", ", proposed.Candidates.Select(c => c.Card.Name)), EndStep: true);
        _answeredWith = null;
        // A wipe takes no target: Rout's own text decides what dies, so its targets are empty.
        return XmageRequests.Oppose(seq, card.Name, proposed.Outcome.Event.Kind == DisruptionKind.BoardWipe ? [] : proposed.Candidates.Select(c => c.Id));
    }

    /// <summary>
    /// The end of a turn. Each armed removal and board wipe fires first, on
    /// the view's battlefield; with a victim, the service answers disrupt,
    /// and the goal reads the after-disruption view instead. Under
    /// interactive disruption the service answers oppose instead and starts
    /// the end-step chain; the goal reads the after view that follows the
    /// chain's last opposed event. A run under a profile never stops early
    /// (spec §4.4), so only a tier-2 run stops.
    /// </summary>
    private JsonObject EndOfTurnEvent(int seq, PilotView view, bool afterDisruption)
    {
        if (afterDisruption != _awaitingAfter)
        {
            throw new XmageConnectionException(afterDisruption
                ? "An after-disruption view arrived with no disruption before it."
                : "An end-of-turn view arrived where the after-disruption view was due.");
        }

        if (_pending is not null)
        {
            throw new XmageConnectionException($"An end-of-turn view arrived while {_pending.Card.Name} was pending.");
        }

        _awaitingAfter = false;
        if (!afterDisruption && Interactive)
        {
            _triedThisStep.Clear();
            if (ProposeEndStep(seq, view) is { } oppose)
            {
                _awaitingAfter = true;
                return oppose;
            }
        }
        else if (!afterDisruption && _schedule?.EndOfTurn(view.Turn, view.Battlefield) is { Count: > 0 } fires)
        {
            foreach (var fire in fires)
            {
                _lines.Add(new GameLogEntry(view.Turn, TurnStep.End, fire.Outcome.LogLine));
            }

            _awaitingAfter = true;
            return XmageRequests.Disrupt(seq, fires.SelectMany(f => f.Victims).Select(v => v.Id).Distinct(StringComparer.Ordinal));
        }

        EndOfTurn(view);
        return XmageRequests.Do(seq, spec.EarlyStop && _goal?.IsSettled == true ? "stop" : "continue");
    }

    private RunRecord Over(WireGameOver over)
    {
        if (over.Reason == "engine_error")
        {
            return EngineError(over.Error ?? "engine_error without a message", over.Log);
        }

        if (over.Reason is not ("horizon" or "stopped" or "life" or "win"))
        {
            throw new XmageConnectionException($"game_over reason '{over.Reason}' is not in wire protocol {XmageWire.Protocol}.");
        }

        if (_pending is not null)
        {
            throw new XmageConnectionException($"The game ended ({over.Reason}) before the opposed event of {_pending.Card.Name}.");
        }

        if (over.Reason is "life" or "win")
        {
            // The winning spell's trigger is still on the stack when the spell
            // resolves, so no event carried it: only the final view holds it.
            EndOfTurn(views.View(over.View ?? throw new XmageConnectionException($"A game_over with reason '{over.Reason}' has no final view.")));
        }
        else if (_awaitingAfter)
        {
            throw new XmageConnectionException($"The game ended ({over.Reason}) before the after-disruption view.");
        }

        _goal?.Finish(line => _lines.Add(new GameLogEntry(Math.Max(1, over.Turns), TurnStep.End, line)));
        return new RunRecord(_goal?.Verdict == true, _goal?.MetTurn, EngineError: false, Log(over.Log), _schedule?.Events) { Digest = over.Digest };
    }

    /// <summary>Evaluates the goal at the end of a turn, once per turn.</summary>
    private void EndOfTurn(PilotView view)
    {
        if (_goal is null || view.Turn < 1 || view.Turn <= _recordedTurn)
        {
            return;
        }

        _recordedTurn = view.Turn;
        _goal.EndOfTurn(view, line => _lines.Add(new GameLogEntry(view.Turn, TurnStep.End, line)));
    }

    /// <summary>The run's log, when the spec asked for it: the sidecar's
    /// lines, with each turn's service lines after that turn's last
    /// end-of-turn snapshot, and the rest at the end. A disrupted turn has
    /// two snapshots; the goal reads the second.</summary>
    private IReadOnlyList<GameLogEntry>? Log(IReadOnlyList<WireLogEntry>? wire)
    {
        if (!spec.CaptureLog)
        {
            return null;
        }

        wire ??= [];
        var lastSnapshot = new Dictionary<int, int>();
        for (var i = 0; i < wire.Count; i++)
        {
            if (wire[i].Message.StartsWith(EndSnapshotPrefix, StringComparison.Ordinal))
            {
                lastSnapshot[wire[i].Turn] = i;
            }
        }

        var lines = _lines.ToLookup(l => l.Turn);
        var log = new List<GameLogEntry>();
        for (var i = 0; i < wire.Count; i++)
        {
            var line = wire[i];
            log.Add(new GameLogEntry(line.Turn, XmageViews.Step(line.Step), line.Message));
            if (lastSnapshot.GetValueOrDefault(line.Turn, -1) == i)
            {
                log.AddRange(lines[line.Turn]);
            }
        }

        var placed = lastSnapshot.Keys.ToHashSet();

        log.AddRange(_lines.Where(l => !placed.Contains(l.Turn)));
        return log;
    }

    private void ExpectSeq(int seq)
    {
        if (seq != ++_seq)
        {
            throw new XmageConnectionException($"Expected seq {_seq}, got {seq}.");
        }
    }

    private MulliganChoice MulliganOf(WireDecide decide)
    {
        var keep = decide.Keep ?? throw Missing("keep", decide.Kind);
        return new MulliganChoice([.. keep.Hand.Select(views.Card)], keep.MulligansTaken, keep.KeepSize);
    }

    private static int CountOf(WireDecide decide) => decide.Count ?? throw Missing("count", decide.Kind);

    private static XmageConnectionException Missing(string field, string kind) => new($"A {kind} decision without {field}.");
}
