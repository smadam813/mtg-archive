using MtgArchive.Api;

namespace MtgArchive.Bracket;

/// <summary>
/// The heuristic bracket classifier, a pure function. Inputs: the resolved
/// decklist, the barometer sets, one parsed spec version, and the caller's
/// declared assumptions. It reads no legality code path and no legality
/// table. The answer is a bracket <em>floor</em>, the minimum bracket the
/// decklist signals prove, plus the assumed bracket the declarations raise
/// it to. It never returns a single unqualified bracket. Every threshold
/// comes from the spec; nothing here knows what bracket 3 allows.
/// </summary>
public static class BracketClassifier
{
    public const string IntentBracketAssumption = "intent_bracket";
    public const string ChainsExtraTurnsAssumption = "chains_extra_turns";

    public static BracketClassification Classify(
        BracketSpec spec,
        IReadOnlyList<ClassifiedCard> deck,
        BarometerSets sets,
        int targetBracket,
        BracketAssumptions assumptions)
    {
        if (!spec.Brackets.Contains(targetBracket))
        {
            throw new ArgumentOutOfRangeException(nameof(targetBracket), targetBracket,
                $"The spec defines brackets {string.Join(", ", spec.Brackets)}.");
        }

        var reasons = new List<BarometerReason>();
        foreach (var signal in Signals(sets))
        {
            var barometer = spec[signal.Barometer];
            var cards = Members(deck, signal.Set);
            if (cards.Count == 0)
            {
                continue;
            }

            var floor = LowestBracket(spec, barometer, t => signal.Admits(cards.Count, t));
            reasons.Add(Reason(barometer, cards, floor, signal.Note(cards.Count)));
        }

        var floorBracket = reasons.Count == 0 ? spec.Brackets[0] : reasons.Max(r => r.FloorBracket);
        var bracketFloor = new BracketFloor(
            floorBracket,
            targetBracket,
            floorBracket <= targetBracket,
            "The minimum bracket the decklist signals prove. Declared assumptions never change it.");

        var declared = new List<DeclaredAssumption>();
        int? assumed = null;
        if (assumptions.IntentBracket is { } intent)
        {
            if (!spec.Brackets.Contains(intent))
            {
                throw new ArgumentOutOfRangeException(nameof(assumptions), intent,
                    $"The spec defines brackets {string.Join(", ", spec.Brackets)}.");
            }

            assumed = Math.Max(floorBracket, intent);
            declared.Add(new DeclaredAssumption(IntentBracketAssumption, true, intent,
                intent > floorBracket
                    ? $"raises the assumed bracket to {intent}"
                    : $"at or below the floor of {floorBracket}; no effect"));
        }
        else
        {
            declared.Add(new DeclaredAssumption(IntentBracketAssumption, false, null, "not declared; unknown"));
        }

        if (assumptions.ChainsExtraTurns is { } chains)
        {
            var extraTurns = spec[BracketSpec.ChainedExtraTurns];
            var chainingFloor = LowestBracket(spec, extraTurns, t => t.ChainingAllowed == true);
            var before = assumed ?? floorBracket;
            assumed = before;
            var effect = "declared false; no effect";
            if (chains && reasons.Any(r => r.Barometer == BracketSpec.ChainedExtraTurns))
            {
                assumed = Math.Max(before, chainingFloor);
                effect = chainingFloor > before
                    ? $"raises the assumed bracket to {chainingFloor}: {extraTurns.Thresholds[chainingFloor].Text}"
                    : $"at or below the assumed bracket of {before}; no effect";
            }
            else if (chains)
            {
                effect = "declared, but the decklist has no extra-turn card; no effect";
            }

            declared.Add(new DeclaredAssumption(ChainsExtraTurnsAssumption, true, chains, effect));
        }
        else
        {
            declared.Add(new DeclaredAssumption(ChainsExtraTurnsAssumption, false, null, "not declared; unknown"));
        }

        var assumedBracket = assumed is { } a
            ? new AssumedBracket(a, AssumedBracketStatus.Declared, a <= targetBracket,
                "The floor raised by the declared assumptions. Never below the floor.")
            : new AssumedBracket(null, AssumedBracketStatus.Unknown, null,
                "The caller declared no assumption, so the assumed bracket is unknown — never guessed.");

        return new BracketClassification(bracketFloor, assumedBracket, reasons, declared);
    }

    /// <summary>One decklist signal per barometer: its set, the threshold
    /// test for a card count, and the note the reason carries.</summary>
    private sealed record Signal(
        string Barometer,
        IReadOnlySet<Guid> Set,
        Func<int, BracketThreshold, bool> Admits,
        Func<int, string> Note);

    private static Signal[] Signals(BarometerSets sets) =>
    [
        new(BracketSpec.GameChangersCount, sets.GameChangers,
            (count, t) => t.MaxCount is null || count <= t.MaxCount,
            count => $"{count} Game Changer(s) in the decklist; the spec counts them per decklist."),
        new(BracketSpec.MassLandDenial, sets.MassLandDenial,
            (_, t) => t.Allowed == true,
            _ => "Membership is the curated mass-land-denial list, a judgment against the spec's definition."),
        new(BracketSpec.ChainedExtraTurns, sets.ExtraTurns,
            (_, t) => t.CardsAllowed == true,
            _ => "Chaining is intent, not a decklist signal: declare chains_extra_turns to raise the assumed bracket. "
                + "The floor counts the cards only."),
    ];

    /// <summary>The distinct decklist cards on a barometer's set, in decklist order.</summary>
    private static List<ClassifiedCard> Members(IReadOnlyList<ClassifiedCard> deck, IReadOnlySet<Guid> set)
        => deck.Where(c => set.Contains(c.OracleId)).DistinctBy(c => c.OracleId).ToList();

    /// <summary>The lowest bracket whose threshold admits the signal; the
    /// spec's highest bracket when none does.</summary>
    private static int LowestBracket(BracketSpec spec, Barometer barometer, Func<BracketThreshold, bool> admits)
        => spec.Brackets.FirstOrDefault(b => admits(barometer.Thresholds[b]), spec.Brackets[^1]);

    private static BarometerReason Reason(Barometer barometer, List<ClassifiedCard> cards, int floor, string note)
        => new(
            barometer.Name,
            barometer.Definition,
            barometer.Source,
            cards.Count,
            floor,
            barometer.Thresholds[floor].Text,
            [.. cards.Select(c => Citation.OfCard(c.OracleId, c.Name))],
            note);
}

/// <summary>One distinct decklist card the classifier sees.</summary>
public sealed record ClassifiedCard(Guid OracleId, string Name);

/// <summary>The barometer membership sets by <c>oracle_id</c>: the pinned
/// Game Changers snapshot and the two curated lists.</summary>
public sealed record BarometerSets(
    IReadOnlySet<Guid> GameChangers,
    IReadOnlySet<Guid> MassLandDenial,
    IReadOnlySet<Guid> ExtraTurns);

/// <summary>
/// The caller's declared assumptions: the inputs no decklist can supply.
/// Both are optional. The response reports an absent one as unknown and
/// never guesses it. <c>intent_bracket</c> is the bracket the pilot means
/// to play at; the skill maps intent and expected game length to it, and
/// the service never sees phrases. <c>chains_extra_turns</c> declares the
/// intent to chain extra turns, which no decklist proves.
/// </summary>
public sealed record BracketAssumptions
{
    public int? IntentBracket { get; init; }

    public bool? ChainsExtraTurns { get; init; }

    public static readonly BracketAssumptions None = new();
}

/// <summary>The classifier's four parts. The endpoint adds the versions
/// block and the snapshot citations.</summary>
public sealed record BracketClassification(
    BracketFloor BracketFloor,
    AssumedBracket AssumedBracket,
    IReadOnlyList<BarometerReason> Reasons,
    IReadOnlyList<DeclaredAssumption> Assumptions);

/// <summary>The primary value: the minimum bracket provable from decklist
/// signals, against the caller's target.</summary>
public sealed record BracketFloor(int Bracket, int TargetBracket, bool WithinTarget, string Basis);

/// <summary>The floor raised by the declared assumptions. Unknown (null
/// bracket) when the caller declared nothing.</summary>
public sealed record AssumedBracket(int? Bracket, AssumedBracketStatus Status, bool? WithinTarget, string Basis);

public enum AssumedBracketStatus
{
    Declared,
    Unknown,
}

/// <summary>One barometer that fired: the cards that fired it, the count,
/// the bracket floor it proves, and the spec's threshold text at that
/// bracket — never a bare boolean. <c>source</c> is the spec's URL for the
/// definition.</summary>
public sealed record BarometerReason(
    string Barometer,
    string Definition,
    string Source,
    int Count,
    int FloorBracket,
    string Threshold,
    IReadOnlyList<Citation> Cards,
    string Note);

/// <summary>One assumption input, declared or not, and what it did.</summary>
public sealed record DeclaredAssumption(string Name, bool Declared, object? Value, string Effect);
