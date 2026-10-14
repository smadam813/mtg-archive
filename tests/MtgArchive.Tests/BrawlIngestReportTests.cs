using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The Brawl section of the ingest report (ADR 0012): after
/// each ingest the maintainer sees each Brawl format's as-of date, counts,
/// and the cards whose status changed since the prior ingest. Drift stays a
/// Commander term. Each test runs the real pipeline over HTTP, twice when it
/// needs a prior state.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BrawlIngestReportTests(PostgresFixture postgres) : IDisposable
{
    private readonly string _eventsDir = Directory.CreateTempSubdirectory("mtg-archive-brawl-report-").FullName;

    private readonly FakeBulkSource _source = new();

    public void Dispose() => Directory.Delete(_eventsDir, recursive: true);

    [Fact]
    public async Task The_report_has_one_section_per_brawl_format_with_the_as_of_date_and_three_counts()
    {
        using var app = await NewAppAsync();
        using var client = app.CreateClient();

        var report = await IngestAsync(client,
            CardLine(Guid.NewGuid(), "Everywhere", brawl: BrawlKeys.Legal),
            CardLine(Guid.NewGuid(), "Also Everywhere", brawl: BrawlKeys.Legal),
            CardLine(Guid.NewGuid(), "Banned In Brawl", brawl: new BrawlKeys("banned", "legal", "not_legal")),
            CardLine(Guid.NewGuid(), "No Brawl Keys"));

        var sections = report.GetProperty("brawl_legality").EnumerateArray().ToList();
        Assert.Equal(
            ["brawl", "competitive_brawl", "standard_brawl"],
            sections.Select(s => s.GetProperty("format").GetString()));
        var asOf = _source.UpdatedAt;
        Assert.All(sections, s => Assert.Equal(asOf, s.GetProperty("legality_as_of").GetDateTimeOffset()));
        Assert.Equal((2, 1, 1), Counts(sections[0]));
        Assert.Equal((3, 0, 1), Counts(sections[1]));
        Assert.Equal((2, 0, 2), Counts(sections[2]));
        // The first ingest has no prior state, so nothing changed.
        Assert.All(sections, s => Assert.Empty(s.GetProperty("status_changes").EnumerateArray()));
    }

    [Fact]
    public async Task A_second_ingest_lists_a_card_moved_from_legal_to_banned_as_a_status_change()
    {
        var steady = Guid.NewGuid();
        var banned = Guid.NewGuid();
        using var app = await NewAppAsync();
        using var client = app.CreateClient();
        await IngestAsync(client,
            CardLine(steady, "Steady Card", brawl: BrawlKeys.Legal),
            CardLine(banned, "Soon Banned", brawl: BrawlKeys.Legal));

        var report = await IngestAsync(client,
            CardLine(steady, "Steady Card", brawl: BrawlKeys.Legal),
            CardLine(banned, "Soon Banned", brawl: new BrawlKeys("banned", "legal", "legal")));

        var change = Assert.Single(Section(report, "brawl").GetProperty("status_changes").EnumerateArray());
        Assert.Equal(banned, change.GetProperty("oracle_id").GetGuid());
        Assert.Equal("Soon Banned", change.GetProperty("name").GetString());
        Assert.Equal("legal", change.GetProperty("from").GetString());
        Assert.Equal("banned", change.GetProperty("to").GetString());
        Assert.Empty(Section(report, "competitive_brawl").GetProperty("status_changes").EnumerateArray());
        Assert.Empty(Section(report, "standard_brawl").GetProperty("status_changes").EnumerateArray());
    }

    [Fact]
    public async Task A_rotation_out_of_standard_brawl_shows_as_a_status_change()
    {
        var rotating = Guid.NewGuid();
        using var app = await NewAppAsync();
        using var client = app.CreateClient();
        await IngestAsync(client, CardLine(rotating, "Rotating Card", brawl: BrawlKeys.Legal));

        var report = await IngestAsync(client,
            CardLine(rotating, "Rotating Card", brawl: new BrawlKeys("legal", "legal", "not_legal")));

        var change = Assert.Single(Section(report, "standard_brawl").GetProperty("status_changes").EnumerateArray());
        Assert.Equal(rotating, change.GetProperty("oracle_id").GetGuid());
        Assert.Equal("legal", change.GetProperty("from").GetString());
        Assert.Equal("not_legal", change.GetProperty("to").GetString());
        Assert.Equal((0, 0, 1), Counts(Section(report, "standard_brawl")));
        Assert.Empty(Section(report, "brawl").GetProperty("status_changes").EnumerateArray());
    }

    [Fact]
    public async Task A_brawl_ban_the_curated_files_do_not_name_is_not_drift()
    {
        var card = Guid.NewGuid();
        using var app = await NewAppAsync();
        using var client = app.CreateClient();
        await IngestAsync(client, CardLine(card, "Brawl Only Ban", brawl: BrawlKeys.Legal));

        var report = await IngestAsync(client,
            CardLine(card, "Brawl Only Ban", brawl: new BrawlKeys("banned", "banned", "banned")));

        Assert.Empty(report.GetProperty("drift_detections").EnumerateArray());
        Assert.All(report.GetProperty("brawl_legality").EnumerateArray(),
            s => Assert.Single(s.GetProperty("status_changes").EnumerateArray()));
    }

    [Fact]
    public async Task The_banlist_and_ban_events_serve_commander_bans_only()
    {
        using var app = await NewAppAsync("""
            [{ "effective_date": "2024-01-01", "date_precision": "day",
               "announcement_url": "https://example.org/2024",
               "events": [{ "card": "Commander Ban", "action": "ban", "scope": "all" }] }]
            """);
        using var client = app.CreateClient();
        await IngestAsync(client,
            CardLine(Guid.NewGuid(), "Commander Ban", commanderLegality: "banned", brawl: BrawlKeys.Legal),
            CardLine(Guid.NewGuid(), "Brawl Ban", brawl: new BrawlKeys("banned", "banned", "banned")));

        using var banlist = await GetJsonAsync(client, "/banlist/2024-01-01");
        var banned = Assert.Single(banlist.RootElement.GetProperty("banned").EnumerateArray());
        Assert.Equal("Commander Ban", banned.GetProperty("card").GetProperty("label").GetString());
        var listed = Assert.Single(banlist.RootElement.GetProperty("events").EnumerateArray());

        using var banEvent = await GetJsonAsync(client, $"/ban-events/{listed.GetProperty("id").GetInt32()}");
        Assert.Equal("Commander Ban", banEvent.RootElement.GetProperty("card").GetProperty("label").GetString());
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return JsonDocument.Parse(text);
    }

    private static JsonElement Section(JsonElement report, string format)
        => report.GetProperty("brawl_legality").EnumerateArray()
            .Single(s => s.GetProperty("format").GetString() == format);

    private static (int Legal, int Banned, int NotLegal) Counts(JsonElement section)
        => (section.GetProperty("legal").GetInt32(),
            section.GetProperty("banned").GetInt32(),
            section.GetProperty("not_legal").GetInt32());

    private async Task<JsonElement> IngestAsync(HttpClient client, params string[] lines)
    {
        _source.OracleCards = Lines(lines);
        var run = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        return run.GetProperty("report").Clone();
    }

    private async Task<ArchiveApp> NewAppAsync(string events = """
        [{ "effective_date": "2024-01-01", "date_precision": "day",
           "announcement_url": "https://example.org/2024", "events": [] }]
        """)
    {
        var eventsPath = Path.Combine(_eventsDir, "banlist-events.json");
        await File.WriteAllTextAsync(eventsPath, events);
        return new ArchiveApp(
            autoMigrate: true,
            await postgres.CreateDatabaseAsync($"brawl_report_{Guid.NewGuid():N}"),
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(_source);
                services.RemoveAll<ICrSource>();
                services.AddSingleton<ICrSource>(new FakeCrSource());
            },
            new Dictionary<string, string?> { ["Banlist:EventsPath"] = eventsPath });
    }
}
