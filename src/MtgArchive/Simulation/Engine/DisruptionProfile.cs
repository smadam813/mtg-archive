namespace MtgArchive.Simulation.Engine;

/// <summary>The closed disruption-event vocabulary (ADR 0005). It grows on
/// demand only; an unknown kind is a request error.</summary>
public enum DisruptionKind
{
    Counterspell,
    TargetedRemoval,
    BoardWipe,
}

/// <summary>One scheduled event: it unlocks on <see cref="Turn"/> and
/// stays armed until it fires once.</summary>
public sealed record DisruptionEvent(DisruptionKind Kind, int Turn)
{
    public int Turn { get; } = Turn >= 1
        ? Turn
        : throw new ArgumentOutOfRangeException(nameof(Turn), Turn, "An event unlocks on turn 1 or later.");

    public string KindName => DisruptionProfile.NameOf(Kind);

    public override string ToString() => $"{KindName} (turn {Turn})";
}

/// <summary>How the disrupted arm resolves its events (ADR 0013).
/// Abstracted disruption is tier 3 (ADR 0005): the engine moves the cards.
/// Interactive disruption is the first tier-4 question: a scripted
/// opponent casts each event as a disruption card.</summary>
public enum DisruptionMode
{
    Abstracted,
    Interactive,
}

/// <summary>
/// The caller's deterministic schedule of one-shot events, and the
/// disruption mode that resolves them. Every run of a batch faces the same
/// schedule, so the resilience delta is the deck's. A profile may hold
/// several events of one kind; each is its own one-shot. The engine never
/// supplies a profile of its own: the named default lives skill-side.
/// </summary>
public sealed record DisruptionProfile(IReadOnlyList<DisruptionEvent> Events, DisruptionMode Mode = DisruptionMode.Abstracted)
{
    public const string CounterspellKind = "counterspell";

    public const string TargetedRemovalKind = "targeted_removal";

    public const string BoardWipeKind = "board_wipe";

    public const string AbstractedMode = "abstracted";

    public const string InteractiveMode = "interactive";

    /// <summary>The vocabulary, closed at three names.</summary>
    public static readonly IReadOnlyList<string> Vocabulary = [CounterspellKind, TargetedRemovalKind, BoardWipeKind];

    /// <summary>The disruption modes by wire name.</summary>
    public static readonly IReadOnlyList<string> Modes = [AbstractedMode, InteractiveMode];

    /// <summary>The mode's wire name.</summary>
    public string ModeName => NameOf(Mode);

    public static string NameOf(DisruptionMode mode) => mode switch
    {
        DisruptionMode.Abstracted => AbstractedMode,
        DisruptionMode.Interactive => InteractiveMode,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown disruption mode."),
    };

    public static DisruptionMode ModeOf(string name) => name switch
    {
        AbstractedMode => DisruptionMode.Abstracted,
        InteractiveMode => DisruptionMode.Interactive,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, $"Unknown disruption mode; the modes are {string.Join(", ", Modes)}."),
    };

    /// <summary>The tier-3 assumptions block. Each entry is a modeling
    /// choice the disrupted floor depends on. The tier-3 envelope
    /// declares them; nothing absorbs them silently.</summary>
    public static readonly IReadOnlyList<string> Assumptions =
    [
        "No protection: there is no response window. Disruption resolves unconditionally; the deck's own counterspells and protection contribute nothing. Resilience is rebuild speed only.",
        "Blind pilot: the play and mulligan policies never see the disruption profile. Tier 3 varies the environment and nothing else.",
        "Adversarial targeting: each event hits the most expensive goal card it can, with a name tie-break. With no goal card present, it hits the most expensive legal target. A counterspell counters the first goal card cast and holds for a goal card still in the deck.",
        "No indestructible: a board wipe destroys every creature; targeted removal hits any nonland permanent.",
    ];

    /// <summary>
    /// The interactive assumptions block (ADR 0013), which replaces the
    /// tier-3 block under interactive disruption, in the spec's order: the
    /// cards, the mana rule, the fire points, targeting, one answer deep,
    /// the response rule, the blind pilot, the real rules, effect casts,
    /// one-shot events and the unprofiled responses.
    /// </summary>
    public static readonly IReadOnlyList<string> InteractiveAssumptions = InteractiveWith(TapOutSentence);

    /// <summary>The interactive entries; <paramref name="responseTail"/> ends the greedy response-rule entry.</summary>
    private static string[] InteractiveWith(string responseTail) =>
    [
        "Disruption cards: " + string.Join("; ", DisruptionCards.All.Select(c => $"{NameOf(c.Kind)} casts {c.Name} ({c.Cost}, {c.Describe()}){(c.CastWith is null ? string.Empty : " " + c.CastWith)}")) + ".",
        "Opponent mana: the scripted opponent has no lands. While its disruption card is cast or is on the stack, it adds exactly the unpaid cost and pays it, and it pays every ward cost and every \"unless its controller pays\" tax. So soft counters such as Mana Leak never stop a disruption card.",
        "Fire points: a counterspell is cast right after our spell is cast, with its costs paid; targeted removal and a board wipe are cast at our end step. The opponent acts only on our turn, and only through the profile.",
        "Adversarial targeting: each disruption card aims by the tier-3 rule. A counterspell takes the first goal card cast and holds for a goal card still in the deck. Removal tries the goal cards first, then every other nonland permanent, each by mana value descending, then by name. A board wipe fires when the tier-3 rule fires it and has no target: Rout's own text decides what dies. The opponent skips a target it cannot legally target or cannot affect: Counterspell skips a spell that can't be countered, and Atomize skips a hexproof, shroud, protected or indestructible permanent. It pays ward and does not skip it. With no target the card is not cast, and the event stays armed.",
        "One answer deep: our deck gets one response window per disruption card, at our first priority with the card on top of the stack. The opponent never answers our response, and at most one disruption card is on the stack at a time.",
        "Greedy response rule: when a disruption card threatens a goal card, the pilot answers with the cheapest response card: a counter whose reach covers the card's types and colors, or a one-shot protection grant that covers the threatened card. Against Counterspell that is \"can't be countered\"; against Atomize hexproof, shroud, indestructible, phase-out, or protection or hexproof from black or green; against Rout team indestructible or team phase-out. Phase-out counts only when every threatened goal leaf is due after this turn. It skips goal cards, \"unless its controller pays\" counters and pacts, and it may spend mana from a card in hand." + responseTail,
        "Blind pilot: the play and mulligan policies never see the disruption profile.",
        "Real rules: XMage resolves each disruption card by the rules. \"Can't be countered\", countered-spell triggers and locks apply; a lock such as Grand Abolisher, split second or Teferi, Time Raveler makes the card not cast. A countered commander goes to the command zone, and its tax rises; a destroyed commander goes to the command zone. Tokens and animated creatures die to Rout. Dies triggers resolve before the goal reads the end of the turn. Atomize's proliferate chooses nothing.",
        "Effect casts: a spell that an effect casts gets no cast event, so no counterspell can take it, and it scores no cast goal.",
        "One-shot events: an event is spent once its disruption card is cast, whether it hits or is answered. A card that is not cast spends nothing.",
        "Unprofiled responses: a response card the archive cannot profile never answers, so the answered rate is a lower bound. engine_coverage.unprofiled_responses lists those cards.",
    ];

    /// <summary>The sentence of the greedy response-rule entry that the hold
    /// policy replaces (ADR 0014).</summary>
    private const string TapOutSentence = " The main phase does not change, so the pilot may tap out or spend protection before a disruption card comes.";

    /// <summary>The entries the hold policy adds (ADR 0014).</summary>
    public static readonly IReadOnlyList<string> HoldAssumptions =
    [
        "Hold policy: both arms play goal-aware-hold. The pilot never casts a response card whose only rank is \"anything else\".",
        "Hold reserve: while the whole goal holds, the pilot keeps untapped mana. The amount is the cheapest response card in hand that answers removal or a wipe aimed at a goal card. The reserve counts mana value only, and it ends when a disruption card hits.",
        "Hold limits: the counterspell path does not change, because the reserve starts only after the goal holds. An early all_of leaf has no protection. The held mana may lack the answer's colors, because the engine picks the sources that pay.",
    ];

    private static readonly IReadOnlyList<string> HoldInteractiveAssumptions = [.. InteractiveWith(string.Empty), .. HoldAssumptions];

    /// <summary>The assumptions block of a disruption mode and play policy.
    /// The hold policy drops the tap-out sentence of the response-rule entry
    /// and adds its own entries.</summary>
    public static IReadOnlyList<string> AssumptionsFor(DisruptionMode mode, string? playPolicy = null)
    {
        if (mode != DisruptionMode.Interactive)
        {
            return Assumptions;
        }

        return playPolicy == GoalAwareHoldPilot.PolicyName ? HoldInteractiveAssumptions : InteractiveAssumptions;
    }

    public static string NameOf(DisruptionKind kind) => kind switch
    {
        DisruptionKind.Counterspell => CounterspellKind,
        DisruptionKind.TargetedRemoval => TargetedRemovalKind,
        DisruptionKind.BoardWipe => BoardWipeKind,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown disruption kind."),
    };

    public static DisruptionKind KindOf(string name) => name switch
    {
        CounterspellKind => DisruptionKind.Counterspell,
        TargetedRemovalKind => DisruptionKind.TargetedRemoval,
        BoardWipeKind => DisruptionKind.BoardWipe,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, $"Unknown disruption kind; the vocabulary is {string.Join(", ", Vocabulary)}."),
    };

    public bool Equals(DisruptionProfile? other) => other is not null && Mode == other.Mode && Events.SequenceEqual(other.Events);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Mode);
        foreach (var e in Events)
        {
            hash.Add(e);
        }

        return hash.ToHashCode();
    }

    /// <summary>The events in brackets. An interactive profile names its
    /// mode first; an abstracted one keeps the tier-3 text, which the
    /// parity report prints.</summary>
    public override string ToString()
        => (Mode == DisruptionMode.Abstracted ? string.Empty : $"{NameOf(Mode)} ") + $"[{string.Join(", ", Events)}]";
}

/// <summary>
/// One disruption card of the scripted opponent (ADR 0013): its name, its
/// mana value, its card types and its colors. The colors decide which
/// color-specific answers work against it.
/// </summary>
public sealed record DisruptionCard(DisruptionKind Kind, string Name, string Cost, int ManaValue, IReadOnlyList<string> Types, IReadOnlyList<string> Colors)
{
    /// <summary>How the opponent casts the card at instant speed, when not
    /// by its own spell ability: Rout through its "pay {2} more" flash
    /// ability. Null for an instant.</summary>
    public string? CastWith { get; init; }

    /// <summary>Its colors and types in words: "blue instant", "black and green instant".</summary>
    public string Describe()
        => $"{string.Join(" and ", Colors.Select(c => ProtectionGrant.ColorWords[c]))} {string.Join(" ", Types.Select(t => t.ToLowerInvariant()))}";
}

/// <summary>
/// The one fixed, engine-neutral disruption card table (ADR 0013). The
/// owner approved it; no request or format changes it.
/// </summary>
public static class DisruptionCards
{
    public static readonly DisruptionCard Counterspell = new(DisruptionKind.Counterspell, "Counterspell", "{U}{U}", 2, ["Instant"], ["U"]);

    public static readonly DisruptionCard Atomize = new(DisruptionKind.TargetedRemoval, "Atomize", "{2}{B}{G}", 4, ["Instant"], ["B", "G"]);

    public static readonly DisruptionCard Rout = new(DisruptionKind.BoardWipe, "Rout", "{3}{W}{W}", 5, ["Sorcery"], ["W"])
    {
        CastWith = "through its \"pay {2} more\" flash ability",
    };

    /// <summary>The table in kind order.</summary>
    public static readonly IReadOnlyList<DisruptionCard> All = [Counterspell, Atomize, Rout];

    /// <summary>True when a response feature proves an answer to a card of
    /// the table (glossary: Response card): a counter whose reach covers the
    /// card's types and colors, or a one-shot protection grant that answers
    /// the card (<see cref="ProtectionGrant.Answers"/>). The reach ignores
    /// "unless its controller pays" and the pact clause. A grant that answers
    /// no card of the table, such as protection from red or hexproof from
    /// blue, does not count.</summary>
    public static bool AnswersAny(ResponseFeature response)
        => All.Any(c => response.Grants.Any(g => g.Answers(c)) || (response.Counter is { } counter && counter.Reaches(c.Types, c.Colors)));

    public static DisruptionCard For(DisruptionKind kind) => kind switch
    {
        DisruptionKind.Counterspell => Counterspell,
        DisruptionKind.TargetedRemoval => Atomize,
        DisruptionKind.BoardWipe => Rout,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown disruption kind."),
    };
}

/// <summary>One victim a disruption card aimed at, and whether the card
/// hit it (interactive disruption, ADR 0013).</summary>
public sealed record VictimHit(string Card, bool Hit);

/// <summary>One event's fate in one run: the turn it fired and what it hit,
/// or unused when it never fired. Under interactive disruption, fired means
/// the opponent cast the event's disruption card; the outcome then also
/// records each aimed victim with whether the card hit it, and a run whose
/// card was never cast is not cast.</summary>
public sealed class DisruptionOutcome(DisruptionEvent @event)
{
    public DisruptionEvent Event { get; } = @event;

    /// <summary>The turn the event fired on; null while armed or unused.</summary>
    public int? FiredTurn { get; private set; }

    /// <summary>The names of the cards the event hit, in order. Under
    /// interactive disruption, the cards its disruption card aimed at.</summary>
    public IReadOnlyList<string> Victims { get; private set; } = [];

    /// <summary>Interactive only: each aimed victim and whether the card
    /// hit it, in order; null under abstracted disruption.</summary>
    public IReadOnlyList<VictimHit>? Hits { get; private set; }

    /// <summary>Interactive only: the cards our deck answered the
    /// disruption card with, from the service's own respond replies, when
    /// the card was answered. Null when the card hit, when it left the
    /// stack without our response, under abstracted disruption, and while
    /// the event is armed or unused.</summary>
    public IReadOnlyList<string>? AnsweredBy { get; private set; }

    /// <summary>Interactive only: the opponent cast the card, and our
    /// respond reply stopped it. An answered event is a fired event.</summary>
    public bool Answered => AnsweredBy is not null;

    /// <summary>Interactive only: the reason of the last attempt that was
    /// not cast (<c>no_target</c> or <c>cannot_cast</c>); null when every
    /// attempt was cast or none was made.</summary>
    public string? NotCastReason { get; private set; }

    /// <summary>Never fired: the run reports it as unused. Read it after
    /// the run ends.</summary>
    public bool Unused => FiredTurn is null;

    /// <summary>Interactive only: the event never fired, and at least one
    /// attempt to cast its disruption card was not cast.</summary>
    public bool NotCast => FiredTurn is null && NotCastReason is not null;

    internal bool IsArmed(int turn) => FiredTurn is null && Event.Turn <= turn;

    /// <summary>The log line of a fired event: the kind and its victims joined.</summary>
    public string LogLine => $"Disruption: {Event.KindName} fires on {string.Join(", ", Victims)}.";

    internal void Fire(int turn, IReadOnlyList<string> victims)
    {
        FiredTurn = turn;
        Victims = victims;
    }

    /// <summary>Interactive: the opponent cast the disruption card; the
    /// event is spent whether it hit or was answered. A non-null
    /// <paramref name="answeredBy"/> marks it answered.</summary>
    internal void Commit(int turn, IReadOnlyList<VictimHit> hits, IReadOnlyList<string>? answeredBy = null)
    {
        if (FiredTurn is not null)
        {
            throw new InvalidOperationException($"The {Event} event fired already.");
        }

        FiredTurn = turn;
        Victims = [.. hits.Select(h => h.Card)];
        Hits = hits;
        AnsweredBy = answeredBy;
    }

    /// <summary>Interactive: the disruption card was not cast. Nothing is
    /// spent, and the event stays armed for its next fire point.</summary>
    internal void MarkNotCast(string reason)
    {
        if (FiredTurn is not null)
        {
            throw new InvalidOperationException($"The {Event} event fired already.");
        }

        NotCastReason = reason;
    }
}
