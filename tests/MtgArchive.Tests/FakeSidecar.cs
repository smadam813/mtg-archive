using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tests;

/// <summary>
/// An in-process stand-in for the XMage sidecar on 127.0.0.1: it speaks
/// the service's wire protocol, answers hello and load_deck itself, and plays each
/// start_game with the test's game script. The protocol tests run on it
/// without Docker; <see cref="XmageSidecarContainer"/> runs the real image.
/// </summary>
internal sealed class FakeSidecar : IAsyncDisposable
{
    public const string DeckId = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    private readonly Func<FakeGame, Task> _game;

    private readonly CancellationTokenSource _stop = new();

    private readonly List<TcpClient> _clients = [];

    private int _connections;

    public FakeSidecar(Func<FakeGame, Task> game, int threads = 2, IReadOnlyList<string>? unimplemented = null, int protocol = XmageWire.Protocol)
    {
        _game = game;
        Threads = threads;
        Unimplemented = unimplemented ?? [];
        Protocol = protocol;
        _listener.Start();
        _ = AcceptAsync();
    }

    public int Threads { get; }

    public IReadOnlyList<string> Unimplemented { get; }

    public int Protocol { get; }

    /// <summary>The version the next hello reports; a test changes it to act out an image upgrade.</summary>
    public string Version { get; set; } = "xmage-1.4.62V1+0123abcd";

    /// <summary>The connections accepted so far.</summary>
    public int Connections => Volatile.Read(ref _connections);

    /// <summary>Every load_deck and start_game the fake read, in order.</summary>
    public ConcurrentQueue<JsonObject> Received { get; } = new();

    /// <summary>Every reply the service sent to a decide or an event, in order.</summary>
    public ConcurrentQueue<JsonObject> Replies { get; } = new();

    public XmageOptions Options(double readTimeoutSeconds = 5)
        => new("127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(readTimeoutSeconds));

    /// <summary>Closes every open connection, as a sidecar restart does.</summary>
    public void DropConnections()
    {
        lock (_clients)
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }

            _clients.Clear();
        }
    }

    /// <summary>Stops listening and closes every connection, as a sidecar
    /// process that exits on a heap error does.</summary>
    public void Exit()
    {
        _stop.Cancel();
        _listener.Stop();
        DropConnections();
    }

    public ValueTask DisposeAsync()
    {
        Exit();
        return ValueTask.CompletedTask;
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception)
            {
                return;
            }

            Interlocked.Increment(ref _connections);
            lock (_clients)
            {
                _clients.Add(client);
            }

            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            while (true)
            {
                JsonObject message;
                using (var frame = await XmageWire.ReadFrameAsync(stream, _stop.Token))
                {
                    if (frame is null)
                    {
                        return;
                    }

                    message = JsonNode.Parse(frame.RootElement.GetRawText())!.AsObject();
                }

                switch ((string?)message["type"])
                {
                    case "hello":
                        await XmageWire.WriteFrameAsync(stream, new JsonObject
                        {
                            ["type"] = "hello",
                            ["engine"] = "xmage",
                            ["version"] = Version,
                            ["threads"] = Threads,
                            ["protocol"] = Protocol,
                        }, _stop.Token);
                        break;
                    case "load_deck":
                        Received.Enqueue(message);
                        await XmageWire.WriteFrameAsync(stream, new JsonObject
                        {
                            ["type"] = "deck",
                            ["deck_id"] = DeckId,
                            ["unimplemented"] = new JsonArray([.. Unimplemented.Select(u => (JsonNode?)u)]),
                        }, _stop.Token);
                        break;
                    case "start_game":
                        Received.Enqueue(message);
                        await _game(new FakeGame(stream, message, Replies, _stop.Token));
                        break;
                }
            }
        }
        catch (Exception)
        {
            // A closed connection or a stopped fake ends the loop.
        }
        finally
        {
            client.Dispose();
        }
    }
}
