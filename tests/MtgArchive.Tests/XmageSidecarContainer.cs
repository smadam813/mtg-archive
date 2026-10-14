using System.Globalization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tests;

/// <summary>
/// The suite's one XMage sidecar container. CI builds its image as
/// <c>mtg-archive-xmage:ci</c>; /verify builds a per-checkout tag and passes
/// it in <c>MTG_XMAGE_IMAGE</c>. It starts on
/// first use and lives until the test run ends; the Testcontainers resource
/// reaper removes it. The XMage tests never skip (spec §6.6): without the
/// image, or with a stale one, they fail and name the build command.
/// </summary>
internal static class XmageSidecarContainer
{
    /// <summary>
    /// The image tag. <c>MTG_XMAGE_IMAGE</c> overrides it, so that parallel
    /// worktrees on one host can each test their own build.
    /// </summary>
    public static readonly string Image =
        Environment.GetEnvironmentVariable("MTG_XMAGE_IMAGE") is { Length: > 0 } image ? image : "mtg-archive-xmage:ci";

    /// <summary>The sidecar's game threads, and so the pool size a test sees.</summary>
    public const int Threads = 4;

    private static readonly Lazy<Task<IContainer>> Container = new(StartAsync);

    /// <summary>Options for a new <see cref="XmageSidecar"/> on the shared container.</summary>
    public static async Task<XmageOptions> OptionsAsync()
    {
        var container = await Container.Value;
        return new XmageOptions(container.Hostname, container.GetMappedPublicPort(7070), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
    }

    private static async Task<IContainer> StartAsync()
    {
        var container = new ContainerBuilder(Image)
            .WithImagePullPolicy(_ => false)
            .WithEnvironment("XMAGE_THREADS", Threads.ToString(CultureInfo.InvariantCulture))
            .WithPortBinding(7070, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("XMAGE listening"))
            .Build();
        try
        {
            await container.StartAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The XMage sidecar image {Image} did not start. Build it first: docker build --target runtime -t {Image} engine/xmage",
                ex);
        }

        await RequireCurrentAsync(container);
        return container;
    }

    /// <summary>
    /// A stale image (an old <c>ci</c> tag, say) still starts, and every
    /// request on it then fails as a 503. The handshake check names the
    /// cause once, at first use, with the rebuild command.
    /// </summary>
    private static async Task RequireCurrentAsync(IContainer container)
    {
        var options = new XmageOptions(container.Hostname, container.GetMappedPublicPort(7070), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
        await using var sidecar = new XmageSidecar(options);
        try
        {
            await sidecar.HelloAsync(CancellationToken.None);
        }
        catch (XmageUnavailableException ex)
        {
            throw new InvalidOperationException(
                $"The XMage sidecar image {Image} is stale: {ex.Message} Rebuild it: docker build --target runtime -t {Image} engine/xmage",
                ex);
        }
    }
}
