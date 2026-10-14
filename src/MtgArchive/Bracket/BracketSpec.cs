using System.Globalization;
using System.Text.Json;
using MtgArchive.Api;

namespace MtgArchive.Bracket;

/// <summary>
/// One parsed bracket spec version (<c>data/bracket-spec/&lt;date&gt;.json</c>).
/// It holds the three barometers with their definitions and per-bracket
/// thresholds. The file supplies every definition and threshold; code
/// supplies none. The classifier reads this and nothing else. The parser
/// checks the shape the classifier needs and adds every problem to the
/// caller's error list (the banlist-events pattern).
/// </summary>
public sealed record BracketSpec(
    DateOnly Date,
    string Source,
    IReadOnlyList<Barometer> Barometers)
{
    public const string GameChangersCount = "game_changers_count";
    public const string MassLandDenial = "mass_land_denial";
    public const string ChainedExtraTurns = "chained_extra_turns";

    /// <summary>The three barometers the issue scopes, in report order.</summary>
    public static readonly string[] BarometerNames = [GameChangersCount, MassLandDenial, ChainedExtraTurns];

    private static readonly string[] RootKeys = ["date", "source", "notes", "barometers"];
    private static readonly string[] BarometerKeys = ["name", "definition", "source", "thresholds"];

    /// <summary>The brackets the spec defines, ascending.</summary>
    public IReadOnlyList<int> Brackets { get; } = Barometers[0].Thresholds.Keys.Order().ToList();

    public Barometer this[string name] => Barometers.Single(b => b.Name == name);

    /// <summary>Parses a spec document; null when it has errors.</summary>
    public static BracketSpec? Parse(string file, string json, List<string> errors)
    {
        var before = errors.Count;
        using var document = JsonShape.Parse(file, json, errors);
        if (document is null)
        {
            return null;
        }

        var shape = new JsonShape(file, errors);
        var root = document.RootElement;
        if (!shape.IsObject(root, "$"))
        {
            return null;
        }

        shape.OnlyKeys(root, "$", RootKeys, isRoot: true);
        var date = ParseDate(shape, root, "$", "date");
        var source = shape.String(root, "$", "source");
        var barometers = new List<Barometer>();
        if (shape.Array(root, "$", "barometers", out var array, nonEmpty: true))
        {
            var index = 0;
            foreach (var element in array.EnumerateArray())
            {
                var barometer = ParseBarometer(shape, element, $"$.barometers[{index++}]");
                if (barometer is not null)
                {
                    barometers.Add(barometer);
                }
            }
        }

        foreach (var name in BarometerNames)
        {
            if (barometers.Count(b => b.Name == name) != 1)
            {
                shape.Fail("$.barometers", $"exactly one barometer named '{name}' is required");
            }
        }

        var bracketSets = barometers.Select(b => string.Join(",", b.Thresholds.Keys.Order())).Distinct().ToList();
        if (bracketSets.Count > 1)
        {
            shape.Fail("$.barometers", "every barometer must define thresholds for the same brackets");
        }

        return errors.Count == before && date is { } d && source is not null
            ? new BracketSpec(d, source, barometers.OrderBy(b => Array.IndexOf(BarometerNames, b.Name)).ToList())
            : null;
    }

    internal static DateOnly? ParseDate(JsonShape shape, JsonElement obj, string path, string name)
    {
        var text = shape.String(obj, path, name);
        if (text is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        shape.Fail($"{path}.{name}", $"'{text}' is not a yyyy-MM-dd date");
        return null;
    }

    private static Barometer? ParseBarometer(JsonShape shape, JsonElement element, string path)
    {
        if (!shape.IsObject(element, path))
        {
            return null;
        }

        shape.OnlyKeys(element, path, BarometerKeys);
        var name = shape.Enum(element, path, "name", BarometerNames);
        var definition = shape.String(element, path, "definition");
        var source = shape.String(element, path, "source");
        if (name is null || definition is null || source is null
            || !shape.Required(element, path, "thresholds", out var thresholds)
            || !shape.IsObject(thresholds, $"{path}.thresholds"))
        {
            return null;
        }

        var parsed = new Dictionary<int, BracketThreshold>();
        foreach (var property in thresholds.EnumerateObject())
        {
            var thresholdPath = $"{path}.thresholds.{property.Name}";
            if (!int.TryParse(property.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var bracket) || bracket < 1)
            {
                shape.Fail(thresholdPath, "bracket keys are positive integers");
                continue;
            }

            var threshold = ParseThreshold(shape, name, property.Value, thresholdPath);
            if (threshold is not null)
            {
                parsed[bracket] = threshold;
            }
        }

        if (parsed.Count == 0)
        {
            shape.Fail($"{path}.thresholds", "at least one bracket threshold is required");
            return null;
        }

        return new Barometer(name, definition, source, parsed);
    }

    /// <summary>Each barometer has its own threshold keys: <c>max</c> (null
    /// = unlimited) for the count, <c>allowed</c> for mass land denial, and
    /// <c>cards_allowed</c> plus <c>chaining_allowed</c> for extra turns.
    /// Every threshold carries the spec's own <c>text</c>.</summary>
    private static BracketThreshold? ParseThreshold(JsonShape shape, string barometer, JsonElement element, string path)
    {
        if (!shape.IsObject(element, path))
        {
            return null;
        }

        var text = shape.String(element, path, "text");
        switch (barometer)
        {
            case GameChangersCount:
                shape.OnlyKeys(element, path, ["max", "text"]);
                if (!shape.Required(element, path, "max", out var max))
                {
                    return null;
                }

                int? limit = max.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.Number when max.TryGetInt32(out var n) && n >= 0 => n,
                    _ => -1,
                };
                if (limit == -1)
                {
                    shape.Fail($"{path}.max", "must be a non-negative integer or null (unlimited)");
                    return null;
                }

                return text is null ? null : new BracketThreshold(text, MaxCount: limit);
            case MassLandDenial:
                shape.OnlyKeys(element, path, ["allowed", "text"]);
                var allowed = shape.Bool(element, path, "allowed");
                return text is null || allowed is null ? null : new BracketThreshold(text, Allowed: allowed);
            default:
                shape.OnlyKeys(element, path, ["cards_allowed", "chaining_allowed", "text"]);
                var cards = shape.Bool(element, path, "cards_allowed");
                var chaining = shape.Bool(element, path, "chaining_allowed");
                return text is null || cards is null || chaining is null
                    ? null
                    : new BracketThreshold(text, CardsAllowed: cards, ChainingAllowed: chaining);
        }
    }
}

/// <summary>One barometer as the spec defines it, thresholds keyed by bracket.</summary>
public sealed record Barometer(
    string Name,
    string Definition,
    string Source,
    IReadOnlyDictionary<int, BracketThreshold> Thresholds);

/// <summary>One bracket's line for one barometer. The parser sets only
/// the fields the barometer uses. <see cref="Text"/> is the spec's own
/// wording; reasons quote it verbatim.</summary>
public sealed record BracketThreshold(
    string Text,
    int? MaxCount = null,
    bool? Allowed = null,
    bool? CardsAllowed = null,
    bool? ChainingAllowed = null);
