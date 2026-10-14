using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Data;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The ingest run framework end to end: trigger/poll API,
/// single-flight, per-phase progress, the six-field report, upsert, stale
/// marking, and temp-artifact lifecycle. Each test gets its own migrated
/// database and a fixture-backed Scryfall source.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IngestApiTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Ingest_lifecycle_upserts_diffs_and_marks_stale()
    {
        var relic = Guid.NewGuid();
        var bear = Guid.NewGuid();
        var idol = Guid.NewGuid();
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();

        // Run 1: two new cards, one with a ruling and a tag.
        source.OracleCards = Lines(
            CardLine(relic, "Ancient Relic", oracleText: "{T}: Add {C}."),
            CardLine(bear, "Old Bear", typeLine: "Creature — Bear", manaCost: "{1}{G}", cmc: 2));
        source.Rulings = Lines(RulingLine(relic, "It taps for colorless."));
        source.OracleTags = Lines(TagLine("ramp", relic));
        var run1 = await TriggerAsync(client);
        var report = (await WaitForCompletionAsync(client, run1, "succeeded")).GetProperty("report");

        Assert.Equal(2, report.GetProperty("new_cards").GetInt32());
        Assert.Equal(0, report.GetProperty("changed_cards").GetInt32());
        Assert.Empty(report.GetProperty("stale_cards").EnumerateArray());
        Assert.Empty(report.GetProperty("drift_detections").EnumerateArray());
        Assert.Empty(report.GetProperty("errors").EnumerateArray());
        Assert.Equal("2026-08-07", report.GetProperty("cr_version_found").GetString());
        var collectionRows = report.GetProperty("collection_rows_pointing_at_stale_cards");
        Assert.True(collectionRows.GetProperty("populated").GetBoolean());
        Assert.Empty(collectionRows.GetProperty("rows").EnumerateArray());

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var card = await db.Cards.Include(c => c.Rulings).Include(c => c.OracleTags)
                .SingleAsync(c => c.OracleId == relic);
            Assert.Equal("It taps for colorless.", Assert.Single(card.Rulings).Comment);
            Assert.Equal("ramp", Assert.Single(card.OracleTags).Tag);
            Assert.Equal("normal", card.Layout);
            Assert.False(card.Stale);

            // A row from before the layout column: the next run fills it.
            card.Layout = null;
            await db.SaveChangesAsync();
        }

        Assert.All(source.Downloaded, path => Assert.False(File.Exists(path)));

        // Run 2, identical snapshot: nothing new, nothing changed. Forced
        // past the no-op gate so the upsert diff itself runs. The layout
        // fill does not count as a change.
        var run2 = await TriggerAsync(client, force: true);
        report = (await WaitForCompletionAsync(client, run2, "succeeded")).GetProperty("report");
        Assert.Equal(0, report.GetProperty("new_cards").GetInt32());
        Assert.Equal(0, report.GetProperty("changed_cards").GetInt32());
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal("normal", (await db.Cards.SingleAsync(c => c.OracleId == relic)).Layout);
        }

        // Run 3: the relic's text changes, the bear disappears, an idol is new.
        source.OracleCards = Lines(
            CardLine(relic, "Ancient Relic", oracleText: "{T}: Add {C}{C}."),
            CardLine(idol, "Gleaming Idol"));
        var run3 = await TriggerAsync(client);
        report = (await WaitForCompletionAsync(client, run3, "succeeded")).GetProperty("report");
        Assert.Equal(1, report.GetProperty("new_cards").GetInt32());
        Assert.Equal(1, report.GetProperty("changed_cards").GetInt32());
        var stale = Assert.Single(report.GetProperty("stale_cards").EnumerateArray());
        Assert.Equal(bear, stale.GetProperty("oracle_id").GetGuid());
        Assert.Equal("Old Bear", stale.GetProperty("name").GetString());

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.True((await db.Cards.SingleAsync(c => c.OracleId == bear)).Stale);
            var card = await db.Cards.SingleAsync(c => c.OracleId == relic);
            Assert.Equal("{T}: Add {C}{C}.", card.OracleText);
            Assert.Contains("{C}{C}", card.Raw.RootElement.GetProperty("oracle_text").GetString());
        }

        // Run 4: the bear returns — unmarked stale, counted as changed.
        source.OracleCards = Lines(
            CardLine(relic, "Ancient Relic", oracleText: "{T}: Add {C}{C}."),
            CardLine(idol, "Gleaming Idol"),
            CardLine(bear, "Old Bear", typeLine: "Creature — Bear", manaCost: "{1}{G}", cmc: 2));
        var run4 = await TriggerAsync(client);
        report = (await WaitForCompletionAsync(client, run4, "succeeded")).GetProperty("report");
        Assert.Equal(0, report.GetProperty("new_cards").GetInt32());
        Assert.Equal(1, report.GetProperty("changed_cards").GetInt32());
        Assert.Empty(report.GetProperty("stale_cards").EnumerateArray());

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.False((await db.Cards.SingleAsync(c => c.OracleId == bear)).Stale);
        }

        var latest = await GetRunAsync(client, "/ingest/runs/latest");
        Assert.Equal(run4, latest.GetProperty("run_id").GetInt32());
    }

    [Fact]
    public async Task Run_reports_phase_progress_in_canonical_order()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = Lines(CardLine(Guid.NewGuid(), "Lone Cube"));

        var runId = await TriggerAsync(client);
        var run = await WaitForCompletionAsync(client, runId, "succeeded");

        var phases = run.GetProperty("phases").EnumerateArray().ToList();
        Assert.Equal(
            new[] { "fetch_validate", "card_upsert", "source_profile_derive", "card_feature_derive", "cr_refresh", "banlist_replay", "legality_rebuild", "popularity_load", "bracket_load", "re_embed" },
            phases.Select(p => p.GetProperty("phase").GetString()).ToArray());
        Assert.Equal(
            Enumerable.Repeat("succeeded", 10).ToArray(),
            phases.Select(p => p.GetProperty("status").GetString()).ToArray());
        Assert.NotEqual(JsonValueKind.Undefined, phases[0].GetProperty("started_at").ValueKind);
        Assert.False(run.GetProperty("force").GetBoolean());
    }

    [Fact]
    public async Task Failed_validation_reports_every_error_and_writes_no_cards()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = Lines(
            CardLine(Guid.NewGuid(), "Valid Card"),
            """{"name":"No Oracle Id","type_line":"Artifact","cmc":1,"released_at":"2020-01-01","color_identity":[]}""",
            "not json at all");

        var runId = await TriggerAsync(client);
        var run = await WaitForCompletionAsync(client, runId, "failed");

        var errors = run.GetProperty("report").GetProperty("errors").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e!.Contains("line 2") && e.Contains("oracle_id"));
        Assert.Contains(errors, e => e!.Contains("line 3") && e.Contains("invalid JSON"));

        var phases = run.GetProperty("phases").EnumerateArray().ToList();
        Assert.Equal("failed", phases[0].GetProperty("status").GetString());
        Assert.NotNull(phases[0].GetProperty("error").GetString());
        Assert.Equal("skipped", phases[1].GetProperty("status").GetString());

        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(0, await db.Cards.CountAsync());

        // A failed run leaves the downloaded files for the rerun.
        Assert.Contains(source.Downloaded, File.Exists);
    }

    [Fact]
    public async Task Second_trigger_during_a_run_returns_the_running_runs_id()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source, services =>
        {
            services.RemoveAll<IIngestPhase>();
            services.AddSingleton<IIngestPhase>(new BlockingPhase(gate));
        });
        using var client = app.CreateClient();

        var first = await TriggerAsync(client, force: true);
        var second = await TriggerAsync(client);
        Assert.Equal(first, second);

        var running = await GetRunAsync(client, $"/ingest/runs/{first}");
        Assert.Equal("running", running.GetProperty("status").GetString());
        Assert.True(running.GetProperty("force").GetBoolean());
        Assert.False(running.TryGetProperty("report", out _));

        gate.SetResult();
        await WaitForCompletionAsync(client, first, "succeeded");

        // Single flight releases: the next trigger is a fresh run.
        var third = await TriggerAsync(client);
        Assert.NotEqual(first, third);
        await WaitForCompletionAsync(client, third, "succeeded");
    }

    [Fact]
    public async Task Trigger_after_the_run_row_goes_final_starts_a_new_run()
    {
        // The row reads final before the runner's task finishes.
        // A trigger in that window must not return the finished run's id.
        var hold = new FinalStatusHold();
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source, services =>
        {
            services.RemoveAll<IIngestPhase>();
            services.ConfigureDbContext<ArchiveDbContext>(options => options.AddInterceptors(hold));
        });
        using var client = app.CreateClient();

        var first = await TriggerAsync(client, force: true);
        await hold.Landed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var landed = await GetRunAsync(client, $"/ingest/runs/{first}");
        Assert.Equal("succeeded", landed.GetProperty("status").GetString());

        // The trigger arrives while the row reads final and the runner is
        // not yet idle. It must wait for the idle transition, not return
        // the finished run's id.
        var second = TriggerAsync(client);
        await Task.Delay(250);
        Assert.False(second.IsCompleted, "the trigger answered before the runner went idle");
        hold.Release.SetResult();
        var previous = await second;
        Assert.True(previous > first, $"run {previous} is not newer than run {first}");
        await WaitForCompletionAsync(client, previous, "succeeded");

        for (var cycle = 0; cycle < 10; cycle++)
        {
            var next = await TriggerAsync(client);
            Assert.True(next > previous, $"run {next} is not newer than run {previous}");
            await WaitForCompletionAsync(client, next, "succeeded");
            previous = next;
        }
    }

    [Fact]
    public async Task Unknown_runs_are_problem_404s()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();

        foreach (var path in new[] { "/ingest/runs/latest", "/ingest/runs/12345" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("urn:mtg-archive:problem:unknown-ingest-run",
                problem.RootElement.GetProperty("type").GetString());
        }
    }

    private async Task<(string ConnectionString, FakeBulkSource Source)> SetUpAsync()
        => (await postgres.CreateDatabaseAsync($"ingest_{Guid.NewGuid():N}"), new FakeBulkSource());

    private static ArchiveApp NewApp(
        string connectionString, FakeBulkSource source, Action<IServiceCollection>? configureServices = null)
        => new(autoMigrate: true, connectionString, services =>
        {
            services.RemoveAll<IScryfallBulkSource>();
            services.AddSingleton<IScryfallBulkSource>(source);
            services.RemoveAll<ICrSource>();
            services.AddSingleton<ICrSource>(new FakeCrSource());
            configureServices?.Invoke(services);
        });

    /// <summary>Holds the fetch slot open until the test releases it.</summary>
    private sealed class BlockingPhase(TaskCompletionSource gate) : IIngestPhase
    {
        public string Name => IngestPhases.FetchValidate;

        public Task RunAsync(IngestContext context, CancellationToken cancellationToken)
            => gate.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Holds the runner inside its final write, after the row
    /// commits, until the test releases it. Fires once.</summary>
    private sealed class FinalStatusHold : SaveChangesInterceptor
    {
        public TaskCompletionSource Landed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var final = eventData.Context!.ChangeTracker.Entries<IngestRun>()
                .Any(e => e.Entity.Status != IngestRunStatus.Running);
            if (final && Landed.TrySetResult())
            {
                await Release.Task;
            }

            return result;
        }
    }
}
