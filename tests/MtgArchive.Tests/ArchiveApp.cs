using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MtgArchive.Api;
using MtgArchive.Embedding;
using MtgArchive.Ingest;
using MtgArchive.Rules;

namespace MtgArchive.Tests;

/// <summary>
/// In-memory host for HTTP-seam tests. Registers the seam-probe endpoints
/// through the service's <see cref="IEndpointMapper"/> seam, and lets a test
/// swap further services (fake ingest phases, a fixture-backed Scryfall
/// source) via <paramref name="configureServices"/>. Startup migration is
/// off unless a test opts in with a real connection string, and so is
/// embedding-model provisioning; the embedding client talks to a
/// <see cref="FakeOllama"/> (no test may reach Ollama). The ingest work
/// directory is unique per host so parallel tests never share temp files;
/// the curated dir defaults to the empty-but-valid fixture files and the
/// work dir gets an empty banlist events file and
/// empty bracket-spec, game-changers, and popularity directories (the
/// service defaults point at the compose mount), so those phases succeed
/// trivially; and
/// <paramref name="settings"/> overrides any further configuration.
/// </summary>
public sealed class ArchiveApp(
    bool autoMigrate = false,
    string? connectionString = null,
    Action<IServiceCollection>? configureServices = null,
    IReadOnlyDictionary<string, string?>? settings = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // WebApplicationFactory finds the content root through the current
        // directory. A test that changes it breaks every parallel host.
        // This search starts at the test assembly instead.
        builder.UseSolutionRelativeContentRoot(Path.Combine("src", "MtgArchive"));
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:AutoMigrate", autoMigrate.ToString());
        builder.UseSetting("Embedding:PullOnStartup", "false");
        var workDir = Path.Combine(Path.GetTempPath(), "mtg-archive-tests", Guid.NewGuid().ToString("N"));
        builder.UseSetting("Ingest:WorkDir", workDir);
        builder.UseSetting("Ingest:CuratedDir",
            Path.Combine(AppContext.BaseDirectory, "fixtures", "curated"));
        Directory.CreateDirectory(workDir);
        var eventsPath = Path.Combine(workDir, "banlist-events.json");
        File.WriteAllText(eventsPath, "[]");
        builder.UseSetting("Banlist:EventsPath", eventsPath);
        var categoriesPath = Path.Combine(workDir, "banlist-categories.json");
        File.WriteAllText(categoriesPath, "[]");
        builder.UseSetting("Banlist:CategoriesPath", categoriesPath);
        // Empty-but-valid bracket data: no spec revision and no snapshot,
        // so the bracket load succeeds and classification reports nothing ingested.
        // No popularity export either: every format reads the EDHREC proxy.
        foreach (var (setting, name) in new[]
                 {
                     ("Ingest:BracketSpecDir", "bracket-spec"),
                     ("Ingest:GameChangersDir", "game-changers"),
                     ("Ingest:PopularityDir", "popularity"),
                 })
        {
            var dir = Path.Combine(workDir, name);
            Directory.CreateDirectory(dir);
            builder.UseSetting(setting, dir);
        }

        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Archive", connectionString);
        }

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEndpointMapper, SeamProbe.Mapper>();
            // Fixture-backed sources by default: the corpus-state gate probes
            // both at every ingest trigger, and no test may touch the
            // network. Tests replace them via configureServices.
            services.AddSingleton<IScryfallBulkSource>(new FakeBulkSource());
            services.AddSingleton<ICrSource>(new FakeCrSource());
            services.AddHttpClient<OllamaEmbedder>().ConfigurePrimaryHttpMessageHandler(() => new FakeOllama());
            configureServices?.Invoke(services);
        });
    }
}
