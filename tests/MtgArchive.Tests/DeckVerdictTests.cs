using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Data;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The deck verdict (ADR 0012): <c>POST /deck-verdict</c> judges
/// one decklist in one format, card legality and deck shape together, with
/// no simulation. Each test ingests the same fixture snapshot through the
/// real pipeline and reads the verdict over HTTP.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DeckVerdictTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset AsOf = new(2026, 10, 3, 21, 9, 47, TimeSpan.Zero);

    private readonly string _eventsDir = Directory.CreateTempSubdirectory("mtg-archive-verdict-").FullName;

    public void Dispose() => Directory.Delete(_eventsDir, recursive: true);

    [Fact]
    public async Task A_legal_100_card_commander_deck_is_legal_and_too_big_for_standard_brawl()
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();
        var deck = Deck(["Ula, Tide Caller"], Fillers(40).Append(("Island", 59)));

        var commander = await VerdictAsync(client, deck, null);
        var standard = await VerdictAsync(client, deck, "standard_brawl");

        Assert.Equal("legal", commander.GetProperty("status").GetString());
        Assert.Empty(commander.GetProperty("card_findings").EnumerateArray());
        Assert.Empty(commander.GetProperty("shape_findings").EnumerateArray());
        Assert.Equal(100, commander.GetProperty("deck_size").GetInt32());
        Assert.Equal("commander", commander.GetProperty("versions").GetProperty("format").GetString());

        Assert.Equal("illegal", standard.GetProperty("status").GetString());
        var finding = Assert.Single(standard.GetProperty("shape_findings").EnumerateArray());
        Assert.Equal("too_many_cards", finding.GetProperty("kind").GetString());
        Assert.Equal(60, standard.GetProperty("required_deck_size").GetInt32());
        Assert.Equal("903.12", finding.GetProperty("citation").GetProperty("id").GetString());
        Assert.Equal(AsOf, standard.GetProperty("versions").GetProperty("legality_as_of").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_short_list_or_a_list_with_no_commander_is_incomplete()
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();

        var short70 = await VerdictAsync(client, Deck(["Ula, Tide Caller"], Fillers(40).Append(("Island", 29))), null);
        var headless = await VerdictAsync(client, Deck([], Fillers(40).Append(("Island", 60))), "brawl");

        Assert.Equal("incomplete", short70.GetProperty("status").GetString());
        Assert.Equal(70, short70.GetProperty("deck_size").GetInt32());
        Assert.Equal(["too_few_cards"], Kinds(short70));
        Assert.Equal("incomplete", headless.GetProperty("status").GetString());
        Assert.Equal(["no_commander"], Kinds(headless));
    }

    [Fact]
    public async Task Extra_copies_an_off_identity_card_a_bad_pairing_or_a_card_not_legal_in_its_role_is_illegal()
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();

        var extra = await VerdictAsync(client, Deck(["Ula, Tide Caller"],
            Fillers(39).Skip(1).Append(("Filler 01", 2)).Append(("Island", 59))), null);
        var offIdentity = await VerdictAsync(client, Deck(["Ula, Tide Caller"],
            Fillers(39).Append(("Red Spell", 1)).Append(("Island", 59))), null);
        var badPair = await VerdictAsync(client, Deck(["Ula, Tide Caller", "Pira, Partner"],
            Fillers(39).Append(("Island", 59))), null);
        var notEligible = await VerdictAsync(client, Deck(["Filler 41"], Fillers(40).Append(("Wastes", 59))), null);

        Assert.Equal("illegal", extra.GetProperty("status").GetString());
        var copies = Assert.Single(extra.GetProperty("shape_findings").EnumerateArray());
        Assert.Equal("copy_limit", copies.GetProperty("kind").GetString());
        Assert.Equal(["Filler 01"], CardNames(copies));

        Assert.Equal("illegal", offIdentity.GetProperty("status").GetString());
        var identity = Assert.Single(offIdentity.GetProperty("shape_findings").EnumerateArray());
        Assert.Equal("color_identity", identity.GetProperty("kind").GetString());
        Assert.Equal(["Red Spell"], CardNames(identity));

        Assert.Equal("illegal", badPair.GetProperty("status").GetString());
        Assert.Equal(["pairing"], Kinds(badPair));

        Assert.Equal("illegal", notEligible.GetProperty("status").GetString());
        Assert.Empty(notEligible.GetProperty("shape_findings").EnumerateArray());
        var finding = Assert.Single(notEligible.GetProperty("card_findings").EnumerateArray());
        Assert.Equal("Filler 41", finding.GetProperty("card").GetProperty("label").GetString());
        Assert.Equal("commander", finding.GetProperty("role").GetString());
        Assert.Equal("not_eligible", finding.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("Pira, Partner", "Pyra, Partner")]
    [InlineData("Wilson, Bear Friend", "Cary, Bear Friend")]
    [InlineData("Mira, Who Chooses", "Guild Background")]
    [InlineData("Rory, Companion", "The Tenth Doctor")]
    [InlineData("Ana, Partnered", "Bo, Partnered")]
    public async Task Each_pairing_rule_admits_its_pair(string first, string second)
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();

        var verdict = await VerdictAsync(client, Deck([first, second], Fillers(40).Append(("Wastes", 58))), "brawl");

        Assert.Equal("legal", verdict.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_colorless_brawl_commander_allows_basics_of_one_type_only()
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();

        var oneType = await VerdictAsync(client, Deck(["Karn Golem"], Fillers(40).Append(("Island", 59))), "brawl");
        var twoTypes = await VerdictAsync(client, Deck(["Karn Golem"],
            Fillers(40).Append(("Island", 30)).Append(("Plains", 29))), "competitive_brawl");
        var inCommander = await VerdictAsync(client, Deck(["Karn Golem"], Fillers(40).Append(("Island", 59))), null);

        Assert.Equal("legal", oneType.GetProperty("status").GetString());
        Assert.Equal("illegal", twoTypes.GetProperty("status").GetString());
        var finding = Assert.Single(twoTypes.GetProperty("shape_findings").EnumerateArray());
        Assert.Equal("basic_land_types", finding.GetProperty("kind").GetString());
        Assert.Equal(["Island", "Plains"], CardNames(finding));
        Assert.Equal("903.12e", finding.GetProperty("citation").GetProperty("id").GetString());

        // Commander keeps its own color identity rule.
        Assert.Equal("illegal", inCommander.GetProperty("status").GetString());
        Assert.Equal(["color_identity"], Kinds(inCommander));
    }

    [Fact]
    public async Task A_companion_does_not_count_and_is_judged_in_its_role_and_identity()
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();
        var main = Fillers(40).Append(("Island", 59)).ToList();

        var onIdentity = await VerdictAsync(client, Deck(["Ula, Tide Caller"], main, companion: "Blue Companion"), "brawl");
        var offIdentity = await VerdictAsync(client, Deck(["Ula, Tide Caller"], main, companion: "Green Companion"), "brawl");
        var noKeyword = await VerdictAsync(client, Deck(["Ula, Tide Caller"], main, companion: "Filler 41"), "brawl");

        Assert.Equal("legal", onIdentity.GetProperty("status").GetString());
        Assert.Equal(100, onIdentity.GetProperty("deck_size").GetInt32());

        Assert.Equal("illegal", offIdentity.GetProperty("status").GetString());
        var identity = Assert.Single(offIdentity.GetProperty("shape_findings").EnumerateArray());
        Assert.Equal("color_identity", identity.GetProperty("kind").GetString());
        Assert.Equal(["Green Companion"], CardNames(identity));

        Assert.Equal("illegal", noKeyword.GetProperty("status").GetString());
        var finding = Assert.Single(noKeyword.GetProperty("card_findings").EnumerateArray());
        Assert.Equal("companion", finding.GetProperty("role").GetString());
        Assert.Equal("not_eligible", finding.GetProperty("status").GetString());
        Assert.Equal("brawl legal as of 2026-10-03; not eligible as companion",
            finding.GetProperty("citation").GetProperty("label").GetString());
    }

    [Fact]
    public async Task Missing_legality_rows_give_unknown()
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
            await db.Legalities
                .Where(l => l.Format == Format.Brawl && db.Cards.Any(c => c.Id == l.CardId && c.Name == "Filler 07"))
                .ExecuteDeleteAsync();
        }

        var deck = Deck(["Ula, Tide Caller"], Fillers(40).Append(("Island", 59)));
        var brawl = await VerdictAsync(client, deck, "brawl");
        var shortList = await VerdictAsync(client, Deck(["Ula, Tide Caller"], Fillers(40)), "brawl");
        var broken = await VerdictAsync(client, Deck(["Ula, Tide Caller"], Fillers(40).Append(("Red Spell", 1))), "brawl");

        Assert.Equal("unknown", brawl.GetProperty("status").GetString());
        Assert.Equal("unknown", shortList.GetProperty("status").GetString());
        Assert.Equal("illegal", broken.GetProperty("status").GetString());
        Assert.Equal("legal", (await VerdictAsync(client, deck, "competitive_brawl")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Each_brawl_card_finding_cites_its_legality_row_and_a_commander_ban_cites_its_ban_event()
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();
        var deck = Deck(["Ula, Tide Caller"], Fillers(39).Append(("Golos Rock", 1)).Append(("Island", 59)));

        var competitive = await VerdictAsync(client, deck, "competitive_brawl");
        var standard = await VerdictAsync(client, deck, "standard_brawl");
        var commander = await VerdictAsync(client, deck, null);

        var banned = Assert.Single(competitive.GetProperty("card_findings").EnumerateArray());
        Assert.Equal("banned", banned.GetProperty("status").GetString());
        Assert.False(banned.TryGetProperty("ban_event", out _));
        var citation = banned.GetProperty("citation");
        Assert.Equal("legality", citation.GetProperty("type").GetString());
        Assert.Equal("competitivebrawl banned as of 2026-10-03", citation.GetProperty("label").GetString());

        // The citation resolves through card detail to the row it names.
        var detail = await GetJsonAsync(client, $"/cards/{citation.GetProperty("id").GetString()}");
        var row = detail.GetProperty("legality").EnumerateArray().Single(r =>
            r.GetProperty("format").GetString() == "competitive_brawl" && r.GetProperty("role").GetString() == "main");
        Assert.Equal("banned", row.GetProperty("status").GetString());
        Assert.Equal(citation.GetRawText(), row.GetProperty("citation").GetRawText());

        var notLegal = standard.GetProperty("card_findings").EnumerateArray()
            .Single(f => f.GetProperty("card").GetProperty("label").GetString() == "Golos Rock");
        Assert.Equal("standardbrawl not_legal as of 2026-10-03",
            notLegal.GetProperty("citation").GetProperty("label").GetString());

        var ban = Assert.Single(commander.GetProperty("card_findings").EnumerateArray());
        Assert.False(ban.TryGetProperty("citation", out _));
        var banEvent = ban.GetProperty("ban_event");
        Assert.Equal("ban_event", banEvent.GetProperty("type").GetString());
        await GetJsonAsync(client, $"/ban-events/{banEvent.GetProperty("id").GetString()}");
    }

    [Fact]
    public async Task A_brawl_format_with_a_banlist_pin_or_an_unknown_format_is_a_400()
    {
        using var app = await IngestAsync();
        using var client = app.CreateClient();
        var decklist = Deck(["Ula, Tide Caller"], Fillers(40));

        var pinned = await client.PostAsJsonAsync("/deck-verdict", new { decklist, format = "brawl", banlist_version = "2020-01-01" });
        var unknown = await client.PostAsJsonAsync("/deck-verdict", new { decklist, format = "modern" });
        var unresolved = await client.PostAsJsonAsync("/deck-verdict", new { decklist = Deck(["Nobody"], []), format = "brawl" });

        Assert.Equal(HttpStatusCode.BadRequest, pinned.StatusCode);
        Assert.Contains("unversioned-format", await pinned.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("unknown-format", await unknown.Content.ReadAsStringAsync());
        Assert.Contains("unresolved_names", await unresolved.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);
        return body.RootElement.Clone();
    }

    private static string[] CardNames(JsonElement finding)
        => [.. finding.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("label").GetString()!)];

    private static string[] Kinds(JsonElement verdict)
        => [.. verdict.GetProperty("shape_findings").EnumerateArray().Select(f => f.GetProperty("kind").GetString()!)];

    private static IEnumerable<(string Name, int Quantity)> Fillers(int count)
        => Enumerable.Range(1, count).Select(i => ($"Filler {i:D2}", 1));

    private static object Deck(
        IEnumerable<string> commanders, IEnumerable<(string Name, int Quantity)> entries, string? companion = null)
        => new
        {
            commanders = commanders.Select(name => new { name }).ToArray(),
            companion = companion is null ? null : new { name = companion },
            entries = entries.Select(e => new { card = new { name = e.Name }, quantity = e.Quantity }).ToArray(),
        };

    private static async Task<JsonElement> VerdictAsync(HttpClient client, object decklist, string? format)
    {
        var response = await client.PostAsJsonAsync("/deck-verdict", new { decklist, format });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        using var body = JsonDocument.Parse(text);
        return body.RootElement.Clone();
    }

    /// <summary>The fixture corpus: every card in all three Brawl pools
    /// unless a line says otherwise.</summary>
    private static string[] Corpus()
    {
        var legal = BrawlKeys.Legal;
        List<string> lines =
        [
            CardLine(Guid.NewGuid(), "Ula, Tide Caller", typeLine: "Legendary Creature — Merfolk", colorIdentity: ["U"], brawl: legal),
            CardLine(Guid.NewGuid(), "Island", typeLine: "Basic Land — Island", colorIdentity: ["U"], brawl: legal),
            CardLine(Guid.NewGuid(), "Wastes", typeLine: "Basic Land", brawl: legal),
            CardLine(Guid.NewGuid(), "Golos Rock", brawl: new BrawlKeys("legal", "banned", "not_legal")),
            CardLine(Guid.NewGuid(), "Plains",typeLine: "Basic Land — Plains", colorIdentity: ["W"], brawl: legal),
            Legend("Karn Golem", [], typeLine: "Legendary Artifact Creature — Golem"),
            Legend("Blue Companion", ["U"], keywords: ["Companion"], text: "Companion — Your deck has no red cards."),
            Legend("Green Companion", ["G"], keywords: ["Companion"], text: "Companion — Your deck has no red cards."),
            CardLine(Guid.NewGuid(), "Red Spell", typeLine: "Instant", colorIdentity: ["R"], brawl: legal),
            Legend("Pira, Partner", ["R"], keywords: ["Partner"], text: "Partner"),
            Legend("Pyra, Partner", ["W"], keywords: ["Partner"], text: "Partner"),
            Legend("Wilson, Bear Friend", ["G"], keywords: ["Partner"], text: FriendsForever),
            Legend("Cary, Bear Friend", ["B"], keywords: ["Partner"], text: FriendsForever),
            Legend("Mira, Who Chooses", ["U"], keywords: ["Choose a background"], text: "Choose a Background"),
            CardLine(Guid.NewGuid(), "Guild Background", typeLine: "Legendary Enchantment — Background", colorIdentity: ["W"], brawl: legal),
            Legend("Rory, Companion", ["W"], keywords: ["Doctor's companion"], text: "Doctor's companion"),
            Legend("The Tenth Doctor", ["R"], typeLine: "Legendary Creature — Time Lord Doctor"),
            Legend("Ana, Partnered", ["G"], keywords: ["Partner with", "Partner"], text: "Partner with Bo, Partnered"),
            Legend("Bo, Partnered", ["U"], keywords: ["Partner with", "Partner"], text: "Partner with Ana, Partnered"),
        ];
        lines.AddRange(Fillers(41).Select(f => CardLine(Guid.NewGuid(), f.Name, brawl: legal)));
        return [.. lines];
    }

    private const string FriendsForever = "Partner—Friends forever (You can have two commanders if both have friends forever.)";

    private static string Legend(
        string name, string[] colorIdentity, string[]? keywords = null, string? text = null,
        string typeLine = "Legendary Creature — Human")
        => CardLine(Guid.NewGuid(), name, typeLine: typeLine, oracleText: text, keywords: keywords,
            colorIdentity: colorIdentity, power: "2", toughness: "2", brawl: BrawlKeys.Legal);

    private async Task<ArchiveApp> IngestAsync()
    {
        var eventsPath = Path.Combine(_eventsDir, "banlist-events.json");
        await File.WriteAllTextAsync(eventsPath, """
            [{ "effective_date": "2020-01-01", "date_precision": "day",
               "announcement_url": "https://example.org/2020",
               "events": [{ "card": "Golos Rock", "action": "ban", "scope": "all" }] }]
            """);
        var source = new FakeBulkSource();
        var app = new ArchiveApp(
            autoMigrate: true,
            await postgres.CreateDatabaseAsync($"verdict_{Guid.NewGuid():N}"),
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(source);
                services.RemoveAll<ICrSource>();
                services.AddSingleton<ICrSource>(new FakeCrSource());
            },
            new Dictionary<string, string?> { ["Banlist:EventsPath"] = eventsPath });
        using var client = app.CreateClient();
        source.OracleCards = Lines(Corpus());
        source.UpdatedAt = AsOf;
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        return app;
    }
}
