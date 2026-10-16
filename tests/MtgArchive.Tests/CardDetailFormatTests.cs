using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// Brawl and Standard legality in card detail (ADR 0012): one card lookup
/// shows per-role legality in all five formats. The <c>banlist_version</c>
/// pin applies to the Commander rows only. Each unversioned row holds its
/// <c>legality</c> citation, the Scryfall key, the Scryfall URL, and the
/// as-of date. Each test ingests its own fixture snapshot through the real
/// pipeline and reads card detail over HTTP.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CardDetailFormatTests(PostgresFixture postgres) : IDisposable
{
    private const string Golos = "Golos, Tireless Pilgrim";

    private static readonly DateTimeOffset AsOf = new(2026, 10, 3, 21, 9, 47, TimeSpan.Zero);

    private readonly string _eventsDir = Directory.CreateTempSubdirectory("mtg-archive-detail-").FullName;

    public void Dispose() => Directory.Delete(_eventsDir, recursive: true);

    [Fact]
    public async Task Card_detail_returns_per_role_legality_in_all_five_formats()
    {
        var golos = Guid.NewGuid();
        using var app = await IngestAsync(CardLine(golos, Golos, typeLine: "Legendary Artifact Creature — Scout",
            brawl: new BrawlKeys("legal", "banned", "not_legal", Standard: "legal")));
        using var client = app.CreateClient();

        var detail = await GetJsonAsync(client, $"/cards/{golos}");

        var rows = detail.GetProperty("legality").EnumerateArray().ToList();
        Assert.Equal(
            [
                ("commander", "commander", "banned"), ("commander", "companion", "not_eligible"), ("commander", "main", "banned"),
                ("brawl", "commander", "legal"), ("brawl", "companion", "not_eligible"), ("brawl", "main", "legal"),
                ("competitive_brawl", "commander", "banned"), ("competitive_brawl", "companion", "not_eligible"),
                ("competitive_brawl", "main", "banned"),
                ("standard_brawl", "commander", "not_legal"), ("standard_brawl", "companion", "not_legal"),
                ("standard_brawl", "main", "not_legal"),
                ("standard", "commander", "not_eligible"), ("standard", "companion", "not_eligible"),
                ("standard", "main", "legal"),
            ],
            rows.Select(r => (
                r.GetProperty("format").GetString()!,
                r.GetProperty("role").GetString()!,
                r.GetProperty("status").GetString()!)));

        // A Commander row cites its ban event and carries no Brawl member.
        Assert.Equal("ban_event", rows[0].GetProperty("ban_event").GetProperty("type").GetString());
        Assert.All(rows.Take(3), r =>
        {
            Assert.False(r.TryGetProperty("scryfall_key", out _));
            Assert.False(r.TryGetProperty("legality_as_of", out _));
            Assert.False(r.TryGetProperty("citation", out _));
        });

        // Every Brawl and Standard row, banned or not, holds the Scryfall URL, the key,
        // the as-of date, and its legality citation.
        var keys = new Dictionary<string, string>
        {
            ["brawl"] = "brawl",
            ["competitive_brawl"] = "competitivebrawl",
            ["standard_brawl"] = "standardbrawl",
            ["standard"] = "standard",
        };
        Assert.All(rows.Skip(3), r =>
        {
            var key = keys[r.GetProperty("format").GetString()!];
            Assert.Equal(key, r.GetProperty("scryfall_key").GetString());
            Assert.Equal($"https://scryfall.com/card/tst/1/{golos}", r.GetProperty("scryfall_uri").GetString());
            Assert.Equal(AsOf, r.GetProperty("legality_as_of").GetDateTimeOffset());
            Assert.False(r.TryGetProperty("ban_event", out _));
            var citation = r.GetProperty("citation");
            Assert.Equal("legality", citation.GetProperty("type").GetString());
            Assert.Equal(golos.ToString(), citation.GetProperty("id").GetString());
        });
        Assert.Equal("competitivebrawl banned as of 2026-10-03",
            rows[8].GetProperty("citation").GetProperty("label").GetString());
        Assert.Equal("standardbrawl not_legal as of 2026-10-03",
            rows[11].GetProperty("citation").GetProperty("label").GetString());
        Assert.Equal("brawl legal as of 2026-10-03",
            rows[5].GetProperty("citation").GetProperty("label").GetString());
        Assert.Equal("standard legal as of 2026-10-03",
            rows[14].GetProperty("citation").GetProperty("label").GetString());

        // A Standard deck has no commander seat, so a legendary creature
        // is not eligible there, though the key says legal.
        Assert.Equal("standard legal as of 2026-10-03; not eligible as commander",
            rows[12].GetProperty("citation").GetProperty("label").GetString());

        // The key never holds not_eligible: the label gives what the key
        // says, then the role fact the service derives.
        Assert.Equal("brawl legal as of 2026-10-03; not eligible as companion",
            rows[4].GetProperty("citation").GetProperty("label").GetString());
        Assert.Equal("competitivebrawl banned as of 2026-10-03; not eligible as companion",
            rows[7].GetProperty("citation").GetProperty("label").GetString());
    }

    [Fact]
    public async Task A_banlist_version_pin_changes_only_the_commander_rows()
    {
        var golos = Guid.NewGuid();
        using var app = await IngestAsync(CardLine(golos, Golos, typeLine: "Legendary Artifact Creature — Scout",
            brawl: new BrawlKeys("legal", "banned", "not_legal")));
        using var client = app.CreateClient();

        var latest = await GetJsonAsync(client, $"/cards/{golos}");
        var pinned = await GetJsonAsync(client, $"/cards/{golos}?banlist_version=2020-01-01");

        Assert.Equal("2020-01-01", pinned.GetProperty("versions").GetProperty("banlist_version").GetString());
        Assert.Equal(["banned", "not_eligible", "banned"], Statuses(latest, "commander"));
        Assert.Equal(["legal", "not_eligible", "legal"], Statuses(pinned, "commander"));
        Assert.False(Rows(pinned, "commander")[0].TryGetProperty("ban_event", out _));
        foreach (var format in new[] { "brawl", "competitive_brawl", "standard_brawl", "standard" })
        {
            Assert.Equal(
                Rows(latest, format).Select(r => r.GetRawText()),
                Rows(pinned, format).Select(r => r.GetRawText()));
        }

        // An unknown pin is still a 400, never a fallback.
        var unknown = await client.GetAsync($"/cards/{golos}?banlist_version=1999-01-01");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task A_legality_citation_resolves_through_card_detail_to_its_row()
    {
        var golos = Guid.NewGuid();
        using var app = await IngestAsync(CardLine(golos, Golos, typeLine: "Legendary Artifact Creature — Scout",
            brawl: new BrawlKeys("legal", "banned", "not_legal")));
        using var client = app.CreateClient();

        // Found by name, the way a skill reaches a card the user names.
        var byName = await GetJsonAsync(client, $"/cards?name={Uri.EscapeDataString(Golos)}");
        var citation = Rows(byName, "competitive_brawl")
            .Single(r => r.GetProperty("role").GetString() == "commander")
            .GetProperty("citation");
        Assert.Equal("competitivebrawl banned as of 2026-10-03", citation.GetProperty("label").GetString());

        var target = await GetJsonAsync(client, $"/cards/{citation.GetProperty("id").GetString()}");
        var row = Rows(target, "competitive_brawl").Single(r => r.GetProperty("role").GetString() == "commander");
        Assert.Equal("banned", row.GetProperty("status").GetString());
        Assert.Equal("competitivebrawl", row.GetProperty("scryfall_key").GetString());
        Assert.Equal($"https://scryfall.com/card/tst/1/{golos}", row.GetProperty("scryfall_uri").GetString());
        Assert.Equal(AsOf, row.GetProperty("legality_as_of").GetDateTimeOffset());
    }

    private static List<JsonElement> Rows(JsonElement detail, string format)
        => [.. detail.GetProperty("legality").EnumerateArray().Where(r => r.GetProperty("format").GetString() == format)];

    private static string[] Statuses(JsonElement detail, string format)
        => [.. Rows(detail, format).Select(r => r.GetProperty("status").GetString()!)];

    private async Task<ArchiveApp> IngestAsync(params string[] lines)
    {
        // Golos is banned in Commander from 2024-01-01 on, so a pin to the
        // earlier version shows the Commander rows before the ban.
        var eventsPath = Path.Combine(_eventsDir, "banlist-events.json");
        await File.WriteAllTextAsync(eventsPath, $$"""
            [{ "effective_date": "2020-01-01", "date_precision": "day",
               "announcement_url": "https://example.org/2020", "events": [] },
             { "effective_date": "2024-01-01", "date_precision": "day",
               "announcement_url": "https://example.org/2024",
               "events": [{ "card": "{{Golos}}", "action": "ban", "scope": "all" }] }]
            """);
        var source = new FakeBulkSource();
        var app = new ArchiveApp(
            autoMigrate: true,
            await postgres.CreateDatabaseAsync($"detail_{Guid.NewGuid():N}"),
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(source);
                services.RemoveAll<ICrSource>();
                services.AddSingleton<ICrSource>(new FakeCrSource());
            },
            new Dictionary<string, string?> { ["Banlist:EventsPath"] = eventsPath });
        using var client = app.CreateClient();
        source.OracleCards = Lines(lines);
        source.UpdatedAt = AsOf;
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        return app;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);
        return body.RootElement.Clone();
    }
}
