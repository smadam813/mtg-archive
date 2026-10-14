using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Ingest;
using MtgArchive.Rules;

namespace MtgArchive.Tests;

/// <summary>
/// The CR refresh phase through the ingest surface: full text
/// archived in <c>cr_document</c> keyed by the header's effective date, the
/// report's "CR version found" recorded every run, and a CR change doing a
/// full chunk replace plus the recorded re-embed signal. Each test gets its
/// own migrated database; only the CR phase is registered, so the other
/// canonical slots skip.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CrRefreshTests(PostgresFixture postgres)
{
    [Fact]
    public async Task First_ingest_archives_the_version_chunks_it_and_records_the_signal()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();

        var report = await RunIngestAsync(client);

        Assert.Equal("2026-08-07", report.GetProperty("cr_version_found").GetString());

        await using var db = PostgresFixture.CreateContext(connectionString);
        var document = await db.CrDocuments.SingleAsync();
        Assert.Equal(new DateOnly(2026, 8, 7), document.EffectiveDate);
        // Full text archived in Postgres, normalized (no BOM, LF endings).
        Assert.StartsWith("Magic: The Gathering Comprehensive Rules", document.FullText);
        Assert.Contains("704.5aa", document.FullText);
        Assert.DoesNotContain('\r', document.FullText);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.FullText))),
            document.FullTextSha256);
        Assert.Equal(source.SourceUrl, document.SourceUrl);

        Assert.Equal(
            ["100.1", "100.2", "100.3", "101.1", "119.1", "704.5"],
            await db.CrRuleChunks.OrderBy(c => c.Number).Select(c => c.Number).ToArrayAsync());
        Assert.Equal(
            ["Abandon", "Ability", "Active Player"],
            await db.CrGlossaryChunks.OrderBy(g => g.Term).Select(g => g.Term).ToArrayAsync());

        var signal = await db.RulesReembedSignals.SingleAsync();
        Assert.Equal(new DateOnly(2026, 8, 7), signal.CrEffectiveDate);
    }

    [Fact]
    public async Task Unchanged_version_reports_but_rewrites_nothing()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        await RunIngestAsync(client);
        int[] chunkIds;
        DateTimeOffset recordedAt;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            chunkIds = await db.CrRuleChunks.OrderBy(c => c.Id).Select(c => c.Id).ToArrayAsync();
            recordedAt = (await db.RulesReembedSignals.SingleAsync()).RecordedAt;
        }

        // Forced past the no-op gate so the phase's own unchanged-version
        // path runs (the gate alone would already skip it).
        var report = await RunIngestAsync(client, force: true);

        // "CR version found" is reported every run, changed or not.
        Assert.Equal("2026-08-07", report.GetProperty("cr_version_found").GetString());
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(1, await db.CrDocuments.CountAsync());
            // Untouched rows, not a silent re-replace.
            Assert.Equal(chunkIds, await db.CrRuleChunks.OrderBy(c => c.Id).Select(c => c.Id).ToArrayAsync());
            Assert.Equal(recordedAt, (await db.RulesReembedSignals.SingleAsync()).RecordedAt);
        }
    }

    [Fact]
    public async Task A_version_change_archives_both_versions_and_fully_replaces_the_chunks()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        await RunIngestAsync(client);

        source.RawText = CrFixtures.RawExcerptNextVersion();
        var report = await RunIngestAsync(client);

        Assert.Equal("2026-11-06", report.GetProperty("cr_version_found").GetString());

        await using var db = PostgresFixture.CreateContext(connectionString);
        // Both versions stay archived; only the chunks track the current one.
        Assert.Equal(
            [new DateOnly(2026, 8, 7), new DateOnly(2026, 11, 6)],
            await db.CrDocuments.OrderBy(d => d.EffectiveDate).Select(d => d.EffectiveDate).ToArrayAsync());

        // Full replace: the renumbered rule and renamed term exist only in
        // their new forms.
        var numbers = await db.CrRuleChunks.Select(c => c.Number).ToArrayAsync();
        Assert.Contains("101.2", numbers);
        Assert.DoesNotContain("101.1", numbers);
        var terms = await db.CrGlossaryChunks.Select(g => g.Term).ToArrayAsync();
        Assert.Contains("Abscond", terms);
        Assert.DoesNotContain("Abandon", terms);

        var signal = await db.RulesReembedSignals.SingleAsync();
        Assert.Equal(new DateOnly(2026, 11, 6), signal.CrEffectiveDate);
    }

    // WotC republishing an older version (rollback, corrected file) must
    // not desync the reference surface: the chunks follow the fetch, and
    // GET /rules/{number} echoes the chunked version's date rather than the
    // maximum archived date.
    [Fact]
    public async Task A_republished_older_version_wins_back_the_chunks_and_the_echoed_date()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        await RunIngestAsync(client);
        source.RawText = CrFixtures.RawExcerptNextVersion();
        await RunIngestAsync(client);

        source.RawText = CrFixtures.RawExcerpt().Replace(
            "Synthetic fixture credits line.", "Synthetic fixture credits line, corrected.");
        await RunIngestAsync(client);

        var response = await client.GetAsync("/rules/101.1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("2026-08-07",
            body.RootElement.GetProperty("versions").GetProperty("cr_effective_date").GetString());

        // The newer version's renumbered rule left with its chunks.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/rules/101.2")).StatusCode);
    }

    [Fact]
    public async Task A_source_that_stops_serving_the_cr_fails_the_run_with_itemized_errors()
    {
        var (connectionString, source) = await SetUpAsync();
        source.RawText = "<html>maintenance page</html>";
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();

        var run = await TriggerAndWaitAsync(client, "failed");

        var phase = run.GetProperty("phases").EnumerateArray()
            .Single(p => p.GetProperty("phase").GetString() == "cr_refresh");
        Assert.Equal("failed", phase.GetProperty("status").GetString());
        Assert.NotEmpty(run.GetProperty("report").GetProperty("errors").EnumerateArray());

        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(0, await db.CrDocuments.CountAsync());
        Assert.Equal(0, await db.CrRuleChunks.CountAsync());
    }

    private async Task<(string ConnectionString, FakeCrSource Source)> SetUpAsync()
        => (await postgres.CreateDatabaseAsync($"cr_{Guid.NewGuid():N}"), new FakeCrSource());

    private static ArchiveApp NewApp(string connectionString, FakeCrSource source)
        => new(autoMigrate: true, connectionString, services =>
        {
            services.RemoveAll<ICrSource>();
            services.AddSingleton<ICrSource>(source);
            // Only the CR phase: the other canonical slots skip, so these
            // tests need no Scryfall fixture.
            services.RemoveAll<IIngestPhase>();
            services.AddScoped<IIngestPhase, CrRefreshPhase>();
        });

    private static async Task<JsonElement> RunIngestAsync(HttpClient client, bool force = false)
        => (await TriggerAndWaitAsync(client, "succeeded", force)).GetProperty("report");

    private static async Task<JsonElement> TriggerAndWaitAsync(HttpClient client, string expected, bool force = false)
    {
        var trigger = await client.PostAsJsonAsync("/ingest", new { force });
        Assert.Equal(HttpStatusCode.Accepted, trigger.StatusCode);
        int runId;
        using (var body = JsonDocument.Parse(await trigger.Content.ReadAsStringAsync()))
        {
            runId = body.RootElement.GetProperty("run_id").GetInt32();
        }

        for (var attempt = 0; attempt < 400; attempt++)
        {
            var response = await client.GetAsync($"/ingest/runs/{runId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (body.RootElement.GetProperty("status").GetString() != "running")
            {
                Assert.Equal(expected, body.RootElement.GetProperty("status").GetString());
                return body.RootElement.Clone();
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Ingest run {runId} did not complete.");
    }
}
