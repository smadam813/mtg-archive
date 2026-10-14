using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// The XMage test decks: real-card decks with frozen card feature profiles,
/// a goal, a mulligan policy and batch options, captured from the golden
/// cases at cutover.
/// </summary>
internal static class TestDecks
{
    public static readonly string Root = Path.Combine(RepoRoot.Find(), "tests", "MtgArchive.Tests", "fixtures", "decks");

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        IgnoreReadOnlyProperties = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static IReadOnlyList<string> Names()
        => Directory.EnumerateFiles(Root, "*.case.json")
            .Select(p => Path.GetFileName(p)[..^".case.json".Length])
            .Order(StringComparer.Ordinal)
            .ToList();

    public static string CasePath(string name) => Path.Combine(Root, name + ".case.json");

    public static TestDeck Load(string name) => LoadFile(CasePath(name));

    /// <summary>Reads and parses one case file by path. A file without
    /// profiles is an error (Phase 4, Review Focus 5).</summary>
    public static TestDeck LoadFile(string path)
    {
        var name = Path.GetFileName(path)[..^".case.json".Length];
        var file = JsonSerializer.Deserialize<TestDeckFile>(File.ReadAllText(path), Json)
            ?? throw new InvalidOperationException($"{name}: the case file is empty.");
        if (file.Profiles is null)
        {
            throw new InvalidOperationException($"{name}: the case file has no profiles.");
        }

        var errors = new List<string>();
        var goal = GoalPredicateParser.Parse(file.Goal.ToJsonString(), errors);
        var disruption = file.Disruption is null ? null : DisruptionProfileParser.Parse(file.Disruption.ToJsonString(), errors);
        if (errors.Count > 0 || goal is null)
        {
            throw new InvalidOperationException($"{name}: {string.Join("; ", errors)}");
        }

        return new TestDeck(file.Name, file.Deck, goal, file.Mulligan,
            new MonteCarloOptions(file.Runs, file.BaseSeed, file.TurnHorizon, goal, disruption),
            new DeckFeatureProfiles(file.Profiles, file.Unprofiled ?? []));
    }

    public static string Serialize(GoldenSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Json).ReplaceLineEndings("\n") + "\n";
}

/// <summary>One test deck file. <see cref="Goal"/> and
/// <see cref="Disruption"/> hold the wire forms that <c>POST /simulate</c>
/// takes. <see cref="Profiles"/> is required; a file without it is an
/// error.</summary>
internal sealed record TestDeckFile(
    string Name,
    IReadOnlyList<DeckCard> Deck,
    JsonObject Goal,
    string Mulligan,
    int Runs,
    long BaseSeed,
    int TurnHorizon,
    JsonArray? Disruption,
    IReadOnlyDictionary<string, CardFeatureProfile>? Profiles,
    IReadOnlyList<string>? Unprofiled);

/// <summary>One test deck, parsed. <see cref="MulliganPolicy"/> is
/// <c>land-window</c> or <c>goal-aware</c>; <see cref="MonteCarloOptions.Disruption"/>
/// is set for a paired deck.</summary>
internal sealed record TestDeck(
    string Name,
    IReadOnlyList<DeckCard> Deck,
    GoalPredicate Goal,
    string MulliganPolicy,
    MonteCarloOptions Options,
    DeckFeatureProfiles Profiles);

/// <summary>The engine-independent part of a batch result: what the
/// envelope shows, and each exemplar's full log as text.</summary>
internal sealed record GoldenSnapshot(
    string Play,
    string Mulligan,
    int TurnHorizon,
    long BaseSeed,
    int Runs,
    int Successes,
    double Lower,
    double Upper,
    IReadOnlyList<double> Curve,
    IReadOnlyList<GoldenExemplar> Exemplars,
    IReadOnlyList<GoldenEvent>? Events,
    GoldenSnapshot? Disrupted = null)
{
    public static GoldenSnapshot Of(MonteCarloResult result) => new(
        result.Policies.Play,
        result.Policies.Mulligan,
        result.TurnHorizon,
        result.BaseSeed,
        result.Runs,
        result.Successes,
        result.Interval.Lower,
        result.Interval.Upper,
        result.Curve,
        [.. result.Exemplars.Select(GoldenExemplar.Of)],
        result.Events?.Select(e => new GoldenEvent(e.Event.Kind.ToString(), e.Event.Turn, e.Runs, e.FiredRuns, e.MeanFiredTurn)).ToList());
}

internal sealed record GoldenExemplar(string Kind, int RunIndex, int Seed, bool Success, int? MetTurn, IReadOnlyList<string> Log, IReadOnlyList<string>? Events)
{
    public static GoldenExemplar Of(Exemplar exemplar) => new(
        exemplar.Kind.ToString(),
        exemplar.RunIndex,
        exemplar.Seed,
        exemplar.Success,
        exemplar.MetTurn,
        [.. exemplar.Log.Select(l => $"T{l.Turn} {l.Step}: {l.Message}")],
        exemplar.Events?.Select(e => $"T{e.Turn} {e.Kind}: {e.Victim}").ToList());
}

internal sealed record GoldenEvent(string Kind, int Turn, int Runs, int FiredRuns, double? MeanFiredTurn);
