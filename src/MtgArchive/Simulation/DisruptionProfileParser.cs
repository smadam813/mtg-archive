using System.Text.Json;
using MtgArchive.Api;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Simulation;

/// <summary>
/// Request-side parsing of a disruption profile from its wire form, an
/// array of <c>{ "kind": ..., "turn": n }</c>. The parser reports every
/// problem with its path. An unknown kind is an error that names the
/// closed vocabulary. An event after the horizon is valid: it stays unused.
/// </summary>
public static class DisruptionProfileParser
{
    private const string Source = "disruption_profile";

    private static readonly string[] EventKeys = ["kind", "turn"];

    public static DisruptionProfile? Parse(string json, List<string> errors)
    {
        using var document = JsonShape.Parse(Source, json, errors);
        return document is null ? null : Parse(document.RootElement, errors);
    }

    public static DisruptionProfile? Parse(JsonElement element, List<string> errors)
    {
        var shape = new JsonShape(Source, errors);
        if (element.ValueKind != JsonValueKind.Array)
        {
            shape.Fail("$", "must be an array of { kind, turn } events");
            return null;
        }

        var before = shape.ErrorCount;
        var events = element.EnumerateArray()
            .Select((member, index) => ParseEvent(shape, member, $"$[{index}]"))
            .ToList();
        return shape.ErrorCount > before ? null : new DisruptionProfile(events.Select(e => e!).ToList());
    }

    private static DisruptionEvent? ParseEvent(JsonShape shape, JsonElement element, string path)
    {
        if (!shape.IsObject(element, path))
        {
            return null;
        }

        var before = shape.ErrorCount;
        shape.OnlyKeys(element, path, EventKeys);
        var kind = shape.Enum(element, path, "kind", DisruptionProfile.Vocabulary);
        var turn = shape.Int(element, path, "turn", min: 1);
        return shape.ErrorCount > before ? null : new DisruptionEvent(DisruptionProfile.KindOf(kind!), turn!.Value);
    }
}
