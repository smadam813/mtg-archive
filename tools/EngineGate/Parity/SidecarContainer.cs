using System.Globalization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tools.EngineGate.Parity;

/// <summary>
/// One sidecar process for a gate: a running sidecar that
/// <see cref="ParityOptions.Endpoints"/> names, or a new container from
/// <see cref="ParityOptions.Image"/> with a published port. The container
/// goes when the gate ends.
/// </summary>
internal sealed class SidecarContainer : IAsyncDisposable
{
    /// <summary>XMAGE_SWITCHES that turn the three game-copies changes of patch 0002 off (spec §6.4).</summary>
    public const string CopiesOff = "-Dmtgarchive.copies.oneCopy=false -Dmtgarchive.copies.shareLibraryCards=false -Dmtgarchive.copies.noPriorityBookmark=false";

    private readonly IContainer? _container;

    private SidecarContainer(IContainer? container, XmageOptions options)
    {
        _container = container;
        Options = options;
    }

    public XmageOptions Options { get; }

    /// <param name="index">0 for the first process, 1 for the second.</param>
    /// <param name="switches">XMAGE_SWITCHES for a new container; a named endpoint must already run with them.</param>
    public static async Task<SidecarContainer> OpenAsync(ParityOptions options, int index, string? switches, TextWriter stdout)
    {
        if (index < options.Endpoints.Count)
        {
            return new SidecarContainer(null, ParityOptions.EndpointOptions(options.Endpoints[index]));
        }

        stdout.WriteLine($"starting sidecar {index + 1} from {options.Image} with {options.Threads} game threads{(switches is null ? string.Empty : ", game-copies changes off")}");
        var builder = new ContainerBuilder(options.Image)
            .WithImagePullPolicy(_ => false)
            .WithEnvironment("XMAGE_THREADS", options.Threads.ToString(CultureInfo.InvariantCulture))
            .WithPortBinding(7070, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("XMAGE listening"));
        if (switches is not null)
        {
            builder = builder.WithEnvironment("XMAGE_SWITCHES", switches);
        }

        var container = builder.Build();
        try
        {
            await container.StartAsync();
            return new SidecarContainer(container, new XmageOptions(container.Hostname, container.GetMappedPublicPort(7070), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60)));
        }
        catch
        {
            // A container that never listened must not outlive the gate.
            await container.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
