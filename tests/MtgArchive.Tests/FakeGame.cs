using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tests;

/// <summary>One game on the fake: the script sends decides and events,
/// then ends with <see cref="OverAsync"/>.</summary>
internal sealed class FakeGame(NetworkStream stream, JsonObject start, ConcurrentQueue<JsonObject> replies, CancellationToken token)
{
    private int _seq;

    public JsonObject Start { get; } = start;

    /// <summary>Sends a decide or an event with the next seq and returns the reply.</summary>
    public async Task<JsonObject> AskAsync(JsonObject message)
    {
        message["seq"] = ++_seq;
        await XmageWire.WriteFrameAsync(stream, message, token);
        using var frame = await XmageWire.ReadFrameAsync(stream, token) ?? throw new EndOfStreamException("The service closed the connection.");
        var reply = JsonNode.Parse(frame.RootElement.GetRawText())!.AsObject();
        replies.Enqueue(reply);
        return reply;
    }

    /// <summary>Sends a message as it is: for a script that breaks the protocol.</summary>
    public Task SendAsync(JsonObject message) => XmageWire.WriteFrameAsync(stream, message, token);

    public Task OverAsync(string reason, int turns, string? error = null, JsonArray? log = null, JsonObject? view = null, string? digest = null)
        => SendAsync(FakeViews.GameOver(reason, turns, error, log, view, digest));

    /// <summary>Waits until the service closes the connection.</summary>
    public async Task WaitClosedAsync()
    {
        try
        {
            while (await XmageWire.ReadFrameAsync(stream, token) is { } frame)
            {
                frame.Dispose();
            }
        }
        catch (Exception)
        {
            // closed
        }
    }
}
