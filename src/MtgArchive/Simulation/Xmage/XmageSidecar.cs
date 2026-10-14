using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MtgArchive.Simulation.Xmage;

/// <summary>Where the sidecar listens and how long the service waits for it.</summary>
public sealed record XmageOptions(string Host, int Port, TimeSpan ConnectTimeout, TimeSpan ReadTimeout)
{
    public const string EndpointSetting = "Simulation:Xmage:Endpoint";

    public const string ConnectTimeoutSetting = "Simulation:Xmage:ConnectTimeoutSeconds";

    public const string ReadTimeoutSetting = "Simulation:Xmage:ReadTimeoutSeconds";

    /// <summary>The endpoint is "host:port", by default the compose
    /// service <c>xmage:7070</c>. A connect waits 5 s and a read 60 s
    /// unless the settings say otherwise. A malformed endpoint throws.</summary>
    public static XmageOptions From(IConfiguration configuration)
    {
        var endpoint = configuration[EndpointSetting] ?? "xmage:7070";
        var colon = endpoint.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(endpoint[(colon + 1)..], out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{EndpointSetting} is '{endpoint}'; it must be host:port.");
        }

        return new XmageOptions(
            endpoint[..colon],
            port,
            TimeSpan.FromSeconds(configuration.GetValue<double?>(ConnectTimeoutSetting) ?? 5),
            TimeSpan.FromSeconds(configuration.GetValue<double?>(ReadTimeoutSetting) ?? 60));
    }
}

/// <summary>The sidecar cannot be reached, refused the hello, or changed
/// during a request. The request fails; no run counts it as a game.</summary>
public sealed class XmageUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>One game's connection failed: it closed, went silent past the
/// read timeout, or broke the protocol. The game ends in an engine error.</summary>
public sealed class XmageConnectionException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>One TCP connection to the sidecar. It carries one exchange or
/// one game at a time.</summary>
public sealed class XmageConnection : IAsyncDisposable
{
    private readonly TcpClient _client;

    private readonly NetworkStream _stream;

    private readonly TimeSpan _readTimeout;

    private XmageConnection(TcpClient client, TimeSpan readTimeout)
    {
        _client = client;
        _stream = client.GetStream();
        _readTimeout = readTimeout;
    }

    /// <summary>Connects and says hello. A refused or slow connect, a
    /// failed hello, or another engine or protocol is
    /// <see cref="XmageUnavailableException"/>.</summary>
    public static async Task<(XmageConnection Connection, WireHello Hello)> OpenAsync(XmageOptions options, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connect.CancelAfter(options.ConnectTimeout);
            try
            {
                await client.ConnectAsync(options.Host, options.Port, connect.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                client.Dispose();
                throw new XmageUnavailableException($"No connection to the XMage sidecar at {options.Host}:{options.Port} within {options.ConnectTimeout.TotalSeconds} s.");
            }
            catch (SocketException ex)
            {
                client.Dispose();
                throw new XmageUnavailableException($"No connection to the XMage sidecar at {options.Host}:{options.Port}: {ex.Message}", ex);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        var connection = new XmageConnection(client, options.ReadTimeout);
        try
        {
            await connection.SendAsync(XmageRequests.Hello(), cancellationToken).ConfigureAwait(false);
            var reply = await connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            var hello = reply.Type == "hello"
                ? reply.As<WireHello>()
                : throw new XmageConnectionException($"The sidecar answered hello with a {reply.Type ?? "reply"} message.");
            if (hello.Engine != "xmage" || hello.Protocol != XmageWire.Protocol)
            {
                throw new XmageUnavailableException($"The sidecar speaks {hello.Engine} protocol {hello.Protocol}; the service speaks xmage protocol {XmageWire.Protocol}.");
            }

            return (connection, hello);
        }
        catch (Exception ex) when (ex is XmageConnectionException or JsonException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new XmageUnavailableException($"The XMage sidecar at {options.Host}:{options.Port} failed the hello: {ex.Message}", ex);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task SendAsync(JsonNode message, CancellationToken cancellationToken)
    {
        try
        {
            await XmageWire.WriteFrameAsync(_stream, message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            throw new XmageConnectionException($"Sending to the sidecar failed: {ex.Message}", ex);
        }
    }

    /// <summary>The next message. A closed stream, no message within the
    /// read timeout, or bad framing or JSON is <see cref="XmageConnectionException"/>.
    /// A cancelled request throws <see cref="OperationCanceledException"/>.</summary>
    public async Task<XmageMessage> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        read.CancelAfter(_readTimeout);
        try
        {
            using var document = await XmageWire.ReadFrameAsync(_stream, read.Token).ConfigureAwait(false)
                ?? throw new XmageConnectionException("The sidecar closed the connection.");
            return XmageMessage.Of(document);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new XmageConnectionException($"No message from the sidecar within {_readTimeout.TotalSeconds} s.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ObjectDisposedException)
        {
            throw new XmageConnectionException($"Reading from the sidecar failed: {ex.Message}", ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// The service's side of the sidecar (spec §3.1): one pool of connections,
/// at most one per sidecar game thread, shared by every batch the service
/// runs. The first hello sizes the pool by the threads the sidecar
/// reports. A lease waits for a free slot, so two batches at once share
/// the threads, and a connection plays one game at a time, so games never
/// mix. A new connection whose hello names another version or thread count
/// fails its request and resets the pool: no envelope names a version that
/// did not play all its games.
/// </summary>
public sealed class XmageSidecar(XmageOptions options) : IAsyncDisposable
{
    /// <summary>Idle connections, each tagged with the pool it joined. A
    /// return can race a reset, so a lease closes an idle connection of a
    /// reset pool instead of reusing it.</summary>
    private readonly ConcurrentBag<(Slots Slots, XmageConnection Connection)> _idle = [];

    private readonly SemaphoreSlim _gate = new(1, 1);

    private Slots? _slots;

    public XmageOptions Options { get; } = options;

    /// <summary>The sidecar's hello, from the first connection that answers it.</summary>
    public async Task<WireHello> HelloAsync(CancellationToken cancellationToken)
        => (await SlotsAsync(cancellationToken).ConfigureAwait(false)).Hello;

    /// <summary>A connection for one exchange or one game: an idle one, or a
    /// new one when <paramref name="fresh"/> is set or none is idle. It
    /// waits for a free slot.</summary>
    public async Task<XmageLease> LeaseAsync(bool fresh, CancellationToken cancellationToken)
    {
        var slots = await SlotsAsync(cancellationToken).ConfigureAwait(false);
        await slots.Free.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (!fresh && _idle.TryTake(out var idle))
            {
                if (ReferenceEquals(idle.Slots, slots))
                {
                    return new XmageLease(this, slots, idle.Connection, reused: true);
                }

                await idle.Connection.DisposeAsync().ConfigureAwait(false);
            }

            var (connection, hello) = await XmageConnection.OpenAsync(Options, cancellationToken).ConfigureAwait(false);
            if (hello.Version != slots.Hello.Version || hello.Threads != slots.Hello.Threads)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                await ResetAsync(slots).ConfigureAwait(false);
                throw new XmageUnavailableException(
                    $"The XMage sidecar changed from {slots.Hello.Version} ({slots.Hello.Threads} threads) to {hello.Version} ({hello.Threads} threads) during the request. Send the request again.");
            }

            return new XmageLease(this, slots, connection, reused: false);
        }
        catch
        {
            slots.Free.Release();
            throw;
        }
    }

    /// <summary>
    /// Runs <paramref name="use"/> on a leased connection. Any failure,
    /// a cancellation included, breaks the lease, because a stopped read
    /// can leave the stream inside a frame. A connection failure on a
    /// reused idle connection gets one retry on a new connection when
    /// <paramref name="mayRetry"/> allows it; any other connection failure
    /// goes to <paramref name="failed"/>. No connection at all throws
    /// <see cref="XmageUnavailableException"/>.
    /// </summary>
    internal async Task<T> WithConnectionAsync<T>(
        Func<XmageConnection, CancellationToken, Task<T>> use,
        Func<bool> mayRetry,
        Func<XmageConnectionException, T> failed,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var lease = await LeaseAsync(fresh: attempt > 0, cancellationToken).ConfigureAwait(false);
            try
            {
                return await use(lease.Connection, cancellationToken).ConfigureAwait(false);
            }
            catch (XmageConnectionException ex)
            {
                lease.Break();
                if (attempt > 0 || !lease.Reused || !mayRetry())
                {
                    return failed(ex);
                }
            }
            catch
            {
                lease.Break();
                throw;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        while (_idle.TryTake(out var idle))
        {
            await idle.Connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Takes a lease's connection back, or closes it when it broke
    /// or belongs to a pool that was reset; frees the slot either way.</summary>
    internal async ValueTask ReturnAsync(Slots slots, XmageConnection connection, bool broken)
    {
        if (broken || !ReferenceEquals(Volatile.Read(ref _slots), slots))
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _idle.Add((slots, connection));
        }

        slots.Free.Release();
    }

    private async Task<Slots> SlotsAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _slots) is { } known)
        {
            return known;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _slots) is { } again)
            {
                return again;
            }

            var (connection, hello) = await XmageConnection.OpenAsync(Options, cancellationToken).ConfigureAwait(false);
            var slots = new Slots(hello, new SemaphoreSlim(hello.Threads, hello.Threads));
            _idle.Add((slots, connection));
            Volatile.Write(ref _slots, slots);
            return slots;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets a sidecar that changed: the next hello sizes a new pool.</summary>
    private async Task ResetAsync(Slots stale)
    {
        if (ReferenceEquals(Interlocked.CompareExchange(ref _slots, null, stale), stale))
        {
            while (_idle.TryTake(out var idle))
            {
                await idle.Connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>One sidecar's hello and the free game slots it allows.</summary>
    internal sealed record Slots(WireHello Hello, SemaphoreSlim Free);
}

/// <summary>One connection held for one exchange or one game. Disposing
/// the lease returns the connection to the pool, or closes it after
/// <see cref="Break"/>.</summary>
public sealed class XmageLease : IAsyncDisposable
{
    private readonly XmageSidecar _sidecar;

    private readonly XmageSidecar.Slots _slots;

    private bool _broken;

    private int _returned;

    internal XmageLease(XmageSidecar sidecar, XmageSidecar.Slots slots, XmageConnection connection, bool reused)
    {
        _sidecar = sidecar;
        _slots = slots;
        Connection = connection;
        Reused = reused;
    }

    public XmageConnection Connection { get; }

    /// <summary>The connection sat idle in the pool, so it may have died
    /// with a sidecar restart since its last use.</summary>
    public bool Reused { get; }

    /// <summary>Closes the connection on dispose: an exchange on it failed
    /// or stopped part way.</summary>
    public void Break() => _broken = true;

    public ValueTask DisposeAsync()
        => Interlocked.Exchange(ref _returned, 1) == 0 ? _sidecar.ReturnAsync(_slots, Connection, _broken) : ValueTask.CompletedTask;
}
