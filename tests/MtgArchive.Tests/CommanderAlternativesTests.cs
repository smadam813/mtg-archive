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
/// POST /commander-alternatives over the real pipeline: the
/// pool is commander-role-legal cards only with no color-identity
/// pre-filter, the original commanders are excluded, retention and rank
/// follow the declared rule, ban losses cite fetchable ban events, an
/// unknown pin is a 400, and collection scope hard-filters and reports
/// availability. Each test seeds its own migrated database. The format
/// tests (ADR 0012) ingest a fixture snapshot through the real
/// pipeline instead, so the Brawl rows come from the rebuild.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CommanderAlternativesTests(PostgresFixture postgres) : IDisposable
{
    private const string Version = "2025-04-21";

    private static readonly DateTimeOffset AsOf = new(2026, 10, 3, 21, 9, 47, TimeSpan.Zero);

    private readonly string _eventsDir = Directory.CreateTempSubdirectory("mtg-archive-alternatives-").FullName;

    public void Dispose() => Directory.Delete(_eventsDir, recursive: true);

    [Fact]
    public async Task A_brawl_request_ranks_the_formats_commanders_and_cites_its_legality_losses()
    {
        var wrenn = Guid.NewGuid();
        var ragavan = Guid.NewGuid();
        var solRing = Guid.NewGuid();
        var elves = Guid.NewGuid();
        var chandra = Guid.NewGuid();
        var gruul = Guid.NewGuid();
        var commanderOnly = Guid.NewGuid();
        var bannedLegend = Guid.NewGuid();
        var competitiveBanned = new BrawlKeys("legal", "banned", "not_legal");
        using var app = await IngestAsync(
            CardLine(wrenn, "Wrenn and Six", typeLine: "Legendary Planeswalker — Wrenn", colorIdentity: ["R", "G"], brawl: competitiveBanned),
            CardLine(ragavan, "Ragavan, Nimble Pilferer", typeLine: "Legendary Creature — Monkey Pirate", colorIdentity: ["R"], brawl: competitiveBanned),
            CardLine(solRing, "Sol Ring"),
            CardLine(elves, "Llanowar Elves", typeLine: "Creature — Elf Druid", colorIdentity: ["G"], brawl: BrawlKeys.Legal),
            CardLine(Guid.NewGuid(), "Mountain", typeLine: "Basic Land — Mountain", colorIdentity: [], brawl: BrawlKeys.Legal),
            CardLine(chandra, "Chandra Legend", typeLine: "Legendary Planeswalker — Chandra", colorIdentity: ["R"], brawl: BrawlKeys.Legal),
            CardLine(gruul, "Gruul Legend", typeLine: "Legendary Creature — Human", colorIdentity: ["R", "G"], brawl: BrawlKeys.Legal),
            CardLine(commanderOnly, "Commander Only Legend", typeLine: "Legendary Creature — Elf", colorIdentity: ["G"]),
            CardLine(bannedLegend, "Banned Legend", typeLine: "Legendary Creature — Elf", colorIdentity: ["G"], brawl: competitiveBanned));
        using var client = app.CreateClient();

        var response = await PostAsync(client, new
        {
            format = "competitive_brawl",
            decklist = new
            {
                commanders = new[] { new { name = "Wrenn and Six" } },
                entries = new object[]
                {
                    new { card = new { name = "Ragavan, Nimble Pilferer" }, quantity = 1 },
                    new { card = new { name = "Sol Ring" }, quantity = 1 },
                    new { card = new { name = "Llanowar Elves" }, quantity = 1 },
                    new { card = new { name = "Mountain" }, quantity = 5 },
                },
            },
        });

        var versions = response.GetProperty("versions");
        Assert.Equal("competitive_brawl", versions.GetProperty("format").GetString());
        Assert.Equal(AsOf, versions.GetProperty("legality_as_of").GetDateTimeOffset());
        Assert.False(versions.TryGetProperty("banlist_version", out _));
        Assert.Equal(8, response.GetProperty("original").GetProperty("deck_size").GetInt32());

        // Only Competitive Brawl commanders, the planeswalker included. Gruul
        // keeps Elves and the Mountains; Chandra keeps the Mountains only.
        var candidates = response.GetProperty("candidates").EnumerateArray().ToList();
        Assert.Equal([gruul, chandra], candidates.Select(c => c.GetProperty("card").GetProperty("id").GetGuid()));
        Assert.Equal([6, 5], candidates.Select(c => c.GetProperty("retained_count").GetInt32()));
        Assert.Equal([2, 3], candidates.Select(c => c.GetProperty("lost_count").GetInt32()));

        var lost = candidates[1].GetProperty("lost").EnumerateArray()
            .ToDictionary(l => l.GetProperty("card").GetProperty("id").GetGuid());
        Assert.Equal("banned", lost[ragavan].GetProperty("reason").GetString());
        AssertCitation(lost[ragavan].GetProperty("citation"), "legality", ragavan, "competitivebrawl banned as of 2026-10-03");
        Assert.Equal("not_legal", lost[solRing].GetProperty("reason").GetString());
        AssertCitation(lost[solRing].GetProperty("citation"), "legality", solRing, "competitivebrawl not_legal as of 2026-10-03");
        Assert.Equal("color_identity", lost[elves].GetProperty("reason").GetString());
        AssertCitation(lost[elves].GetProperty("citation"), "card", elves, "Llanowar Elves");
    }

    [Fact]
    public async Task Commander_bans_cite_the_ban_event_and_not_legal_cites_the_card()
    {
        var original = Guid.NewGuid();
        var crypt = Guid.NewGuid();
        var unplayable = Guid.NewGuid();
        var lutri = Guid.NewGuid();
        using var app = await IngestWithEventsAsync(
            """
            [{ "card": "Mana Crypt", "action": "ban", "scope": "all" },
             { "card": "Lutri, the Spellchaser", "action": "ban", "scope": "companion" }]
            """,
            CardLine(original, "Original", typeLine: "Legendary Creature — Elf", colorIdentity: ["U", "R"]),
            CardLine(Guid.NewGuid(), "Other", typeLine: "Legendary Creature — Elf", colorIdentity: ["U", "R"]),
            CardLine(crypt, "Mana Crypt"),
            CardLine(unplayable, "Shahrazad", commanderLegality: "not_legal"),
            CardLine(lutri, "Lutri, the Spellchaser", typeLine: "Creature — Elemental Otter", colorIdentity: ["U", "R"], keywords: ["Companion"]),
            CardLine(Guid.NewGuid(), "Island", typeLine: "Basic Land — Island"));
        using var client = app.CreateClient();

        var response = await PostAsync(client, new
        {
            decklist = new
            {
                commanders = new[] { new { name = "Original" } },
                companion = new { name = "Lutri, the Spellchaser" },
                entries = new object[]
                {
                    new { card = new { name = "Mana Crypt" }, quantity = 1 },
                    new { card = new { name = "Shahrazad" }, quantity = 1 },
                    new { card = new { name = "Island" }, quantity = 4 },
                },
            },
        });

        Assert.Equal("commander", response.GetProperty("versions").GetProperty("format").GetString());
        var candidate = Assert.Single(response.GetProperty("candidates").EnumerateArray());
        Assert.Equal(6, response.GetProperty("original").GetProperty("deck_size").GetInt32());
        Assert.Equal((4, 2), (candidate.GetProperty("retained_count").GetInt32(), candidate.GetProperty("lost_count").GetInt32()));
        var lost = candidate.GetProperty("lost").EnumerateArray().ToDictionary(l => l.GetProperty("card").GetProperty("id").GetGuid());
        Assert.Equal("banned", lost[crypt].GetProperty("reason").GetString());
        Assert.Equal("ban_event", lost[crypt].GetProperty("citation").GetProperty("type").GetString());
        Assert.Equal("not_legal", lost[unplayable].GetProperty("reason").GetString());
        AssertCitation(lost[unplayable].GetProperty("citation"), "card", unplayable, "Shahrazad");

        // A companion-scope ban reaches the companion seat only.
        var companion = candidate.GetProperty("companion");
        Assert.Equal(lutri, companion.GetProperty("card").GetProperty("id").GetGuid());
        Assert.False(companion.GetProperty("retained").GetBoolean());
        Assert.Equal("banned", companion.GetProperty("reason").GetString());
        var ban = companion.GetProperty("citation");
        Assert.Equal("ban_event", ban.GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/ban-events/{ban.GetProperty("id").GetString()}")).StatusCode);
    }

    [Fact]
    public async Task A_brawl_companion_is_judged_but_not_counted_and_ties_break_by_popularity_rank()
    {
        var popular = Guid.NewGuid();
        var lessPopular = Guid.NewGuid();
        var red = Guid.NewGuid();
        var jegantha = Guid.NewGuid();
        using var app = await IngestAsync(
            CardLine(Guid.NewGuid(), "Original", typeLine: "Legendary Creature — Elf", colorIdentity: ["R", "G"], brawl: BrawlKeys.Legal),
            CardLine(lessPopular, "Less Popular", typeLine: "Legendary Creature — Elf", colorIdentity: ["R", "G"], edhrecRank: 90, brawl: BrawlKeys.Legal),
            CardLine(popular, "Popular", typeLine: "Legendary Creature — Elf", colorIdentity: ["R", "G"], edhrecRank: 30, brawl: BrawlKeys.Legal),
            CardLine(red, "Red Legend", typeLine: "Legendary Creature — Goblin", colorIdentity: ["R"], edhrecRank: 1, brawl: BrawlKeys.Legal),
            CardLine(jegantha, "Jegantha, the Wellspring", typeLine: "Creature — Elemental Elk", colorIdentity: ["R", "G"],
                keywords: ["Companion"], brawl: BrawlKeys.Legal),
            CardLine(Guid.NewGuid(), "Mountain", typeLine: "Basic Land — Mountain", brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();
        var decklist = new
        {
            commanders = new[] { new { name = "Original" } },
            companion = new { name = "Jegantha, the Wellspring" },
            entries = new[] { new { card = new { name = "Mountain" }, quantity = 3 } },
        };

        var response = await PostAsync(client, new { format = "brawl", decklist });

        // All three retain the deck. Distance puts Red last; the brawl
        // popularity order puts Popular before Less Popular.
        var candidates = response.GetProperty("candidates").EnumerateArray().ToList();
        Assert.Equal([popular, lessPopular, red], candidates.Select(c => c.GetProperty("card").GetProperty("id").GetGuid()));
        Assert.True(candidates[0].GetProperty("popularity_rank").GetInt32() < candidates[1].GetProperty("popularity_rank").GetInt32());
        Assert.All(candidates, c => Assert.Equal((3, 0), (c.GetProperty("retained_count").GetInt32(), c.GetProperty("lost_count").GetInt32())));
        Assert.Equal(3, response.GetProperty("original").GetProperty("deck_size").GetInt32());

        var kept = candidates[0].GetProperty("companion");
        Assert.True(kept.GetProperty("retained").GetBoolean());
        Assert.False(kept.TryGetProperty("reason", out _));
        Assert.False(kept.TryGetProperty("citation", out _));
        var escaped = candidates[2].GetProperty("companion");
        Assert.False(escaped.GetProperty("retained").GetBoolean());
        Assert.Equal("color_identity", escaped.GetProperty("reason").GetString());
        AssertCitation(escaped.GetProperty("citation"), "card", jegantha, "Jegantha, the Wellspring");

        // Without a companion no candidate carries the entry.
        var bare = await PostAsync(client, new { format = "brawl", decklist = new { decklist.commanders, decklist.entries } });
        Assert.All(bare.GetProperty("candidates").EnumerateArray(), c => Assert.False(c.TryGetProperty("companion", out _)));
    }

    [Fact]
    public async Task A_brawl_format_takes_no_banlist_pin_and_an_unknown_format_is_a_problem()
    {
        using var app = await IngestAsync(
            CardLine(Guid.NewGuid(), "Original", typeLine: "Legendary Creature — Elf", brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();
        var decklist = new { commanders = new[] { new { name = "Original" } } };

        var pinned = await client.PostAsJsonAsync("/commander-alternatives", new { format = "brawl", decklist, banlist_version = "2024-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, pinned.StatusCode);
        Assert.Equal("banlist_version", (await Problem(pinned)).GetProperty("parameter").GetString());

        var unknown = await client.PostAsJsonAsync("/commander-alternatives", new { format = "historic_brawl", decklist });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Ranks_by_retained_count_then_closeness_then_edhrec_rank_with_the_partition_cited()
    {
        var connectionString = await NewDatabaseAsync();
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();
        var original = Commander("Original", ["G", "W"]);
        var forest = Card("Forest", ["G"], copyLimit: null);
        var plains = Card("Plains", ["W"], copyLimit: null);
        var elf = Card("Llanowar Elves", ["G"]);
        var crypt = Card("Mana Crypt", [], banned: true);
        var ring = Card("Sol Ring", []);
        // Candidates: the same identity, a strict superset, an overlapping
        // pair, a mono-color, and a colorless one.
        var twin = Commander("Twin", ["G", "W"], rank: 300);
        var naya = Commander("Naya", ["R", "G", "W"], rank: 50);
        var bant = Commander("Bant", ["G", "W", "U"], rank: 10);
        var green = Commander("Green", ["G"], rank: 1);
        var colorless = Commander("Colorless", []);
        await SeedAsync(connectionString, original, forest, plains, elf, crypt, ring, twin, naya, bant, green, colorless);

        var response = await PostAsync(client, new
        {
            decklist = new
            {
                commanders = new[] { new { name = "Original" } },
                entries = new object[]
                {
                    new { card = new { name = "Forest" }, quantity = 10 },
                    new { card = new { name = "Plains" }, quantity = 5 },
                    new { card = new { oracle_id = elf.OracleId }, quantity = 1 },
                    new { card = new { name = "Mana Crypt" }, quantity = 1 },
                    new { card = new { name = "Sol Ring" }, quantity = 1 },
                },
            },
        });

        Assert.Equal(Version, response.GetProperty("versions").GetProperty("banlist_version").GetString());
        var originalBlock = response.GetProperty("original");
        Assert.Equal(original.OracleId, Assert.Single(originalBlock.GetProperty("commanders").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(["W", "G"], Letters(originalBlock.GetProperty("color_identity")));
        Assert.Equal(18, originalBlock.GetProperty("deck_size").GetInt32());

        var candidates = response.GetProperty("candidates").EnumerateArray().ToList();
        // Twin, Bant and Naya all retain 17; Twin is at distance 0, then
        // Bant (rank 10) beats Naya (rank 50) at distance 1. Green retains
        // 12, Colorless 1. The original commander never appears.
        Assert.Equal(
            [twin.OracleId, bant.OracleId, naya.OracleId, green.OracleId, colorless.OracleId],
            candidates.Select(c => c.GetProperty("card").GetProperty("id").GetGuid()));
        Assert.Equal([17, 17, 17, 12, 1], candidates.Select(c => c.GetProperty("retained_count").GetInt32()));
        Assert.Equal([1, 1, 1, 6, 17], candidates.Select(c => c.GetProperty("lost_count").GetInt32()));
        Assert.Equal([0, 1, 1, 1, 2], candidates.Select(c => c.GetProperty("color_identity_distance").GetInt32()));
        Assert.All(candidates, c => Assert.False(c.TryGetProperty("available", out _)));

        var greenLost = candidates[3].GetProperty("lost").EnumerateArray().ToList();
        var lostPlains = Assert.Single(greenLost, l => l.GetProperty("card").GetProperty("id").GetGuid() == plains.OracleId);
        Assert.Equal("color_identity", lostPlains.GetProperty("reason").GetString());
        Assert.Equal(5, lostPlains.GetProperty("quantity").GetInt32());
        Assert.Equal("card", lostPlains.GetProperty("citation").GetProperty("type").GetString());
        Assert.Equal(plains.OracleId, lostPlains.GetProperty("citation").GetProperty("id").GetGuid());

        var lostCrypt = Assert.Single(greenLost, l => l.GetProperty("card").GetProperty("id").GetGuid() == crypt.OracleId);
        Assert.Equal("banned", lostCrypt.GetProperty("reason").GetString());
        var ban = lostCrypt.GetProperty("citation");
        Assert.Equal("ban_event", ban.GetProperty("type").GetString());
        Assert.Contains("Mana Crypt banned", ban.GetProperty("label").GetString());
        var fetched = await client.GetAsync($"/ban-events/{ban.GetProperty("id").GetString()}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        var retained = candidates[3].GetProperty("retained").EnumerateArray().ToList();
        Assert.Equal(
            [forest.OracleId, elf.OracleId, ring.OracleId],
            retained.Select(r => r.GetProperty("card").GetProperty("id").GetGuid()));
        Assert.Equal(10, retained[0].GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task The_pool_is_commander_legal_cards_only_and_scope_hard_filters_to_owned()
    {
        var connectionString = await NewDatabaseAsync();
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();
        var original = Commander("Original", ["U"]);
        var island = Card("Island", ["U"], copyLimit: null);
        var owned = Commander("Owned", ["U"], owned: 2);
        var unowned = Commander("Unowned", ["U"]);
        var bannedAsCommander = Commander("Banned As Commander", ["U"], commanderLegal: false);
        var notACommander = Card("Counterspell", ["U"]);
        var stale = Commander("Stale", ["U"], stale: true);
        await SeedAsync(connectionString, original, island, owned, unowned, bannedAsCommander, notACommander, stale);
        var decklist = new
        {
            commanders = new[] { new { name = "Original" } },
            entries = new[] { new { card = new { name = "Island" }, quantity = 3 } },
        };

        var open = await PostAsync(client, new { decklist });
        Assert.Equal([owned.OracleId, unowned.OracleId], Ids(open));

        var scoped = await PostAsync(client, new { decklist, collection_scope = true });
        var hit = Assert.Single(scoped.GetProperty("candidates").EnumerateArray());
        Assert.Equal(owned.OracleId, hit.GetProperty("card").GetProperty("id").GetGuid());
        Assert.Equal(1, hit.GetProperty("available").GetInt32());
    }

    [Fact]
    public async Task Limit_defaults_to_twenty_and_is_capped()
    {
        var connectionString = await NewDatabaseAsync();
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();
        await SeedAsync(connectionString,
            [Commander("Original", ["R"]), .. Enumerable.Range(0, 230).Select(i => Commander($"Candidate {i}", ["R"], rank: i))]);
        var decklist = new { commanders = new[] { new { name = "Original" } } };

        Assert.Equal(20, (await PostAsync(client, new { decklist })).GetProperty("candidates").GetArrayLength());
        Assert.Equal(3, (await PostAsync(client, new { decklist, limit = 3 })).GetProperty("candidates").GetArrayLength());
        Assert.Equal(200, (await PostAsync(client, new { decklist, limit = 999 })).GetProperty("candidates").GetArrayLength());

        var below = await client.PostAsJsonAsync("/commander-alternatives", new { decklist, limit = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, below.StatusCode);
        Assert.Equal("application/problem+json", below.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Bad_requests_are_problems_never_fallbacks()
    {
        var connectionString = await NewDatabaseAsync();
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();
        await SeedAsync(connectionString, Commander("Original", ["R"]), Commander("Other", ["R"]));
        var decklist = new { commanders = new[] { new { name = "Original" } } };

        var unknownPin = await client.PostAsJsonAsync("/commander-alternatives", new { decklist, banlist_version = "1999-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, unknownPin.StatusCode);
        Assert.Equal("application/problem+json", unknownPin.Content.Headers.ContentType?.MediaType);
        Assert.Equal("banlist_version", (await Problem(unknownPin)).GetProperty("parameter").GetString());

        var pinBeforeDecklist = await client.PostAsJsonAsync("/commander-alternatives", new
        {
            decklist = new { commanders = new[] { new { name = "No Such Card" } } },
            banlist_version = "1999-01-01",
        });
        Assert.Equal("banlist_version", (await Problem(pinBeforeDecklist)).GetProperty("parameter").GetString());

        var unresolved = await client.PostAsJsonAsync("/commander-alternatives", new
        {
            decklist = new
            {
                commanders = new[] { new { name = "Original" } },
                entries = new[] { new { card = new { name = "No Such Card" }, quantity = 1 } },
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, unresolved.StatusCode);
        var name = Assert.Single((await Problem(unresolved)).GetProperty("unresolved_names").EnumerateArray());
        Assert.Equal("No Such Card", name.GetProperty("name").GetString());

        var noCommander = await client.PostAsJsonAsync("/commander-alternatives", new { decklist = new { } });
        Assert.Equal(HttpStatusCode.BadRequest, noCommander.StatusCode);
        Assert.Contains("commander", (await Problem(noCommander)).GetProperty("detail").GetString());

        var pinned = await PostAsync(client, new { decklist, banlist_version = Version });
        Assert.Equal(Version, pinned.GetProperty("versions").GetProperty("banlist_version").GetString());
    }

    private static void AssertCitation(JsonElement citation, string type, Guid id, string label)
    {
        Assert.Equal(type, citation.GetProperty("type").GetString());
        Assert.Equal(id.ToString(), citation.GetProperty("id").GetString());
        Assert.Equal(label, citation.GetProperty("label").GetString());
    }

    /// <summary>Ingests the fixture lines through the real pipeline, with
    /// the given banlist events on one version dated 2024-01-01.</summary>
    private Task<ArchiveApp> IngestAsync(params string[] lines) => IngestWithEventsAsync("[]", lines);

    private async Task<ArchiveApp> IngestWithEventsAsync(string events, params string[] lines)
    {
        var eventsPath = Path.Combine(_eventsDir, "banlist-events.json");
        await File.WriteAllTextAsync(eventsPath, $$"""
            [{ "effective_date": "2024-01-01", "date_precision": "day",
               "announcement_url": "https://example.org/2024", "events": {{events}} }]
            """);
        var source = new FakeBulkSource();
        var app = new ArchiveApp(
            autoMigrate: true,
            await NewDatabaseAsync(),
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

    private Task<string> NewDatabaseAsync() => postgres.CreateDatabaseAsync($"commander_alternatives_{Guid.NewGuid():N}");

    private static Guid[] Ids(JsonElement response)
        => [.. response.GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("card").GetProperty("id").GetGuid())];

    private static string[] Letters(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    private static async Task<JsonElement> PostAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/commander-alternatives", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> Problem(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static Seed Commander(
        string name, string[] colorIdentity, int? rank = null, int owned = 0, bool commanderLegal = true, bool stale = false)
    {
        var seed = Card(name, colorIdentity, owned: owned, stale: stale);
        seed.Card.TypeLine = "Legendary Creature — Human";
        seed.Card.Supertypes = ["Legendary"];
        seed.Card.CardTypes = ["Creature"];
        seed.Card.CanBeCommander = true;
        seed.Card.EdhrecRank = rank;
        return seed with { CommanderLegal = commanderLegal };
    }

    private static Seed Card(
        string name, string[] colorIdentity, int? copyLimit = 1, bool banned = false, int owned = 0, bool stale = false)
    {
        var card = TestCards.Minimal(Guid.NewGuid(), colorIdentity);
        card.Name = name;
        card.CopyLimit = copyLimit;
        card.Stale = stale;
        return new Seed(card, owned, banned, CommanderLegal: false);
    }

    private sealed record Seed(Card Card, int Owned, bool Banned, bool CommanderLegal)
    {
        public Guid OracleId => Card.OracleId;
    }

    /// <summary>One banlist version. Every card is legal in main unless
    /// banned (a ban event backs the citation); commander legality follows
    /// the seed (not eligible for non-commanders); owned rows as given.</summary>
    private static async Task SeedAsync(string connectionString, params Seed[] seeds)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        var version = new BanlistVersion
        {
            EffectiveDate = DateOnly.Parse(Version),
            DatePrecision = DatePrecision.Day,
            AnnouncementUrl = "https://example.org/2025",
        };
        db.BanlistVersions.Add(version);
        db.Cards.AddRange(seeds.Select(s => s.Card));
        await db.SaveChangesAsync();

        foreach (var seed in seeds)
        {
            BanEvent? ban = null;
            if (seed.Banned)
            {
                ban = new BanEvent
                {
                    BanlistVersionId = version.Id,
                    CardId = seed.Card.Id,
                    Action = BanAction.Ban,
                    Scope = BanScope.All,
                };
                db.BanEvents.Add(ban);
                await db.SaveChangesAsync();
            }

            db.Legalities.Add(new Legality
            {
                CardId = seed.Card.Id,
                BanlistVersionId = version.Id,
                Role = Role.Main,
                Status = seed.Banned ? LegalityStatus.Banned : LegalityStatus.Legal,
                Reason = ban?.Id,
            });
            db.Legalities.Add(new Legality
            {
                CardId = seed.Card.Id,
                BanlistVersionId = version.Id,
                Role = Role.Commander,
                Status = seed.CommanderLegal ? LegalityStatus.Legal
                    : seed.Card.CanBeCommander ? LegalityStatus.Banned : LegalityStatus.NotEligible,
                Reason = ban?.Id,
            });
            db.Legalities.Add(new Legality
            {
                CardId = seed.Card.Id,
                BanlistVersionId = version.Id,
                Role = Role.Companion,
                Status = LegalityStatus.NotEligible,
            });
            if (seed.Owned > 0)
            {
                db.CollectionCards.Add(new CollectionCard { OracleId = seed.OracleId, Quantity = seed.Owned });
            }
        }

        await db.SaveChangesAsync();
    }
}
