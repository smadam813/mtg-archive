using System.Text.Json;
using System.Text.Json.Nodes;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tests;

/// <summary>
/// The XMage card-correctness fixtures (spec §6.2) in
/// <c>fixtures/xmage-cards/&lt;slug&gt;.json</c>. They began as the staple
/// checks (retired at cutover). A check runs as a sidecar scenario; every
/// assertion is checked against the scenario result.
/// </summary>
internal static class XmageCardFixtures
{
    public static readonly string Root = Path.Combine(RepoRoot.Find(), "tests", "MtgArchive.Tests", "fixtures", "xmage-cards");

    /// <summary>The choose-step answers that name no card.</summary>
    public static readonly IReadOnlySet<string> StopWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "fail to find", "keep the rest", "shuffle", "do not shuffle",
    };

    public static IReadOnlyList<(string Slug, JsonObject Fixture)> All()
        => [.. Directory.EnumerateFiles(Root, "*.json")
            .Where(p => !p.EndsWith(".schema.json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(p => (Path.GetFileNameWithoutExtension(p), JsonNode.Parse(File.ReadAllText(p))!.AsObject()))];

    /// <summary>The scenario message of one check: the start zones, the
    /// actions as they are, and every name the check uses.</summary>
    public static JsonObject Request(JsonObject check)
    {
        var start = check["start"]!.AsObject();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        JsonArray Names(string zone)
        {
            var list = start[zone]?.AsArray().Select(n => (string)n!).ToList() ?? [];
            names.UnionWith(list);
            return new JsonArray([.. list.Select(n => (JsonNode?)n)]);
        }

        var battlefield = new JsonArray();
        foreach (var entry in start["battlefield"]?.AsArray() ?? [])
        {
            var (card, tapped, named) = entry is JsonValue
                ? ((string)entry!, false, (string?)null)
                : ((string)entry!["name"]!, (bool?)entry["tapped"] ?? false, (string?)entry["named"]);
            names.Add(card);
            if (named is not null)
            {
                names.Add(named);
            }

            battlefield.Add(new JsonObject { ["card"] = card, ["tapped"] = tapped, ["named"] = named });
        }

        var hand = Names("hand");
        var library = Names("library");
        var graveyard = Names("graveyard");
        foreach (var action in check["actions"]!.AsArray())
        {
            if ((string?)action!["type"] == "choose" && !StopWords.Contains((string)action["pick"]!))
            {
                names.Add((string)action["pick"]!);
            }
        }

        return new JsonObject
        {
            ["type"] = "scenario",
            ["commanders"] = new JsonArray(),
            ["cards"] = new JsonArray([.. names.Select(n => (JsonNode?)new JsonObject { ["name"] = n, ["quantity"] = 1 })]),
            ["seed"] = 1,
            ["start"] = new JsonObject { ["hand"] = hand, ["battlefield"] = battlefield, ["library"] = library, ["graveyard"] = graveyard },
            ["actions"] = check["actions"]!.DeepClone(),
        };
    }

    /// <summary>Runs one scenario on the sidecar and returns its result.</summary>
    public static async Task<JsonElement> RunAsync(XmageOptions options, JsonObject request)
    {
        var (connection, _) = await XmageConnection.OpenAsync(options, CancellationToken.None);
        await using (connection)
        {
            await connection.SendAsync(request, CancellationToken.None);
            var reply = await connection.ReceiveAsync(CancellationToken.None);
            Assert.Equal("scenario_result", reply.Type);
            return reply.Json;
        }
    }

    /// <summary>Each assertion of the check the result breaks, as "id: why".</summary>
    public static IReadOnlyList<string> Failures(JsonObject check, JsonElement result)
    {
        if (result.GetProperty("error").ValueKind == JsonValueKind.String)
        {
            return [$"scenario error: {result.GetProperty("error").GetString()}"];
        }

        var view = result.GetProperty("view");
        var failures = new List<string>();
        foreach (var assertion in check["assertions"]!.AsArray().Select(a => a!.AsObject()))
        {
            if (Why(assertion, view, result) is { } why)
            {
                failures.Add($"{(string)assertion["id"]!}: {why}");
            }
        }

        return failures;
    }

    private static string? Why(JsonObject a, JsonElement view, JsonElement result)
    {
        IReadOnlyList<string> Zone(string name) => [.. view.GetProperty(name).EnumerateArray().Select(e => e.GetString()!)];
        IReadOnlyList<string> Cards() => [.. a["cards"]!.AsArray().Select(n => (string)n!)];
        string? Contains(string zone)
        {
            var have = Zone(zone).ToList();
            foreach (var card in Cards())
            {
                if (!have.Remove(card))
                {
                    return $"{zone} [{string.Join(", ", Zone(zone))}] lacks {card}";
                }
            }

            return null;
        }

        switch ((string)a["kind"]!)
        {
            case "pool_at_least":
                var pool = view.GetProperty("pool").GetString()!;
                foreach (var (color, node) in a["mana"]!.AsObject())
                {
                    var count = (pool.Length - pool.Replace("{" + color + "}", string.Empty, StringComparison.Ordinal).Length) / (color.Length + 2);
                    if (count < (int)node!)
                    {
                        return $"pool {pool} has {count} {{{color}}}";
                    }
                }

                return null;
            case "battlefield_has":
                var tapped = (bool?)a["tapped"];
                return view.GetProperty("battlefield").EnumerateArray().Any(p => p.GetProperty("card").GetString() == (string)a["card"]!
                        && (tapped is null || p.GetProperty("tapped").GetBoolean() == tapped))
                    ? null
                    : $"no {(tapped is null ? string.Empty : tapped.Value ? "tapped " : "untapped ")}{(string)a["card"]!} on the battlefield";
            case "hand_contains":
                return Contains("hand");
            case "graveyard_contains":
                return Contains("graveyard");
            case "exile_contains":
                return Contains("exile");
            case "library_count":
                return view.GetProperty("library_count").GetInt32() == (int)a["count"]! ? null : $"library holds {view.GetProperty("library_count").GetInt32()}";
            case "library_top":
                var top = result.GetProperty("library").EnumerateArray().Select(e => e.GetString()!).ToList();
                return top.Take(Cards().Count).SequenceEqual(Cards()) ? null : $"library is [{string.Join(", ", top)}]";
            case "named":
                return result.GetProperty("named").EnumerateArray().Any(n => n.GetProperty("card").GetString() == (string)a["permanent"]!
                        && n.GetProperty("value").GetString() == (string)a["value"]!)
                    ? null
                    : $"no {(string)a["permanent"]!} names {(string)a["value"]!}";
            // These kinds began as the staple checks (retired at cutover): locked asks whether an
            // effect forbids the ability; activatable asks whether it can be activated now (not
            // locked, costs payable, timing met).
            case "ability_locked":
            case "ability_activatable":
                var locked = (string)a["kind"]! == "ability_locked";
                var state = result.GetProperty("abilities").EnumerateArray().FirstOrDefault(s => s.GetProperty("card").GetString() == (string)a["permanent"]!
                    && s.GetProperty("index").GetInt32() == (int)a["ability"]!);
                return state.ValueKind == JsonValueKind.Undefined ? $"{(string)a["permanent"]!} has no ability {(int)a["ability"]!}"
                    : state.GetProperty(locked ? "locked" : "activatable").GetBoolean() ? null
                    : $"{(string)a["permanent"]!} ability {(int)a["ability"]!} is {(locked ? "not locked" : "not activatable")}";
            case "life":
                return view.GetProperty("life").GetInt32() == (int)a["amount"]! ? null : $"life is {view.GetProperty("life").GetInt32()}";
            default:
                return $"unknown assertion kind {(string)a["kind"]!}";
        }
    }
}
