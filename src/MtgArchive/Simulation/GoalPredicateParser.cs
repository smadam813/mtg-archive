using System.Text.Json;
using MtgArchive.Api;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Simulation;

/// <summary>
/// Request-side parsing of a goal predicate from its wire form, one object
/// per predicate: <c>{ "kind": "on_battlefield", "card": "...", "by_turn": n }</c>,
/// <c>{ "kind": "cast", "card": "...", "by_turn": n }</c>, or
/// <c>{ "kind": "all_of", "goals": [ ... ] }</c>. The parser reports every
/// problem with its path. An unknown kind is an error that names the
/// closed vocabulary.
/// </summary>
public static class GoalPredicateParser
{
    /// <summary>The prefix of every error, in the place of a file name.</summary>
    private const string Source = "goal";

    private static readonly string[] LeafKeys = ["kind", "card", "by_turn"];

    private static readonly string[] AllOfKeys = ["kind", "goals"];

    public static GoalPredicate? Parse(string json, List<string> errors)
    {
        using var document = JsonShape.Parse(Source, json, errors);
        return document is null ? null : Parse(document.RootElement, errors);
    }

    public static GoalPredicate? Parse(JsonElement element, List<string> errors)
        => Parse(new JsonShape(Source, errors), element, "$");

    private static GoalPredicate? Parse(JsonShape shape, JsonElement element, string path)
    {
        if (!shape.IsObject(element, path))
        {
            return null;
        }

        return shape.Enum(element, path, "kind", GoalPredicate.Vocabulary) switch
        {
            null => null,
            GoalPredicate.AllOfKind => ParseAllOf(shape, element, path),
            var kind => ParseLeaf(shape, element, path, kind),
        };
    }

    private static GoalPredicate? ParseLeaf(JsonShape shape, JsonElement element, string path, string kind)
    {
        var before = shape.ErrorCount;
        shape.OnlyKeys(element, path, LeafKeys);
        var card = shape.String(element, path, "card");
        var byTurn = shape.Int(element, path, "by_turn", min: 1);
        if (shape.ErrorCount > before)
        {
            return null;
        }

        return kind == GoalPredicate.OnBattlefieldKind
            ? new GoalPredicate.OnBattlefield(card!, byTurn!.Value)
            : new GoalPredicate.Cast(card!, byTurn!.Value);
    }

    private static GoalPredicate? ParseAllOf(JsonShape shape, JsonElement element, string path)
    {
        var before = shape.ErrorCount;
        shape.OnlyKeys(element, path, AllOfKeys);
        if (!shape.Array(element, path, "goals", out var goals, nonEmpty: true))
        {
            return null;
        }

        var members = goals.EnumerateArray()
            .Select((member, index) => Parse(shape, member, $"{path}.goals[{index}]"))
            .ToList();
        return shape.ErrorCount > before ? null : new GoalPredicate.AllOf(members.Select(m => m!).ToList());
    }
}
