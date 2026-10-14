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
/// Popularity rank with the EDHREC proxy (ADR 0012): ingest
/// derives a total, 1-based popularity order per format and order, and the
/// candidate routes sort by it and echo it with its source. Each test
/// ingests its own fixture snapshot through the real pipeline and asks over
/// HTTP.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PopularityRankTests(PostgresFixture postgres) : IDisposable
{
    private static readonly string[] AllFormats = ["commander", "brawl", "competitive_brawl", "standard_brawl"];

    // Oracle ids whose text order is their uuid order, so the last tie-break
    // is a known literal.
    private static readonly Guid TwinLow = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TwinHigh = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private readonly string _eventsDir = Directory.CreateTempSubdirectory("mtg-archive-popularity-").FullName;

    private readonly FakeBulkSource _source = new();

    public void Dispose() => Directory.Delete(_eventsDir, recursive: true);

    // Today's /cards/search order: edhrec_rank, nulls last, then name, then
    // oracle id. The Commander popularity order must equal it.
    [Fact]
    public async Task The_commander_popularity_order_is_edhrec_rank_nulls_last_then_name_then_oracle_id()
    {
        var best = Guid.NewGuid();
        var second = Guid.NewGuid();
        var alpha = Guid.NewGuid();
        var zeta = Guid.NewGuid();
        using var app = await IngestAsync(
            CardLine(zeta, "Zeta Relic"),
            CardLine(TwinHigh, "Twin Relic"),
            CardLine(second, "Second Relic", edhrecRank: 50),
            CardLine(alpha, "Alpha Relic"),
            CardLine(TwinLow, "Twin Relic"),
            CardLine(best, "Best Relic", edhrecRank: 3));
        using var client = app.CreateClient();

        var hits = await SearchAsync(client, "commander", "main");

        Assert.Equal([best, second, alpha, TwinLow, TwinHigh, zeta], hits.Select(h => h.Id));
        Assert.Equal([1, 2, 3, 4, 5, 6], hits.Select(h => h.Rank));
    }

    [Fact]
    public async Task A_second_ingest_derives_the_same_ranks()
    {
        using var app = await IngestAsync(
            CardLine(Guid.NewGuid(), "Ranked", edhrecRank: 4, brawl: BrawlKeys.Legal),
            CardLine(Guid.NewGuid(), "Unranked", brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();
        var first = await SearchAsync(client, "brawl", "main");

        await WaitForCompletionAsync(client, await TriggerAsync(client, force: true), "succeeded");

        Assert.Equal(first, await SearchAsync(client, "brawl", "main"));
        Assert.Equal([1, 2], first.Select(h => h.Rank));
    }

    [Fact]
    public async Task Every_card_in_a_format_pool_has_a_unique_rank_over_that_pool()
    {
        using var app = await IngestAsync(
            CardLine(Guid.NewGuid(), "Everywhere", edhrecRank: 7, brawl: BrawlKeys.Legal),
            CardLine(Guid.NewGuid(), "Commander Only", edhrecRank: 1),
            CardLine(Guid.NewGuid(), "Banned In Brawl", edhrecRank: 2, brawl: new BrawlKeys("banned", "legal", "legal")),
            CardLine(Guid.NewGuid(), "Unranked Walker", typeLine: "Legendary Planeswalker — Test", brawl: BrawlKeys.Legal),
            CardLine(Guid.NewGuid(), "Not In Standard", edhrecRank: 4, brawl: new BrawlKeys("legal", "legal", "not_legal")));
        using var client = app.CreateClient();

        // A banned card is in the pool but never a candidate, so its rank
        // leaves a gap in the candidates' ranks.
        Assert.Equal(
            [("Commander Only", 1), ("Banned In Brawl", 2), ("Not In Standard", 3), ("Everywhere", 4), ("Unranked Walker", 5)],
            (await SearchAsync(client, "commander", "main")).Select(h => (h.Name, h.Rank!.Value)));
        Assert.Equal(
            [("Not In Standard", 2), ("Everywhere", 3), ("Unranked Walker", 4)],
            (await SearchAsync(client, "brawl", "main")).Select(h => (h.Name, h.Rank!.Value)));
        Assert.Equal(
            [("Banned In Brawl", 1), ("Not In Standard", 2), ("Everywhere", 3), ("Unranked Walker", 4)],
            (await SearchAsync(client, "competitive_brawl", "main")).Select(h => (h.Name, h.Rank!.Value)));
        Assert.Equal(
            [("Banned In Brawl", 1), ("Everywhere", 2), ("Unranked Walker", 3)],
            (await SearchAsync(client, "standard_brawl", "main")).Select(h => (h.Name, h.Rank!.Value)));
    }

    // With no popularity export the commander order equals the card order,
    // so a commander candidate carries its place in the whole pool.
    [Fact]
    public async Task A_commander_search_sorts_by_the_commander_order()
    {
        var walker = Guid.NewGuid();
        var legend = Guid.NewGuid();
        using var app = await IngestAsync(
            CardLine(Guid.NewGuid(), "Popular Rock", edhrecRank: 1, brawl: BrawlKeys.Legal),
            CardLine(walker, "Walker", typeLine: "Legendary Planeswalker — Test", edhrecRank: 9, brawl: BrawlKeys.Legal),
            CardLine(legend, "Legend", typeLine: "Legendary Creature — Elf", edhrecRank: 5, brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();

        Assert.Equal([(legend, 2), (walker, 3)], (await SearchAsync(client, "brawl", "commander")).Select(h => (h.Id, h.Rank!.Value)));
        Assert.Equal([(legend, 2)], (await SearchAsync(client, "commander", "commander")).Select(h => (h.Id, h.Rank!.Value)));
    }

    [Fact]
    public async Task Commander_alternatives_break_ties_by_popularity_rank_and_carry_it()
    {
        var original = Guid.NewGuid();
        var popular = Guid.NewGuid();
        var unranked = Guid.NewGuid();
        var lessPopular = Guid.NewGuid();
        using var app = await IngestAsync(
            CardLine(original, "Original", typeLine: "Legendary Creature — Elf", colorIdentity: ["G"], edhrecRank: 1),
            CardLine(Guid.NewGuid(), "Forest Friend", colorIdentity: ["G"], edhrecRank: 2),
            CardLine(unranked, "Aardvark Legend", typeLine: "Legendary Creature — Elf", colorIdentity: ["G"]),
            CardLine(lessPopular, "Less Popular", typeLine: "Legendary Creature — Elf", colorIdentity: ["G"], edhrecRank: 90),
            CardLine(popular, "Popular", typeLine: "Legendary Creature — Elf", colorIdentity: ["G"], edhrecRank: 30));
        using var client = app.CreateClient();

        using var body = await SearchBodyAsync(client, new
        {
            decklist = new
            {
                commanders = new[] { new { name = "Original" } },
                entries = new[] { new { card = new { name = "Forest Friend" }, quantity = 1 } },
            },
        }, "/commander-alternatives");

        // All three retain the deck at distance 0, so the commander order
        // decides, and Commander keeps the edhrec_rank order.
        var candidates = body.RootElement.GetProperty("candidates").EnumerateArray().ToList();
        Assert.Equal(
            [(popular, 3), (lessPopular, 4), (unranked, 5)],
            candidates.Select(c => (c.GetProperty("card").GetProperty("id").GetGuid(), c.GetProperty("popularity_rank").GetInt32())));
        Assert.All(candidates, c => Assert.False(c.TryGetProperty("edhrec_rank", out _)));
    }

    [Fact]
    public async Task Every_candidate_route_names_the_edhrec_proxy_and_its_as_of_date()
    {
        var legend = Guid.NewGuid();
        using var app = await IngestAsync(
            CardLine(legend, "Legend", typeLine: "Legendary Creature — Elf", edhrecRank: 5, brawl: BrawlKeys.Legal),
            CardLine(Guid.NewGuid(), "Rock", edhrecRank: 1, brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();

        foreach (var format in AllFormats)
        {
            using var search = await SearchBodyAsync(client, new { format, limit = 1 });
            AssertProxy(search.RootElement.GetProperty("versions"), format);
        }

        using var alternatives = await SearchBodyAsync(client, new
        {
            decklist = new { commanders = new[] { new { name = "Legend" } }, entries = new[] { new { card = new { name = "Rock" }, quantity = 1 } } },
        }, "/commander-alternatives");
        AssertProxy(alternatives.RootElement.GetProperty("versions"), "commander");

        void AssertProxy(JsonElement versions, string format)
        {
            Assert.Equal(format, versions.GetProperty("format").GetString());
            Assert.Equal("edhrec", versions.GetProperty("popularity_source").GetString());
            Assert.Equal(_source.UpdatedAt, versions.GetProperty("popularity_as_of").GetDateTimeOffset());
        }
    }

    private async Task<ArchiveApp> IngestAsync(params string[] lines)
    {
        var eventsPath = Path.Combine(_eventsDir, "banlist-events.json");
        await File.WriteAllTextAsync(eventsPath, """
            [{ "effective_date": "2024-01-01", "date_precision": "day",
               "announcement_url": "https://example.org/2024", "events": [] }]
            """);
        var app = new ArchiveApp(
            autoMigrate: true,
            await postgres.CreateDatabaseAsync($"popularity_{Guid.NewGuid():N}"),
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(_source);
                services.RemoveAll<ICrSource>();
                services.AddSingleton<ICrSource>(new FakeCrSource());
            },
            new Dictionary<string, string?> { ["Banlist:EventsPath"] = eventsPath });
        using var client = app.CreateClient();
        _source.OracleCards = Lines(lines);
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        return app;
    }

    private sealed record Hit(Guid Id, string Name, int? Rank);

    private static async Task<List<Hit>> SearchAsync(HttpClient client, string format, string role)
        => [.. (await SearchBodyAsync(client, new { format, role, limit = 200 })).RootElement.GetProperty("candidates")
            .EnumerateArray()
            .Select(c => new Hit(
                c.GetProperty("card").GetProperty("id").GetGuid(),
                c.GetProperty("card").GetProperty("label").GetString()!,
                c.TryGetProperty("popularity_rank", out var rank) && rank.ValueKind == JsonValueKind.Number
                    ? rank.GetInt32()
                    : null))];

    private static async Task<JsonDocument> SearchBodyAsync(HttpClient client, object body, string route = "/cards/search")
    {
        var response = await client.PostAsJsonAsync(route, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return JsonDocument.Parse(text);
    }
}
