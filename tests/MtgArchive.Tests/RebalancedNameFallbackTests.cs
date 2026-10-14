using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The <c>A-</c> fallback of the shared decklist name resolver
/// (ADR 0012). An Arena rebalanced name that does not resolve resolves to
/// the original card, and the deck route reports the rewrite under
/// <c>name_rewrites</c>. A full name that resolves wins first. Each test
/// ingests one fixture corpus through the real pipeline.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RebalancedNameFallbackTests(PostgresFixture postgres) : IDisposable
{
    private static readonly Guid Helix = Guid.NewGuid();
    private static readonly Guid Town = Guid.NewGuid();
    private static readonly Guid ATown = Guid.NewGuid();

    private readonly string _eventsDir = Directory.CreateTempSubdirectory("mtg-archive-rebalanced-").FullName;

    public void Dispose() => Directory.Delete(_eventsDir, recursive: true);

    [Fact]
    public async Task An_a_prefixed_name_resolves_to_the_original_card_and_the_deck_routes_report_the_rewrite()
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();
        var decklist = Deck(("A-Lightning Helix", 1), ("Island", 10));

        var report = await PostAsync(client, "/tier1-report", new { decklist });
        var verdict = await PostAsync(client, "/deck-verdict", new { decklist });

        var echo = report.GetProperty("input").GetProperty("cards").EnumerateArray()
            .Single(c => c.GetProperty("oracle_id").GetGuid() == Helix);
        Assert.Equal("Lightning Helix", echo.GetProperty("name").GetString());
        foreach (var body in new[] { report, verdict })
        {
            var rewrite = Assert.Single(body.GetProperty("name_rewrites").EnumerateArray());
            Assert.Equal("A-Lightning Helix", rewrite.GetProperty("name").GetString());
            Assert.Equal("card", rewrite.GetProperty("card").GetProperty("type").GetString());
            Assert.Equal(Helix.ToString(), rewrite.GetProperty("card").GetProperty("id").GetString());
            Assert.Equal("Lightning Helix", rewrite.GetProperty("card").GetProperty("label").GetString());
        }
    }

    [Fact]
    public async Task A_town_resolves_as_itself_with_no_rewrite()
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();

        var report = await PostAsync(client, "/tier1-report", new { decklist = Deck(("A-Town", 1), ("Island", 10)) });

        Assert.Contains(report.GetProperty("input").GetProperty("cards").EnumerateArray(),
            c => c.GetProperty("oracle_id").GetGuid() == ATown);
        Assert.DoesNotContain(report.GetProperty("input").GetProperty("cards").EnumerateArray(),
            c => c.GetProperty("oracle_id").GetGuid() == Town);
        Assert.False(report.TryGetProperty("name_rewrites", out _));
    }

    [Fact]
    public async Task An_a_prefixed_name_with_no_original_stays_unresolved()
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/tier1-report", new { decklist = Deck(("A-No Such Card", 1)) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var unresolved = Assert.Single(problem.RootElement.GetProperty("unresolved_names").EnumerateArray());
        Assert.Equal("A-No Such Card", unresolved.GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_a_prefixed_name_that_matches_two_cards_stays_unresolved_and_is_not_retried()
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/tier1-report", new { decklist = Deck(("A-Twin", 1), ("Island", 10)) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var unresolved = Assert.Single(problem.RootElement.GetProperty("unresolved_names").EnumerateArray());
        Assert.Equal("A-Twin", unresolved.GetProperty("name").GetString());
    }

    private static object Deck(params (string Name, int Quantity)[] entries)
        => new { entries = entries.Select(e => new { card = new { name = e.Name }, quantity = e.Quantity }).ToArray() };

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
    {
        var response = await client.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private async Task<ArchiveApp> IngestAsync()
    {
        var eventsPath = Path.Combine(_eventsDir, "banlist-events.json");
        await File.WriteAllTextAsync(eventsPath, "[]");
        var source = new FakeBulkSource();
        var app = new ArchiveApp(
            autoMigrate: true,
            await postgres.CreateDatabaseAsync($"rebalanced_{Guid.NewGuid():N}"),
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(source);
                services.RemoveAll<ICrSource>();
                services.AddSingleton<ICrSource>(new FakeCrSource());
            },
            new Dictionary<string, string?> { ["Banlist:EventsPath"] = eventsPath });
        using var client = app.CreateClient();
        source.OracleCards = Lines(
            CardLine(Helix, "Lightning Helix", typeLine: "Instant", manaCost: "{R}{W}", cmc: 2, colorIdentity: ["R", "W"]),
            CardLine(Town, "Town", typeLine: "Land"),
            CardLine(ATown, "A-Town", typeLine: "Land"),
            CardLine(Guid.NewGuid(), "Twin", typeLine: "Land"),
            CardLine(Guid.NewGuid(), "A-Twin", typeLine: "Land"),
            CardLine(Guid.NewGuid(), "A-Twin", typeLine: "Land"),
            CardLine(Guid.NewGuid(), "Island", typeLine: "Basic Land — Island", colorIdentity: ["U"]));
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        return app;
    }
}
