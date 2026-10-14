using System.Text.RegularExpressions;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;
using MtgArchive.Tools.EngineGate.Parity;

namespace MtgArchive.Tools.EngineGate.Diagnosis;

/// <summary>How often a land entered tapped and untapped on XMage (the ETB switch).</summary>
internal sealed record EtbCount(int Tapped, int Untapped);

/// <summary>Why a failing run missed the goal, read from its board at the goal turn.</summary>
internal enum FailureClass
{
    /// <summary>Less mana on the board (lands and mana rocks) than the goal card's mana value.</summary>
    LandShort,

    /// <summary>Enough mana, but no source for each colored pip: one land pays one pip.</summary>
    ColorShort,

    /// <summary>Enough mana and a source for each pip by the rows, so a land rule or the payment kept the cast out.</summary>
    RulesShort,

    /// <summary>No end-of-turn line for the goal turn.</summary>
    Unknown,
}

/// <summary>A permanent that makes mana with no mana cost: the colors of one
/// tap, and how much mana one tap adds (a land 1, Sol Ring 2).</summary>
internal sealed record BoardSource(IReadOnlyList<string> Colors, int Amount);

/// <summary>One land's share of the failures: runs with it on the board by the goal turn.</summary>
internal sealed record LandLift(string Land, int Runs, int Failures, double FailureRate);

/// <summary>How many successful runs took one path to the goal.</summary>
internal sealed record PathCount(string Path, int Runs);

/// <summary>This trace against another label's trace (--compare).</summary>
internal sealed record TraceComparison(string Label, int Successes, int Runs, Difference Difference);

/// <summary><c>eval/parity/diagnosis/&lt;label&gt;.json</c>.</summary>
internal sealed record TraceSummary(
    string Label,
    string Engine,
    string Case,
    string Mulligan,
    string DeckSha,
    string? EngineVersion,
    IReadOnlyList<string> Ablations,
    int Runs,
    int Successes,
    int EngineErrors,
    double Floor,
    double Lower,
    double Upper,
    IReadOnlyDictionary<string, EtbCount> Etb,
    IReadOnlyDictionary<string, int> PayLife,
    IReadOnlyDictionary<FailureClass, int> Classes,
    IReadOnlyList<LandLift> Lift,
    TraceComparison? Against,
    IReadOnlyList<PathCount>? Paths = null,
    int CannotCast = 0)
{
    /// <summary>Each failing run has one class: engine errors have none.</summary>
    public IReadOnlyDictionary<FailureClass, int> Classes { get; } = Classes is null
        ? throw new ArgumentException($"{Label}: the summary has no classes.", nameof(Classes))
        : Classes.Values.Sum() == Runs - Successes - EngineErrors
        ? Classes
        : throw new ArgumentException($"{Label}: the classes count {Classes.Values.Sum()} runs, but {Runs - Successes - EngineErrors} runs failed without an engine error.", nameof(Classes));
}

/// <summary>The parsers and statistics of the trace command. Pure functions: the unit tests pin them.</summary>
internal static partial class TraceAnalysis
{
    public const string EndPrefix = "END life=";

    /// <summary>The XMAGE_SWITCHES entry that turns the ETB lines on.</summary>
    public const string EtbSwitch = "mtgarchive.diag.etb=true";

    /// <summary>The XMAGE_SWITCHES entry that answers yes to "pay 2 life".</summary>
    public const string PayLifeSwitch = "mtgarchive.diag.payLife=true";

    [GeneratedRegex(@"^PILOT (?<kind>land|cast|cast_commander|activate) (?<card>.+)$")]
    private static partial Regex PilotAction();

    /// <summary>The run's actions by the end of <paramref name="turn"/>, as
    /// "T1 land > T1 cast Sol Ring": a land drop has no name, a commander
    /// cast is a cast, and a failed try is no step.</summary>
    public static string Path(IEnumerable<GameLogEntry> log, int turn)
    {
        var steps = new List<string>();
        foreach (var line in log.Where(l => l.Turn <= turn))
        {
            var step = XmageStep(line.Message);
            if (step is not null)
            {
                steps.Add($"T{line.Turn} {step}");
            }
        }

        return string.Join(" > ", steps);
    }

    private static string? XmageStep(string message)
    {
        var match = PilotAction().Match(message);
        if (!match.Success)
        {
            return null;
        }

        var kind = match.Groups["kind"].Value;
        return kind == "land" ? "land" : $"{(kind == "cast_commander" ? "cast" : kind)} {match.Groups["card"].Value}";
    }

    /// <summary>The <paramref name="top"/> most common paths, most runs first, then by path.</summary>
    public static IReadOnlyList<PathCount> Paths(IEnumerable<string> paths, int top)
        => [.. paths.GroupBy(p => p, StringComparer.Ordinal)
            .Select(g => new PathCount(g.Key, g.Count()))
            .OrderByDescending(p => p.Runs)
            .ThenBy(p => p.Path, StringComparer.Ordinal)
            .Take(top)];

    /// <summary>The failed cast tries on XMage: "PILOT cannot cast" and "PILOT cannot cast_commander" lines.</summary>
    public static int CannotCast(IEnumerable<GameLogEntry> log)
        => log.Count(l => l.Message.StartsWith("PILOT cannot cast", StringComparison.Ordinal));

    [GeneratedRegex(@"^ETB (?<card>.+) (?<state>tapped|untapped)$")]
    private static partial Regex EtbLine();

    [GeneratedRegex(@" bf=\[(?<bf>.*)\] gy=\[(?<gy>.*)\]$")]
    private static partial Regex EndZones();

    [GeneratedRegex(@"^DIAG pay-life (?<card>.+)$")]
    private static partial Regex PayLifeLine();

    public static IReadOnlyDictionary<string, EtbCount> Etb(IEnumerable<GameLogEntry> log)
    {
        var counts = new SortedDictionary<string, EtbCount>(StringComparer.Ordinal);
        foreach (var line in log)
        {
            var match = EtbLine().Match(line.Message);
            if (!match.Success)
            {
                continue;
            }

            var card = match.Groups["card"].Value;
            var now = counts.GetValueOrDefault(card, new EtbCount(0, 0));
            counts[card] = match.Groups["state"].Value == "tapped" ? now with { Tapped = now.Tapped + 1 } : now with { Untapped = now.Untapped + 1 };
        }

        return counts;
    }

    /// <summary>How often each card paid life on XMage (the pay-life switch).</summary>
    public static IReadOnlyDictionary<string, int> PayLife(IEnumerable<GameLogEntry> log)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in log)
        {
            var match = PayLifeLine().Match(line.Message);
            if (match.Success)
            {
                var card = match.Groups["card"].Value;
                counts[card] = counts.GetValueOrDefault(card) + 1;
            }
        }

        return counts;
    }

    /// <summary>The error when <paramref name="switches"/> turn the ETB lines
    /// on but no run logged one: the sidecar image lacks the switch. Null
    /// when the switch is off or the lines came.</summary>
    public static string? SwitchesMissing(string? switches, IReadOnlyDictionary<string, EtbCount> etb, IReadOnlyDictionary<string, int> payLife)
        => switches?.Contains(EtbSwitch, StringComparison.Ordinal) == true && etb.Count == 0
            ? $"--switches sets {EtbSwitch}, but no run logged an ETB line: the image lacks the switch. Rebuild the image; no summary was written."
            : switches?.Contains(PayLifeSwitch, StringComparison.Ordinal) == true && payLife.Count == 0
                ? $"--switches sets {PayLifeSwitch}, but no run logged a pay-life line: the image lacks the switch, or the deck has no land that asks. No summary was written."
                : null;

    /// <summary>The lands of an END line's battlefield (with their tapped
    /// state) and graveyard, matched against known land names, longest name
    /// first, so a comma in a name does not split it (Phase 3D, Review Focus 2).</summary>
    public static (IReadOnlyList<(string Land, bool Tapped)> Battlefield, IReadOnlyList<string> Graveyard) EndLands(string message, IReadOnlyCollection<string> landNames)
    {
        var zones = EndZones().Match(message);
        if (!zones.Success)
        {
            return ([], []);
        }

        var names = landNames.OrderByDescending(n => n.Length).ToList();
        return (
            [.. Items(zones.Groups["bf"].Value, names)],
            [.. Items(zones.Groups["gy"].Value, names).Select(i => i.Land)]);
    }

    /// <summary>The land items of one zone list ("A(T), B, C"). A land name
    /// matches only when a ", ", a "(T)" or the list's end follows it. A
    /// non-land item is skipped up to the next ", "; a non-land name with a
    /// comma leaves a fragment, which matches no land and is skipped the
    /// same way. <paramref name="names"/> is longest first.</summary>
    private static IEnumerable<(string Land, bool Tapped)> Items(string list, IReadOnlyList<string> names)
    {
        var at = 0;
        while (at < list.Length)
        {
            var name = names.FirstOrDefault(n => string.CompareOrdinal(list, at, n, 0, n.Length) == 0
                && (at + n.Length == list.Length || list[at + n.Length] is ',' or '('));
            var end = name is null ? NextItem(list, at) : at + name.Length;
            var tapped = end + 3 <= list.Length && string.CompareOrdinal(list, end, "(T)", 0, 3) == 0;
            if (tapped)
            {
                end += 3;
            }

            if (name is not null)
            {
                yield return (name, tapped);
            }

            at = end + (end < list.Length ? 2 : 0); // skip ", "
        }
    }

    private static int NextItem(string list, int at)
    {
        var comma = list.IndexOf(", ", at, StringComparison.Ordinal);
        return comma < 0 ? list.Length : comma;
    }

    /// <summary>The mana sources of a deck, by card name: each land, with its
    /// colors (both faces of a land MDFC), and each other permanent whose
    /// profile has tap mana: the colors of every tap option and the most
    /// mana one tap adds. A land counts 1: its row lists colors, not an
    /// amount, so a land that adds two mana (Ancient Tomb) counts short.</summary>
    public static IReadOnlyDictionary<string, BoardSource> Sources(IEnumerable<DeckCard> deck, DeckFeatureProfiles profiles)
    {
        var sources = new Dictionary<string, BoardSource>(StringComparer.Ordinal);
        foreach (var card in deck)
        {
            if (card.HasLandFace)
            {
                sources[card.Name] = new BoardSource([.. card.AnyFaceColors.Distinct(StringComparer.Ordinal)], 1);
                continue;
            }

            if (card.IsPermanent && profiles.Of(card) is { TapMana.Count: > 0 } profile)
            {
                var taps = profile.TapMana.Select(Symbols).ToList();
                sources[card.Name] = new BoardSource([.. taps.SelectMany(s => s).Distinct(StringComparer.Ordinal)], taps.Max(s => s.Count));
            }
        }

        return sources;
    }

    /// <summary>The class of one failure from its board at the goal turn.
    /// It ignores tapped state on purpose: a land tapped at end of turn may
    /// have paid for another spell. Each unit of mana pays one pip, so a
    /// dual land pays one of its colors, not both.</summary>
    public static FailureClass Classify(IEnumerable<string> board, IReadOnlyDictionary<string, BoardSource> sources, IReadOnlyList<string> pips, int manaValue)
    {
        List<IReadOnlyList<string>> units = [.. board.Where(sources.ContainsKey).SelectMany(n => Enumerable.Repeat(sources[n].Colors, sources[n].Amount))];
        if (units.Count < manaValue)
        {
            return FailureClass.LandShort;
        }

        return Covers(pips, 0, units, new bool[units.Count]) ? FailureClass.RulesShort : FailureClass.ColorShort;
    }

    /// <summary>A matching of pips to distinct units, by backtracking: a
    /// cost has at most a handful of pips.</summary>
    private static bool Covers(IReadOnlyList<string> pips, int pip, IReadOnlyList<IReadOnlyList<string>> units, bool[] used)
    {
        if (pip == pips.Count)
        {
            return true;
        }

        for (var u = 0; u < units.Count; u++)
        {
            if (!used[u] && units[u].Contains(pips[pip], StringComparer.Ordinal))
            {
                used[u] = true;
                if (Covers(pips, pip + 1, units, used))
                {
                    return true;
                }

                used[u] = false;
            }
        }

        return false;
    }

    /// <summary>The colored pips of a mana cost: each single W, U, B, R or G symbol, once per symbol.</summary>
    public static IReadOnlyList<string> Pips(string? manaCost)
        => [.. Symbols(manaCost).Where(s => s is "W" or "U" or "B" or "R" or "G")];

    /// <summary>The symbols of a mana string, in order. The separators go in an
    /// array: <c>Split('{', '}', options)</c> binds to
    /// <c>Split(char, int count, options)</c> and splits on '{' alone.</summary>
    private static List<string> Symbols(string? mana)
        => [.. (mana ?? string.Empty).Split(['{', '}'], StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>The lands a run had by the end of <paramref name="turn"/>, by
    /// card name: the battlefield and graveyard of that turn's last END
    /// line. <paramref name="lands"/> maps each name a log may show (a card,
    /// or the land face of an MDFC) to its card name.</summary>
    public static IReadOnlySet<string> LandsByTurn(IReadOnlyList<GameLogEntry> log, int turn, IReadOnlyDictionary<string, string> lands)
    {
        var end = log.LastOrDefault(l => l.Turn == turn && l.Message.StartsWith(EndPrefix, StringComparison.Ordinal));
        if (end is null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var (bf, gy) = EndLands(end.Message, [.. lands.Keys]);
        return bf.Select(b => lands[b.Land]).Concat(gy.Select(g => lands[g])).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Each name a log may show for a land of the deck, mapped to its
    /// card name: the card, and the land back face of an MDFC.</summary>
    public static IReadOnlyDictionary<string, string> LandNames(IEnumerable<DeckCard> deck)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var card in deck.Where(c => c.HasLandFace))
        {
            names[card.Name] = card.Name;
            foreach (var face in card.LandFaces())
            {
                names[face.Name] = card.Name;
            }
        }

        return names;
    }

    /// <summary>Each land's failure rate among the runs that had it by the
    /// goal turn, highest first; lands in fewer than 100 runs are left out.</summary>
    public static IReadOnlyList<LandLift> Lift(IReadOnlyList<(bool Success, IReadOnlySet<string> Lands)> runs)
        => [.. runs.SelectMany(r => r.Lands.Select(l => (Land: l, r.Success)))
            .GroupBy(x => x.Land, StringComparer.Ordinal)
            .Select(g => new LandLift(g.Key, g.Count(), g.Count(x => !x.Success), (double)g.Count(x => !x.Success) / g.Count()))
            .Where(l => l.Runs >= 100)
            .OrderByDescending(l => l.FailureRate)
            .ThenBy(l => l.Land, StringComparer.Ordinal)];
}
