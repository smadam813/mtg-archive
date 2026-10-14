using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MtgArchive.Api;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;
using EngineDisruptionMode = MtgArchive.Simulation.Engine.DisruptionMode;

namespace MtgArchive.Tools.EngineGate.Parity;

/// <summary>The JSON of the gate files: snake_case names, enums as names,
/// dictionary keys (card names) as they are.</summary>
internal static class ParityJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        IgnoreReadOnlyProperties = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>The options of <see cref="Read{T}"/>: an unmapped member is an error.</summary>
    private static readonly JsonSerializerOptions Strict = new(Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    /// <summary>Reads a gate file. With <paramref name="strict"/>, a member the
    /// record does not map stops the gate, so a misspelt member
    /// never drops out in silence. The cases and the baseline read strict; a
    /// frozen deck file keeps old members such as <c>definitions</c>.</summary>
    public static T Read<T>(string path, bool strict = false)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), strict ? Strict : Options) ?? throw new InvalidDataException($"{path} is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }
    }

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options).ReplaceLineEndings("\n") + "\n");
    }
}

/// <summary>
/// <c>eval/parity/cases.json</c>: the parity decks and their sources, the
/// run count, the base and fresh seeds, the horizon, the mulligan policies,
/// and the cases. Paths are relative to the cases file.
/// </summary>
internal sealed record ParityCases(
    IReadOnlyList<DeckSource> Decks,
    int Runs,
    long BaseSeed,
    long FreshSeed,
    int TurnHorizon,
    IReadOnlyList<string> Mulligans,
    IReadOnlyList<ParityCase> Cases)
{
    /// <summary>Reads the cases. An unknown format, a case on a
    /// determinism-only deck, or a deck format with no deck goal stops the
    /// gate here, before any sidecar starts.</summary>
    public static ParityCases Load(string path)
    {
        var cases = ParityJson.Read<ParityCases>(path, strict: true);
        foreach (var c in cases.Cases)
        {
            c.Rules();
            c.Mode();
        }

        foreach (var deck in cases.Decks)
        {
            if (deck.Goal is null && deck.Format is not null)
            {
                throw new InvalidDataException($"{deck.Name}: a deck format needs a deck goal; a parity deck's format is on each case.");
            }

            if (deck.DeterminismOnly && cases.Cases.Any(c => c.Deck == deck.Name))
            {
                throw new InvalidDataException($"{deck.Name}: a deck with its own goal is determinism-only; no case may name it.");
            }

            deck.Rules();
        }

        return cases;
    }
}

/// <summary>
/// A gate deck: its name, and the decklist the capture reads. A deck with
/// its own <see cref="Goal"/> is determinism-only: it has no
/// parity case, and only the determinism and leak gates play it, under that
/// goal and <see cref="Format"/> (null is Commander).
/// </summary>
internal sealed record DeckSource(string Name, string Source, JsonObject? Goal = null, string? Format = null, string? Exercises = null)
{
    public bool DeterminismOnly => Goal is not null;

    /// <summary>The game rules of the deck's format.</summary>
    public GameRules Rules() => ParityCase.GameRulesOf(Name, Format);

    public GoalPredicate ParseGoal()
    {
        var errors = new List<string>();
        return GoalPredicateParser.Parse(Goal?.ToJsonString() ?? "null", errors) ?? throw new InvalidDataException($"{Name}: {string.Join("; ", errors)}");
    }
}

/// <summary>One goal on one parity deck, and what it exercises. A case
/// with a disruption profile runs paired (tier 3). <see cref="Format"/> is
/// the request format whose game rules the case plays under; null is
/// Commander, the same default as a request.
/// <see cref="DisruptionMode"/> is the disrupted arm's mode; null is
/// abstracted, the same default as a request.
/// <see cref="PlayPolicy"/> is the play policy of both arms; null is
/// goal-aware-greedy, the same default as a request (ADR 0014).</summary>
internal sealed record ParityCase(string Name, string Deck, JsonObject Goal, JsonArray? Disruption, string Exercises, string? Format = null, string? DisruptionMode = null, string? PlayPolicy = null)
{
    /// <summary>The case's play policy name, with null read as the default.
    /// An unknown name stops the gate, and so does the hold policy outside
    /// an interactive case.</summary>
    public string PlayPolicyName()
    {
        var name = PolicyOf(Name, PlayPolicy);
        if (PlayPolicies.NeedsInteractive(name) && Mode() != EngineDisruptionMode.Interactive)
        {
            throw new InvalidDataException($"{Name}: the play policy {name} needs an interactive disruption mode.");
        }

        return name;
    }

    /// <summary>A play policy wire name with null read as the default; an unknown name stops the gate.</summary>
    internal static string PolicyOf(string owner, string? policy)
        => policy is null ? GoalAwarePilot.PolicyName
            : PlayPolicies.Names.Contains(policy) ? policy
            : throw new InvalidDataException($"{owner}: unknown play policy '{policy}'; the policies are {string.Join(", ", PlayPolicies.Names)}.");

    /// <summary>The case's disruption mode, with null read as abstracted. An
    /// unknown mode, or a mode with no profile, stops the gate.</summary>
    public EngineDisruptionMode Mode()
    {
        if (DisruptionMode is not null && Disruption is null)
        {
            throw new InvalidDataException($"{Name}: a disruption mode needs a disruption profile.");
        }

        return ModeOf(Name, DisruptionMode);
    }

    /// <summary>A mode wire name (<see cref="DisruptionProfile.Modes"/>)
    /// with null read as abstracted; an unknown name stops the gate.</summary>
    internal static EngineDisruptionMode ModeOf(string owner, string? mode)
        => mode is null ? EngineDisruptionMode.Abstracted
            : DisruptionProfile.Modes.Contains(mode) ? DisruptionProfile.ModeOf(mode)
            : throw new InvalidDataException($"{owner}: unknown disruption mode '{mode}'; the modes are {string.Join(", ", DisruptionProfile.Modes)}.");

    /// <summary>The game rules of the case's format.</summary>
    public GameRules Rules() => GameRulesOf(Name, Format);

    /// <summary>The game rules of a format wire name; null is Commander. An unknown name stops the gate.</summary>
    internal static GameRules GameRulesOf(string owner, string? format)
    {
        try
        {
            return GameRules.For(FormatParameter.Resolve(format));
        }
        catch (ProblemException)
        {
            throw new InvalidDataException($"{owner}: unknown format '{format}'; the formats are {string.Join(", ", FormatParameter.Materialized.Select(FormatParameter.WireName))}.");
        }
    }

    public GoalPredicate ParseGoal()
    {
        var errors = new List<string>();
        return GoalPredicateParser.Parse(Goal.ToJsonString(), errors) ?? throw new InvalidDataException($"{Name}: {string.Join("; ", errors)}");
    }

    public DisruptionProfile? ParseDisruption()
    {
        if (Disruption is null)
        {
            return null;
        }

        var errors = new List<string>();
        return DisruptionProfileParser.Parse(Disruption.ToJsonString(), errors) ?? throw new InvalidDataException($"{Name}: {string.Join("; ", errors)}");
    }
}

/// <summary>One card the capture swapped for the filler of its mana value.</summary>
internal sealed record Substitution(string Card, int ManaValue, string Filler);

/// <summary>
/// One frozen parity deck (Decision 1): the decklist after the filler swap,
/// the deck rows and the card feature profiles the XMage adapter reads.
/// Every gate command reads this file, never the archive. The files are
/// frozen at cutover; <c>definitions</c> in an old file is ignored.
/// </summary>
internal sealed record ParityDeck(
    string Name,
    string Source,
    IReadOnlyList<Substitution> Substitutions,
    Decklist Decklist,
    IReadOnlyList<DeckCard> Deck,
    IReadOnlyDictionary<string, CardFeatureProfile> Profiles,
    IReadOnlyList<string> Unprofiled)
{
    public static ParityDeck Load(string path) => ParityJson.Read<ParityDeck>(path);

    public void Save(string path) => ParityJson.Write(path, this);

    /// <summary>The lowercase hex SHA-256 of the file: the baseline's stale check.</summary>
    public static string Sha(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    public DeckFeatureProfiles FeatureProfiles => new(Profiles, Unprofiled);
}

/// <summary><c>eval/parity/baseline.json</c>: the baseline engine's result
/// of every comparison: the serving XMage after cutover (spec §6.1).</summary>
internal sealed record ParityBaseline(string Engine, string Version, string Captured, IReadOnlyList<BaselineEntry> Comparisons)
{
    public static ParityBaseline Load(string path) => ParityJson.Read<ParityBaseline>(path, strict: true);

    public void Save(string path) => ParityJson.Write(path, this);

    /// <summary>The header version of these entries: their one build, or
    /// every build, "mixed:", when an --only rerun left more than one.</summary>
    public static string VersionOf(IEnumerable<BaselineEntry> entries)
    {
        var versions = entries.Select(e => e.EngineVersion ?? "unknown").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return versions.Count == 1 ? versions[0] : $"mixed: {string.Join(", ", versions)}";
    }

    /// <summary>
    /// The entry of a comparison; null with the reason when it is missing or
    /// stale. An entry is stale when its deck file or anything it played
    /// under changed in the current cases: goal, disruption, format (as
    /// game rules), mulligan policy, seeds or horizon.
    /// </summary>
    public (BaselineEntry? Entry, string? Problem) Find(string name, string deckSha, int runs, ParityCases cases, ParityCase @case, string mulligan)
    {
        var entry = Comparisons.FirstOrDefault(c => c.Name == name);
        if (entry is null)
        {
            return (null, $"no baseline for {name}: run parity-baseline");
        }

        if (entry.Runs != runs)
        {
            return (null, $"the baseline of {name} has {entry.Runs} runs, not {runs}: run parity-baseline with the same runs");
        }

        string? changed =
            entry.DeckSha != deckSha ? "its deck file"
            : !JsonNode.DeepEquals(entry.Goal, @case.Goal) ? "the goal"
            : !JsonNode.DeepEquals(entry.Disruption, @case.Disruption) ? "the disruption"
            : ParityCase.ModeOf(name, entry.DisruptionMode) != @case.Mode() ? "the disruption mode"
            : ParityCase.PolicyOf(name, entry.PlayPolicy) != @case.PlayPolicyName() ? "the play policy"
            : ParityCase.GameRulesOf(name, entry.Format) != @case.Rules() ? "the format"
            : entry.Mulligan != mulligan ? "the mulligan policy"
            : entry.BaseSeed != cases.BaseSeed ? "the base seed"
            : entry.FreshSeed != cases.FreshSeed ? "the fresh seed"
            : entry.TurnHorizon != cases.TurnHorizon ? "the turn horizon"
            : null;
        return changed is null
            ? (entry, null)
            : (null, $"the baseline of {name} is stale: {changed} changed; run parity-baseline again");
    }
}

/// <summary>The baseline engine's result of one comparison at the base seed and at
/// the fresh seed. <see cref="EngineVersion"/> is the build that played it;
/// a baseline written before it existed leaves it null. <see cref="Format"/>
/// is the case's format; null is Commander. <see cref="DisruptionMode"/> is
/// the case's mode as the case wrote it; null is abstracted.
/// <see cref="PlayPolicy"/> is the case's play policy as the case wrote it;
/// null is goal-aware-greedy.</summary>
internal sealed record BaselineEntry(
    string Name,
    string Case,
    string Deck,
    string DeckSha,
    JsonObject Goal,
    string Mulligan,
    JsonArray? Disruption,
    string? Arm,
    int Runs,
    long BaseSeed,
    int Successes,
    IReadOnlyList<double> Curve,
    long FreshSeed,
    int FreshSuccesses,
    IReadOnlyList<double> FreshCurve,
    int TurnHorizon,
    string? EngineVersion = null,
    string? Format = null,
    string? DisruptionMode = null,
    string? PlayPolicy = null);

/// <summary>
/// One entry of <c>eval/parity/accepted.json</c>: a comparison that fails
/// on both seeds for a cause a diagnosis names, which the user accepted
/// (phase 3E). A defect on the candidate side is fixed, not accepted.
/// </summary>
internal sealed record AcceptedDifference(string Comparison, string Direction, string Cause, string Evidence)
{
    /// <summary>The candidate's floor is below the baseline's.</summary>
    public const string Lower = "lower";

    /// <summary>The candidate's floor is above the baseline's.</summary>
    public const string Higher = "higher";

    /// <summary>The whole interval of <paramref name="difference"/> (candidate minus baseline) lies on this entry's side of 0.</summary>
    public bool Matches(Difference difference) => Direction switch
    {
        Lower => difference.Upper < 0,
        Higher => difference.Lower > 0,
        _ => false,
    };
}

/// <summary><c>eval/parity/accepted.json</c>. A missing file, or one with no
/// <c>accepted</c> list, accepts nothing.</summary>
internal sealed record AcceptedDifferences(IReadOnlyList<AcceptedDifference> Accepted)
{
    public static AcceptedDifferences Load(string path) =>
        File.Exists(path) ? new(JsonSerializer.Deserialize<AcceptedDifferences>(File.ReadAllText(path), ParityJson.Options)?.Accepted?.OfType<AcceptedDifference>().ToList() ?? []) : new([]);

    public AcceptedDifference? For(string comparison) => Accepted.FirstOrDefault(a => a.Comparison == comparison);

    /// <summary>The entries that stop the gate before any game: a comparison
    /// the cases lack, a comparison named twice, a direction that is neither
    /// value, an empty cause, and evidence that is no file under
    /// <paramref name="root"/> (the directory of the accepted file).</summary>
    public IReadOnlyList<string> Problems(IReadOnlyCollection<string> comparisons, string root)
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in Accepted)
        {
            if (!comparisons.Contains(a.Comparison))
            {
                problems.Add($"accepted.json: {a.Comparison}: no such comparison in the cases.");
            }

            if (!seen.Add(a.Comparison))
            {
                problems.Add($"accepted.json: {a.Comparison}: named twice.");
            }

            if (a.Direction is not (AcceptedDifference.Lower or AcceptedDifference.Higher))
            {
                problems.Add($"accepted.json: {a.Comparison}: direction is {AcceptedDifference.Lower} or {AcceptedDifference.Higher}, not '{a.Direction}'.");
            }

            if (string.IsNullOrWhiteSpace(a.Cause))
            {
                problems.Add($"accepted.json: {a.Comparison}: the cause is empty.");
            }

            if (string.IsNullOrWhiteSpace(a.Evidence) || !File.Exists(Path.Combine(root, a.Evidence)))
            {
                problems.Add($"accepted.json: {a.Comparison}: no evidence file at {Path.Combine(root, a.Evidence ?? string.Empty)}.");
            }
        }

        return problems;
    }
}
