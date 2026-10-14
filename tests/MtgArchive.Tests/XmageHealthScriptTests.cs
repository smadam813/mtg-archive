using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace MtgArchive.Tests;

/// <summary>
/// The compose healthcheck of the sidecar (engine/xmage/health.sh)
/// sends a protocol hello and reads the reply. It runs here against the fake
/// sidecar, which speaks the same frames as the shim.
/// </summary>
public sealed class XmageHealthScriptTests
{
    [Fact]
    public async Task A_sidecar_that_answers_hello_is_healthy()
    {
        await using var sidecar = new FakeSidecar(game => game.OverAsync("horizon", 3));

        var (exit, error) = await RunAsync(sidecar.Options().Port);

        Assert.True(exit == 0, $"health.sh exited {exit}: {error}");
    }

    [Fact]
    public async Task A_sidecar_that_exited_is_unhealthy()
    {
        var sidecar = new FakeSidecar(game => game.OverAsync("horizon", 3));
        var port = sidecar.Options().Port;
        await sidecar.DisposeAsync();

        var (exit, error) = await RunAsync(port);

        Assert.True(exit == 1, $"health.sh exited {exit}: {error}"); // the connect fails
    }

    [Fact]
    public async Task A_listener_that_never_answers_is_unhealthy_after_the_read_timeout()
    {
        // The port is open but nothing reads it, as with a dead accept loop.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var (exit, error) = await RunAsync(port);

        Assert.True(exit == 142, $"health.sh exited {exit}: {error}"); // bash read -t: the 2 s timeout
    }

    /// <summary>On Windows, "bash" is the WSL launcher; the script needs Git's bash.</summary>
    private static string Bash()
    {
        var git = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin", "bash.exe");
        return OperatingSystem.IsWindows() && File.Exists(git) ? git : "bash";
    }

    private static async Task<(int Exit, string Error)> RunAsync(int port)
    {
        var script = Path.Combine(RepoRoot.Find(), "engine", "xmage", "health.sh");
        var start = new ProcessStartInfo(Bash()) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(script);
        start.Environment["XMAGE_PORT"] = port.ToString(CultureInfo.InvariantCulture);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await error);
    }
}
