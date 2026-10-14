using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// Brawl legality in search (ADR 0012): each Brawl format's
/// candidates come from its own Scryfall key, and the three Brawl formats
/// share one commander rule. Each test ingests its own fixture snapshot
/// through the real pipeline and asks the candidate routes over HTTP.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BrawlCandidateTests(PostgresFixture postgres) : IDisposable
{
    private static readonly string[] BrawlFormats = ["brawl", "competitive_brawl", "standard_brawl"];

    private static readonly string[] AllFormats = ["commander", .. BrawlFormats];

    private readonly string _eventsDir = Directory.CreateTempSubdirectory("mtg-archive-brawl-").FullName;

    public void Dispose() => Directory.Delete(_eventsDir, recursive: true);

    [Fact]
    public async Task A_card_outside_a_brawl_pool_never_shows_as_a_candidate()
    {
        var inAll = Guid.NewGuid();
        var notInBrawl = Guid.NewGuid();
        var bannedInCompetitive = Guid.NewGuid();
        var noKeys = Guid.NewGuid();
        using var app = await IngestAsync(
            CardLine(inAll, "In Every Pool", brawl: BrawlKeys.Legal),
            CardLine(notInBrawl, "Not In Brawl", brawl: new BrawlKeys("not_legal", "legal", "not_legal")),
            CardLine(bannedInCompetitive, "Banned In Competitive", brawl: new BrawlKeys("legal", "banned", "legal")),
            CardLine(noKeys, "No Brawl Keys"));
        using var client = app.CreateClient();

        Assert.Equal(new[] { inAll, bannedInCompetitive }.Order(), (await CandidatesAsync(client, "brawl", "main")).Order());
        Assert.Equal(new[] { inAll, notInBrawl }.Order(), (await CandidatesAsync(client, "competitive_brawl", "main")).Order());
        Assert.Equal(new[] { inAll, bannedInCompetitive }.Order(), (await CandidatesAsync(client, "standard_brawl", "main")).Order());
        // Commander still reads its own key and its curated bans.
        Assert.Equal(
            new[] { inAll, notInBrawl, bannedInCompetitive, noKeys }.Order(),
            (await CandidatesAsync(client, "commander", "main")).Order());
    }

    [Fact]
    public async Task A_legendary_planeswalker_commands_in_each_brawl_format_and_not_in_commander()
    {
        var walker = Guid.NewGuid();
        var textWalker = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var bareVehicle = Guid.NewGuid();
        using var app = await IngestAsync(
            CardLine(walker, "Plain Walker", typeLine: "Legendary Planeswalker — Test", brawl: BrawlKeys.Legal),
            CardLine(textWalker, "Text Walker", typeLine: "Legendary Planeswalker — Test",
                oracleText: "Text Walker can be your commander.", brawl: BrawlKeys.Legal),
            CardLine(vehicle, "Crewed Ship", typeLine: "Legendary Artifact — Vehicle", power: "3", toughness: "3",
                brawl: BrawlKeys.Legal),
            CardLine(bareVehicle, "Bare Ship", typeLine: "Legendary Artifact — Vehicle", brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();

        foreach (var format in BrawlFormats)
        {
            Assert.Equal(
                new[] { walker, textWalker, vehicle }.Order(),
                (await CandidatesAsync(client, format, "commander")).Order());
        }

        Assert.Equal([textWalker], await CandidatesAsync(client, "commander", "commander"));
    }

    [Fact]
    public async Task A_meld_result_is_never_commander_legal_in_any_format()
    {
        var meld = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "ingest", "meld-cards.jsonl"))
            .Select(WithBrawlKeysLegal)
            .ToArray();
        using var app = await IngestAsync(meld);
        using var client = app.CreateClient();

        foreach (var format in AllFormats)
        {
            var names = await CandidateNamesAsync(client, format, "commander");
            Assert.DoesNotContain("Brisela, Voice of Nightmares", names);
            Assert.Contains("Bruna, the Fading Light", names);
        }
    }

    // The Brawl commander rule against Scryfall's is:brawler search, end to
    // end. Every positive is legal in brawl and competitivebrawl (is:brawler
    // requires both), and every negative is too, so a format's commander
    // candidates are exactly the positives inside its own pool.
    [Fact]
    public async Task The_brawl_commander_rule_matches_an_is_brawler_sample()
    {
        var brawlers = SampleLines("is-brawler-sample.jsonl");
        var others = SampleLines("not-brawler-sample.jsonl");
        using var app = await IngestAsync([.. brawlers, .. others]);
        using var client = app.CreateClient();

        foreach (var (format, key) in LegalityRebuildPhase.BrawlKeys)
        {
            var wire = format switch
            {
                Data.Format.Brawl => "brawl",
                Data.Format.CompetitiveBrawl => "competitive_brawl",
                _ => "standard_brawl",
            };
            var expected = brawlers
                .Where(line => JsonNode.Parse(line)!["legalities"]![key]?.GetValue<string>() == "legal")
                .Select(line => Guid.Parse(JsonNode.Parse(line)!["oracle_id"]!.GetValue<string>()))
                .Order()
                .ToList();
            Assert.NotEmpty(expected);
            Assert.Equal(expected, (await CandidatesAsync(client, wire, "commander")).Order());
        }

        // The negatives are in the pool, just not commanders.
        var main = await CandidatesAsync(client, "brawl", "main");
        Assert.All(others, line => Assert.Contains(Guid.Parse(JsonNode.Parse(line)!["oracle_id"]!.GetValue<string>()), main));
    }

    private static string[] SampleLines(string file)
        => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "ingest", file));

    private static string WithBrawlKeysLegal(string line)
    {
        var node = JsonNode.Parse(line)!;
        var legalities = node["legalities"]!.AsObject();
        legalities["brawl"] = "legal";
        legalities["competitivebrawl"] = "legal";
        legalities["standardbrawl"] = "legal";
        return node.ToJsonString();
    }

    private async Task<ArchiveApp> IngestAsync(params string[] lines)
    {
        var eventsPath = Path.Combine(_eventsDir, "banlist-events.json");
        await File.WriteAllTextAsync(eventsPath, """
            [{ "effective_date": "2024-01-01", "date_precision": "day",
               "announcement_url": "https://example.org/2024", "events": [] }]
            """);
        var source = new FakeBulkSource();
        var app = new ArchiveApp(
            autoMigrate: true,
            await postgres.CreateDatabaseAsync($"brawl_{Guid.NewGuid():N}"),
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
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        return app;
    }

    private static async Task<List<Guid>> CandidatesAsync(HttpClient client, string format, string role)
        => [.. (await SearchAsync(client, format, role)).Select(c => c.GetProperty("id").GetGuid())];

    private static async Task<List<string>> CandidateNamesAsync(HttpClient client, string format, string role)
        => [.. (await SearchAsync(client, format, role)).Select(c => c.GetProperty("label").GetString()!)];

    private static async Task<IEnumerable<JsonElement>> SearchAsync(HttpClient client, string format, string role)
    {
        var response = await client.PostAsJsonAsync("/cards/search", new { format, role, limit = 200 });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);
        Assert.Equal(format, body.RootElement.GetProperty("versions").GetProperty("format").GetString());
        return [.. body.RootElement.GetProperty("candidates").EnumerateArray()
            .Select(c => c.GetProperty("card").Clone())];
    }
}
