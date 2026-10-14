using System.Text.Json.Nodes;

namespace MtgArchive.Tools.EngineGate;

/// <summary>What the commands change in a /simulate request.</summary>
internal static class Requests
{
    /// <summary>A copy of the request with every goal leaf due at the turn
    /// horizon, so no run settles early and every run plays every turn.</summary>
    public static JsonObject FullHorizon(JsonObject request)
    {
        var copy = request.DeepClone().AsObject();
        SetByTurn(copy["goal"]!.AsObject(), (int?)copy["turn_horizon"] ?? 10);
        return copy;
    }

    /// <summary>The distinct cards of the request's decklist, commanders included.</summary>
    public static int CardCount(JsonObject request)
        => (request["decklist"]?["commanders"]?.AsArray().Count ?? 0) + (request["decklist"]?["entries"]?.AsArray().Count ?? 0);

    private static void SetByTurn(JsonObject goal, int turn)
    {
        if (goal["goals"] is JsonArray members)
        {
            foreach (var member in members)
            {
                SetByTurn(member!.AsObject(), turn);
            }
        }
        else
        {
            goal["by_turn"] = turn;
        }
    }
}
