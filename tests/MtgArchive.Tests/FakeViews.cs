using System.Text.Json.Nodes;

namespace MtgArchive.Tests;

/// <summary>Builders for wire messages. Each call returns new nodes: a
/// JSON node can have one parent only.</summary>
internal static class FakeViews
{
    public static JsonObject View(
        int turn,
        string step,
        string[]? hand = null,
        JsonArray? battlefield = null,
        JsonArray? actions = null,
        JsonArray? resolved = null,
        JsonArray? sources = null,
        JsonObject? finds = null,
        string[]? graveyard = null,
        string[]? commandZone = null) => new()
        {
            ["turn"] = turn,
            ["step"] = step,
            ["life"] = 40,
            ["land_drop_used"] = false,
            ["pool"] = "",
            ["hand"] = Names(hand ?? []),
            ["graveyard"] = Names(graveyard ?? []),
            ["exile"] = new JsonArray(),
            ["command_zone"] = Names(commandZone ?? []),
            ["battlefield"] = battlefield ?? [],
            ["library_count"] = 90,
            ["mana_sources"] = sources ?? [],
            ["hand_mana"] = new JsonArray(),
            ["resolved"] = resolved ?? [],
            ["finds"] = finds ?? [],
            ["actions"] = actions ?? [],
        };

    public static JsonObject Keep(JsonObject view, string[] hand, int taken = 0, int keepSize = 7) => new()
    {
        ["type"] = "decide",
        ["kind"] = "keep",
        ["view"] = view,
        ["keep"] = new JsonObject { ["hand"] = Names(hand), ["mulligans_taken"] = taken, ["keep_size"] = keepSize },
    };

    public static JsonObject Action(JsonObject view) => new() { ["type"] = "decide", ["kind"] = "action", ["view"] = view };

    /// <summary>A bottom or a discard.</summary>
    public static JsonObject Count(string kind, JsonObject view, int count) => new() { ["type"] = "decide", ["kind"] = kind, ["view"] = view, ["count"] = count };

    public static JsonObject Pick(JsonObject view, string purpose, string[]? seen, params (string Id, string Label, string? Card)[] options) => new()
    {
        ["type"] = "decide",
        ["kind"] = "pick",
        ["view"] = view,
        ["pick"] = new JsonObject
        {
            ["purpose"] = purpose,
            ["options"] = new JsonArray([.. options.Select(o => (JsonNode?)new JsonObject { ["id"] = o.Id, ["label"] = o.Label, ["card"] = o.Card })]),
            ["seen"] = seen is null ? null : Names(seen),
        },
    };

    public static JsonObject EndOfTurn(JsonObject view, bool afterDisruption = false)
        => new() { ["type"] = "event", ["kind"] = "end_of_turn", ["view"] = view, ["after_disruption"] = afterDisruption };

    /// <summary>A cast event: our spell on the stack with its costs paid (tier 3).</summary>
    public static JsonObject CastEvent(JsonObject view, string spellId, string card, string face = "front") => new()
    {
        ["type"] = "event",
        ["kind"] = "cast",
        ["view"] = view,
        ["spell"] = new JsonObject { ["id"] = spellId, ["card"] = card, ["face"] = face },
    };

    public static JsonObject Resolved(JsonObject view) => new() { ["type"] = "event", ["kind"] = "resolved", ["view"] = view };

    /// <summary>An opposed event (interactive disruption, ADR 0013). A
    /// <paramref name="reason"/> goes with outcome not_cast only.</summary>
    public static JsonObject Opposed(JsonObject view, string card, string outcome, string? reason = null, JsonArray? victims = null, string[]? lost = null)
    {
        var message = new JsonObject
        {
            ["type"] = "event",
            ["kind"] = "opposed",
            ["view"] = view,
            ["card"] = card,
            ["outcome"] = outcome,
        };
        if (reason is not null)
        {
            message["reason"] = reason;
        }

        message["victims"] = victims ?? [];
        message["lost"] = Names(lost ?? []);
        return message;
    }

    /// <summary>A respond decide (the response window, ADR 0013): the
    /// threat on top of the stack and its victims as (id, card).</summary>
    public static JsonObject Respond(JsonObject view, string threatId, string threat, params (string Id, string Card)[] victims) => new()
    {
        ["type"] = "decide",
        ["kind"] = "respond",
        ["view"] = view,
        ["respond"] = new JsonObject
        {
            ["threat"] = new JsonObject { ["id"] = threatId, ["card"] = threat },
            ["victims"] = new JsonArray([.. victims.Select(v => (JsonNode?)new JsonObject { ["id"] = v.Id, ["card"] = v.Card, ["face"] = "front" })]),
        },
    };

    public static JsonObject Victim(string id, string card, bool hit, string face = "front")
        => new() { ["id"] = id, ["card"] = card, ["face"] = face, ["hit"] = hit };

    public static JsonObject Permanent(string id, string card, string face = "front", bool tapped = false)
        => new() { ["id"] = id, ["card"] = card, ["face"] = face, ["tapped"] = tapped };

    public static JsonObject Source(string id, params string[] options) => new() { ["id"] = id, ["options"] = Names(options) };

    public static JsonObject ResolvedCast(string card, int turn, string face = "front") => new() { ["card"] = card, ["face"] = face, ["turn"] = turn };

    public static JsonObject Land(string id, string card, string face = "front") => new()
    {
        ["id"] = id,
        ["kind"] = "land",
        ["card"] = card,
        ["face"] = face,
        ["zone"] = "hand",
        ["cost"] = "",
        ["permanent_id"] = null,
        ["spends_source"] = false,
    };

    public static JsonObject Cast(string id, string card, string cost) => new()
    {
        ["id"] = id,
        ["kind"] = "cast",
        ["card"] = card,
        ["face"] = "front",
        ["zone"] = "hand",
        ["cost"] = cost,
        ["permanent_id"] = null,
        ["spends_source"] = false,
    };

    /// <summary>A game_over. Pass the final view on a win or a life ending.</summary>
    public static JsonObject GameOver(string reason, int turns, string? error = null, JsonArray? log = null, JsonObject? view = null, string? digest = null) => new()
    {
        ["type"] = "game_over",
        ["turns"] = turns,
        ["reason"] = reason,
        ["digest"] = digest ?? new string('0', 64),
        ["error"] = error,
        ["log"] = log,
        ["view"] = view,
    };

    public static JsonObject Log(int turn, string step, string message) => new() { ["turn"] = turn, ["step"] = step, ["message"] = message };

    private static JsonArray Names(IEnumerable<string> names) => new([.. names.Select(n => (JsonNode?)n)]);
}
