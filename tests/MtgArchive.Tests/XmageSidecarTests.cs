using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tests;

/// <summary>
/// The service's pool of sidecar connections (spec §3.1): one hello sizes
/// it, a lease waits for a free slot, a broken connection closes, and an
/// unreachable or changed sidecar fails fast with
/// <see cref="XmageUnavailableException"/>.
/// </summary>
public sealed class XmageSidecarTests
{
    private static Task Idle(FakeGame game) => game.OverAsync("horizon", 1);

    [Fact]
    public async Task The_first_hello_sizes_the_pool_and_is_asked_once()
    {
        await using var fake = new FakeSidecar(Idle, threads: 3);
        await using var sidecar = new XmageSidecar(fake.Options());

        var first = await sidecar.HelloAsync(CancellationToken.None);
        var second = await sidecar.HelloAsync(CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(3, first.Threads);
        Assert.Equal(fake.Version, first.Version);
        Assert.Equal(1, fake.Connections);
    }

    [Fact]
    public async Task A_lease_waits_for_a_free_slot_and_reuses_the_returned_connection()
    {
        await using var fake = new FakeSidecar(Idle, threads: 2);
        await using var sidecar = new XmageSidecar(fake.Options());
        var a = await sidecar.LeaseAsync(fresh: false, CancellationToken.None);
        var b = await sidecar.LeaseAsync(fresh: false, CancellationToken.None);

        var third = sidecar.LeaseAsync(fresh: false, CancellationToken.None);
        await Task.Delay(200);
        Assert.False(third.IsCompleted);

        await a.DisposeAsync();
        var c = await third.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(c.Reused);
        Assert.Same(a.Connection, c.Connection);
        Assert.Equal(2, fake.Connections);
        await b.DisposeAsync();
        await c.DisposeAsync();
    }

    [Fact]
    public async Task Leases_at_once_never_share_a_connection_or_pass_the_thread_count()
    {
        await using var fake = new FakeSidecar(Idle, threads: 3);
        await using var sidecar = new XmageSidecar(fake.Options());
        var held = new HashSet<XmageConnection>();
        var peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(async () =>
        {
            await using var lease = await sidecar.LeaseAsync(fresh: false, CancellationToken.None);
            lock (held)
            {
                Assert.True(held.Add(lease.Connection), "Two leases hold one connection.");
                peak = Math.Max(peak, held.Count);
            }

            await Task.Delay(20);
            lock (held)
            {
                held.Remove(lease.Connection);
            }
        })));

        Assert.InRange(peak, 1, 3);
        Assert.InRange(fake.Connections, 1, 3);
    }

    [Fact]
    public async Task A_broken_lease_closes_its_connection()
    {
        await using var fake = new FakeSidecar(Idle, threads: 2);
        await using var sidecar = new XmageSidecar(fake.Options());
        var a = await sidecar.LeaseAsync(fresh: false, CancellationToken.None);
        a.Break();
        await a.DisposeAsync();

        var b = await sidecar.LeaseAsync(fresh: false, CancellationToken.None);

        Assert.False(b.Reused);
        Assert.NotSame(a.Connection, b.Connection);
        Assert.Equal(2, fake.Connections);
        await b.DisposeAsync();
    }

    [Fact]
    public async Task No_sidecar_is_unavailable_within_the_connect_timeout()
    {
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        await using var sidecar = new XmageSidecar(new XmageOptions("127.0.0.1", port, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)));

        var clock = Stopwatch.StartNew();
        await Assert.ThrowsAsync<XmageUnavailableException>(() => sidecar.HelloAsync(CancellationToken.None));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), clock.Elapsed.ToString());
    }

    [Fact]
    public async Task A_sidecar_in_another_protocol_is_unavailable()
    {
        await using var fake = new FakeSidecar(Idle, protocol: 1);
        await using var sidecar = new XmageSidecar(fake.Options());

        var error = await Assert.ThrowsAsync<XmageUnavailableException>(() => sidecar.HelloAsync(CancellationToken.None));

        Assert.Contains("xmage protocol 1", error.Message);
        Assert.Contains("xmage protocol 2", error.Message);
    }

    [Fact]
    public async Task A_sidecar_that_changes_version_fails_the_lease_and_the_next_hello_takes_the_new_version()
    {
        await using var fake = new FakeSidecar(Idle, threads: 2);
        await using var sidecar = new XmageSidecar(fake.Options());
        var old = await sidecar.HelloAsync(CancellationToken.None);
        fake.Version = "xmage-1.4.62V1+ffffffff";

        var error = await Assert.ThrowsAsync<XmageUnavailableException>(() => sidecar.LeaseAsync(fresh: true, CancellationToken.None));

        Assert.Contains(old.Version, error.Message);
        Assert.Equal("xmage-1.4.62V1+ffffffff", (await sidecar.HelloAsync(CancellationToken.None)).Version);
    }

    [Theory]
    [InlineData(null, "xmage", 7070)]
    [InlineData("127.0.0.1:7171", "127.0.0.1", 7171)]
    public void The_endpoint_setting_is_host_and_port(string? endpoint, string host, int port)
    {
        var options = XmageOptions.From(new ConfigurationBuilder()
            .AddInMemoryCollection([new(XmageOptions.EndpointSetting, endpoint)])
            .Build());

        Assert.Equal(host, options.Host);
        Assert.Equal(port, options.Port);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), options.ReadTimeout);
    }

    [Fact]
    public void An_endpoint_without_a_port_fails()
        => Assert.Throws<InvalidOperationException>(() => XmageOptions.From(new ConfigurationBuilder()
            .AddInMemoryCollection([new(XmageOptions.EndpointSetting, "xmage")])
            .Build()));
}
