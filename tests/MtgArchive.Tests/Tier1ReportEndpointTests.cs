using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Data;
using MtgArchive.Ingest;
using MtgArchive.Rules;

namespace MtgArchive.Tests;

/// <summary>
/// POST /tier1-report: the wire report over a seeded corpus —
/// snake_case shape, the empty <c>versions</c> echo, upstream row semantics
/// (either rows, Phyrexian notes, flagged commander, availability cutoff),
/// caller-defined categories, partial lists, and the problem+json refusals.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Tier1ReportEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Report_over_a_seeded_deck_preserves_the_upstream_row_semantics()
    {
        var connectionString = await SeedCorpusAsync();
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();

        var response = await client.PostAsync("/tier1-report", Fixture("report-request.json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("""{"format":"commander"}""", report.GetProperty("versions").GetRawText());
        var input = report.GetProperty("input");
        Assert.Equal(99, input.GetProperty("deck_size").GetInt32());
        Assert.Equal(4, input.GetProperty("max_turn").GetInt32());
        Assert.Equal("Test Commander",
            Assert.Single(input.GetProperty("commanders").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(9, report.GetProperty("assumptions").GetArrayLength());

        // Category draws: two caller categories × turns 0..4; k defaults to 1.
        var draws = report.GetProperty("category_draws").EnumerateArray().ToList();
        Assert.Equal(10, draws.Count);
        Assert.All(draws, d => Assert.Equal(1, d.GetProperty("k").GetInt32()));
        var landsOpener = draws.Single(d => d.GetProperty("category").GetString() == "lands"
            && d.GetProperty("turn").GetInt32() == 0);
        Assert.Equal(0.9706945754289187, landsOpener.GetProperty("probability").GetDouble(), 1e-12);

        // Source table: the signet (mana value 2) is available from turn 3.
        var sources = report.GetProperty("mana_sources").GetProperty("sources").EnumerateArray().ToList();
        var signet = sources.Single(s => s.GetProperty("name").GetString() == "Test Signet");
        Assert.Equal(3, signet.GetProperty("availability_turn").GetInt32());
        Assert.Equal("dual", signet.GetProperty("spread").GetString());
        var green = report.GetProperty("mana_sources").GetProperty("by_color").EnumerateArray()
            .Single(c => c.GetProperty("color").GetString() == "G");
        Assert.Equal(33, green.GetProperty("count").GetInt32());
        Assert.Equal(30, green.GetProperty("mono").GetInt32());

        var cells = report.GetProperty("demand_vs_supply").EnumerateArray().ToList();

        // The commander ({2}{G}{G/P}) is flagged; the Phyrexian pip is a
        // note, not a fraction; supply at turn 4 includes the signet.
        var commander = cells.Single(c => c.GetProperty("is_commander").GetBoolean());
        Assert.Equal(1, commander.GetProperty("pips").GetInt32());
        Assert.Equal(4, commander.GetProperty("turn").GetInt32());
        Assert.Equal(33, commander.GetProperty("supply").GetInt32());
        Assert.Contains("{G/P}", Assert.Single(commander.GetProperty("notes").EnumerateArray()).GetString());

        // The bear ({1}{G}, turn 2) sees no signet: availability 3 > 2.
        var bear = cells.Single(c => c.GetProperty("name").GetString() == "Test Bear");
        Assert.Equal(32, bear.GetProperty("supply").GetInt32());
        Assert.False(bear.GetProperty("either").GetBoolean());
        Assert.InRange(bear.GetProperty("casting_probability").GetDouble(), 0, 1);

        // The hybrid ({G/W}) is its own either row over the union of sources.
        var hybrid = cells.Single(c => c.GetProperty("name").GetString() == "Test Hybrid");
        Assert.True(hybrid.GetProperty("either").GetBoolean());
        Assert.Equal(["G", "W"], hybrid.GetProperty("colors").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(38, hybrid.GetProperty("supply").GetInt32());

        Assert.Equal(38, report.GetProperty("land_count").GetInt32());
        Assert.Equal([1, 2, 3, 4],
            report.GetProperty("land_drops").EnumerateArray().Select(l => l.GetProperty("turn").GetInt32()));
    }

    [Fact]
    public async Task Partial_list_without_commander_gets_a_complete_report_with_default_max_turn()
    {
        var connectionString = await SeedCorpusAsync();
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/tier1-report", new
        {
            decklist = new
            {
                entries = new[]
                {
                    new { card = new { name = "Test Forest" }, quantity = 12 },
                    new { card = new { name = "Test Bear" }, quantity = 8 },
                },
            },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(20, report.GetProperty("input").GetProperty("deck_size").GetInt32());
        Assert.Equal(10, report.GetProperty("input").GetProperty("max_turn").GetInt32());
        Assert.Empty(report.GetProperty("input").GetProperty("commanders").EnumerateArray());
        Assert.Empty(report.GetProperty("category_draws").EnumerateArray());
        Assert.Equal(10, report.GetProperty("land_drops").GetArrayLength());
        Assert.Single(report.GetProperty("demand_vs_supply").EnumerateArray());
    }

    [Fact]
    public async Task Unresolvable_names_are_a_problem_with_the_unresolved_names_extension()
    {
        var connectionString = await SeedCorpusAsync();
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();
        var fabricated = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/tier1-report", new
        {
            decklist = new
            {
                commanders = new[] { new { name = "No Such Commander" } },
                entries = new object[]
                {
                    new { card = new { name = "Test Forest" }, quantity = 12 },
                    new { card = new { oracle_id = fabricated }, quantity = 1 },
                },
            },
            categories = new[]
            {
                new { name = "typos", cards = new[] { new { name = "Test Forset" } } },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var unresolved = problem.GetProperty("unresolved_names").EnumerateArray().ToList();
        Assert.Equal(3, unresolved.Count);
        var commander = unresolved.Single(u => u.GetProperty("is_commander").GetBoolean());
        Assert.Equal("No Such Commander", commander.GetProperty("name").GetString());
        Assert.Contains(unresolved, u => u.TryGetProperty("oracle_id", out var id) && id.GetGuid() == fabricated);
        Assert.Contains(unresolved, u => u.TryGetProperty("name", out var n) && n.GetString() == "Test Forset");
    }

    [Fact]
    public async Task Bad_quantity_and_bad_max_turn_are_problems()
    {
        var connectionString = await SeedCorpusAsync();
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();

        var quantity = await client.PostAsJsonAsync("/tier1-report", new
        {
            decklist = new { entries = new[] { new { card = new { name = "Test Forest" }, quantity = 0 } } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, quantity.StatusCode);
        Assert.Equal("application/problem+json", quantity.Content.Headers.ContentType?.MediaType);

        var turn = await client.PostAsJsonAsync("/tier1-report", new
        {
            decklist = new { entries = new[] { new { card = new { name = "Test Forest" }, quantity = 1 } } },
            max_turn = 0,
        });
        Assert.Equal(HttpStatusCode.BadRequest, turn.StatusCode);
        var problem = await turn.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("max_turn", problem.GetProperty("parameter").GetString());
    }

    [Fact]
    public async Task Commander_listed_in_the_library_is_a_problem_not_a_crash()
    {
        var connectionString = await SeedCorpusAsync();
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/tier1-report", new
        {
            decklist = new
            {
                commanders = new[] { new { name = "Test Commander" } },
                entries = new[]
                {
                    new { card = new { name = "Test Forest" }, quantity = 12 },
                    new { card = new { name = "Test Commander" }, quantity = 1 },
                },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("urn:mtg-archive:problem:invalid-decklist", problem.GetProperty("type").GetString());
    }

    // End to end: ingest derives the opponent-dependent
    // facet from the real Oracle text, and the report excludes those sources.
    [Fact]
    public async Task Opponent_dependent_sources_are_excluded_and_listed()
    {
        const string treasure = " (It's an artifact with \"{T}, Sacrifice this token: Add one mana of any color.\")";
        string[] fiveColors = ["B", "G", "R", "U", "W"];
        var connectionString = await postgres.CreateDatabaseAsync($"tier1_{Guid.NewGuid():N}");
        var source = new FakeBulkSource
        {
            OracleCards = IngestHarness.Lines(
                IngestHarness.CardLine(Guid.NewGuid(), "Kenrith, the Returned King", typeLine: "Legendary Creature — Human Noble",
                    manaCost: "{4}{W}", cmc: 5, colorIdentity: fiveColors, oracleText: "{2}{W}: Target player gains 5 life."),
                IngestHarness.CardLine(Guid.NewGuid(), "Smothering Tithe", typeLine: "Enchantment", manaCost: "{3}{W}", cmc: 4,
                    producedMana: fiveColors, colorIdentity: ["W"],
                    oracleText: "Whenever an opponent draws a card, that player may pay {2}. If the player doesn't, you create a Treasure token." + treasure),
                IngestHarness.CardLine(Guid.NewGuid(), "Dockside Extortionist", typeLine: "Creature — Goblin Pirate", manaCost: "{1}{R}", cmc: 2,
                    producedMana: fiveColors, colorIdentity: ["R"],
                    oracleText: "When this creature enters, create X Treasure tokens, where X is the number of artifacts and enchantments your opponents control." + treasure),
                IngestHarness.CardLine(Guid.NewGuid(), "Revel in Riches", typeLine: "Enchantment", manaCost: "{4}{B}", cmc: 5,
                    producedMana: fiveColors, colorIdentity: ["B"],
                    oracleText: "Whenever a creature an opponent controls dies, create a Treasure token." + treasure
                        + "\nAt the beginning of your upkeep, if you control ten or more Treasures, you win the game."),
                IngestHarness.CardLine(Guid.NewGuid(), "An Offer You Can't Refuse", typeLine: "Instant", manaCost: "{U}", cmc: 1,
                    producedMana: fiveColors, colorIdentity: ["U"],
                    oracleText: "Counter target noncreature spell. Its controller creates two Treasure tokens." + treasure),
                IngestHarness.CardLine(Guid.NewGuid(), "Land Tax", typeLine: "Enchantment", manaCost: "{W}", cmc: 1, colorIdentity: ["W"],
                    oracleText: "At the beginning of your upkeep, if an opponent controls more lands than you, you may search your library for up to three basic land cards, reveal them, put them into your hand, then shuffle."),
                IngestHarness.CardLine(Guid.NewGuid(), "Sol Ring", oracleText: "{T}: Add {C}{C}.", producedMana: ["C"]),
                IngestHarness.CardLine(Guid.NewGuid(), "Island", typeLine: "Basic Land — Island", manaCost: "", cmc: 0,
                    producedMana: ["U"], colorIdentity: ["U"], oracleText: "({T}: Add {U}.)")),
        };
        using var app = new ArchiveApp(
            autoMigrate: true,
            connectionString,
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(source);
                services.RemoveAll<ICrSource>();
                services.AddSingleton<ICrSource>(new FakeCrSource());
            });
        using var client = app.CreateClient();
        await IngestHarness.WaitForCompletionAsync(client, await IngestHarness.TriggerAsync(client), "succeeded");

        var response = await client.PostAsJsonAsync("/tier1-report", new
        {
            decklist = new
            {
                commanders = new[] { new { name = "Kenrith, the Returned King" } },
                entries = new[]
                {
                    new { card = new { name = "Smothering Tithe" }, quantity = 1 },
                    new { card = new { name = "Dockside Extortionist" }, quantity = 1 },
                    new { card = new { name = "Revel in Riches" }, quantity = 1 },
                    new { card = new { name = "Land Tax" }, quantity = 1 },
                    new { card = new { name = "An Offer You Can't Refuse" }, quantity = 1 },
                    new { card = new { name = "Sol Ring" }, quantity = 1 },
                    new { card = new { name = "Island" }, quantity = 10 },
                },
            },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var manaSources = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mana_sources");
        var counts = manaSources.GetProperty("by_color").EnumerateArray()
            .ToDictionary(c => c.GetProperty("color").GetString()!, c => c.GetProperty("count").GetInt32());
        Assert.Equal(new Dictionary<string, int> { ["W"] = 0, ["U"] = 10, ["B"] = 0, ["R"] = 0, ["G"] = 0, ["C"] = 1 }, counts);
        Assert.Equal(["Island", "Sol Ring"], manaSources.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("name").GetString()).Order());
        var excluded = manaSources.GetProperty("excluded").EnumerateArray().ToList();
        Assert.Equal(
            ["An Offer You Can't Refuse", "Dockside Extortionist", "Land Tax", "Revel in Riches", "Smothering Tithe"],
            excluded.Select(e => e.GetProperty("name").GetString()).Order());
        Assert.Equal(
            "token_to_another_player",
            Assert.Single(excluded, e => e.GetProperty("name").GetString() == "An Offer You Can't Refuse").GetProperty("reason").GetString());
        Assert.All(excluded.Where(e => e.GetProperty("name").GetString() != "An Offer You Can't Refuse"), e => Assert.Equal("opponent_dependent", e.GetProperty("reason").GetString()));
        Assert.All(excluded, e =>
        {
            Assert.Equal(1, e.GetProperty("quantity").GetInt32());
            Assert.NotEqual(Guid.Empty, e.GetProperty("oracle_id").GetGuid());
        });
    }

    /// <summary>
    /// The format fixes the draw rule (ADR 0012): a Brawl game is on the
    /// play, so a Brawl report sees 7 + max(0, T − 1) cards by turn T, and
    /// a Commander report still sees 7 + T. The lands category at turn T in
    /// Brawl is the Commander row one turn earlier, and turn 1 is the opener.
    /// </summary>
    [Fact]
    public async Task A_brawl_report_counts_the_cards_seen_on_the_play_and_echoes_its_format()
    {
        var connectionString = await SeedCorpusAsync();
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();

        var commander = await ReportAsync(client, format: null);
        var brawl = await ReportAsync(client, format: "brawl");

        Assert.Equal("commander", commander.GetProperty("versions").GetProperty("format").GetString());
        Assert.Equal("brawl", brawl.GetProperty("versions").GetProperty("format").GetString());

        static double Lands(JsonElement report, int turn) => report.GetProperty("category_draws").EnumerateArray()
            .Single(d => d.GetProperty("category").GetString() == "lands" && d.GetProperty("turn").GetInt32() == turn)
            .GetProperty("probability").GetDouble();

        const double opener = 0.9706945754289187;
        Assert.Equal(opener, Lands(commander, 0), 1e-12);
        Assert.Equal(opener, Lands(brawl, 0), 1e-12);
        Assert.Equal(opener, Lands(brawl, 1), 1e-12);
        Assert.NotEqual(Lands(commander, 1), Lands(brawl, 1), 1e-12);
        foreach (var turn in Enumerable.Range(1, 4))
        {
            Assert.Equal(Lands(commander, turn - 1), Lands(brawl, turn), 1e-12);
        }

        // Turn 1's land drop: at least 1 of 38 lands among 7 cards on the play, 8 with the draw.
        static double LandDrop(JsonElement report, int turn) => report.GetProperty("land_drops").EnumerateArray()
            .Single(l => l.GetProperty("turn").GetInt32() == turn).GetProperty("probability").GetDouble();
        Assert.Equal(opener, LandDrop(brawl, 1), 1e-12);
        Assert.Equal(Lands(commander, 1), LandDrop(commander, 1), 1e-12);

        static IEnumerable<string> Assumptions(JsonElement report) => report.GetProperty("assumptions").EnumerateArray().Select(a => a.GetString()!);
        Assert.Contains(Assumptions(commander), a => a.Contains("7 + T", StringComparison.Ordinal) && a.Contains("CR 103.8c", StringComparison.Ordinal));
        Assert.Contains(Assumptions(brawl), a => a.Contains("7 + max(0, T − 1)", StringComparison.Ordinal) && a.Contains("CR 103.8a", StringComparison.Ordinal));
    }

    /// <summary>L, the most seven-card hands the mulligan can look at,
    /// comes from the format's free mulligans: 3 in Competitive Brawl,
    /// which has none, and 4 in the other formats.</summary>
    [Theory]
    [InlineData(null, 4)]
    [InlineData("commander", 4)]
    [InlineData("brawl", 4)]
    [InlineData("competitive_brawl", 3)]
    [InlineData("standard_brawl", 4)]
    public async Task Mulligan_looks_come_from_the_formats_free_mulligans(string? format, int looks)
    {
        var connectionString = await SeedCorpusAsync();
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();

        var report = await ReportAsync(client, format);

        Assert.Equal(looks, report.GetProperty("mulligan_looks").GetInt32());
        Assert.Equal(format ?? "commander", report.GetProperty("versions").GetProperty("format").GetString());
    }

    [Fact]
    public async Task An_unknown_format_is_a_problem_that_names_it()
    {
        var connectionString = await SeedCorpusAsync();
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();

        var request = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "tier1", "report-request.json")))!;
        request["format"] = "historic_brawl";
        var response = await client.PostAsJsonAsync("/tier1-report", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("urn:mtg-archive:problem:unknown-format", problem.GetProperty("type").GetString());
        Assert.Equal("historic_brawl", problem.GetProperty("value").GetString());
    }

    /// <summary>The fixture request, in the format given (none when null).</summary>
    private static async Task<JsonElement> ReportAsync(HttpClient client, string? format)
    {
        var request = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "tier1", "report-request.json")))!;
        if (format is not null)
        {
            request["format"] = format;
        }

        var response = await client.PostAsJsonAsync("/tier1-report", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static StringContent Fixture(string file)
        => new(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "tier1", file)),
            System.Text.Encoding.UTF8,
            "application/json");

    /// <summary>A fresh database with the fixture's cards and their source
    /// profiles: 30 Forest, 6 Plains, 2 Savannah (G/W dual), a G/W signet,
    /// a {G/W} hybrid spell, a {1}{G} bear, and a {2}{G}{G/P} commander.</summary>
    private async Task<string> SeedCorpusAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync($"tier1_{Guid.NewGuid():N}");
        await using var db = PostgresFixture.CreateContext(connectionString);
        await db.Database.MigrateAsync();

        var forest = Land("Test Forest", "Basic Land — Forest", basic: true, "Forest");
        var plains = Land("Test Plains", "Basic Land — Plains", basic: true, "Plains");
        var savannah = Land("Test Savannah", "Land — Forest Plains", basic: false, "Forest", "Plains");
        var signet = Spell("Test Signet", "Artifact", "{2}", 2);
        var hybrid = Spell("Test Hybrid", "Creature — Elf", "{G/W}", 1);
        var bear = Spell("Test Bear", "Creature — Bear", "{1}{G}", 2);
        var commander = Spell("Test Commander", "Legendary Creature — Elf", "{2}{G}{G/P}", 4);
        commander.ColorIdentity = ["G", "W"];
        db.Cards.AddRange(forest, plains, savannah, signet, hybrid, bear, commander);
        await db.SaveChangesAsync();

        db.SourceProfiles.AddRange(
            new SourceProfile { CardId = forest.Id, ProducedColors = ["G"] },
            new SourceProfile { CardId = plains.Id, ProducedColors = ["W"] },
            new SourceProfile { CardId = savannah.Id, ProducedColors = ["G", "W"] },
            new SourceProfile { CardId = signet.Id, ProducedColors = ["G", "W"] });
        await db.SaveChangesAsync();
        return connectionString;
    }

    private static Card Land(string name, string typeLine, bool basic, params string[] subtypes)
    {
        var card = TestCards.Minimal(Guid.NewGuid());
        card.Name = name;
        card.TypeLine = typeLine;
        card.Supertypes = basic ? ["Basic"] : [];
        card.CardTypes = ["Land"];
        card.Subtypes = subtypes;
        return card;
    }

    private static Card Spell(string name, string typeLine, string manaCost, int manaValue)
    {
        var card = TestCards.Minimal(Guid.NewGuid());
        card.Name = name;
        card.TypeLine = typeLine;
        var firstWord = typeLine.Split(' ')[0];
        card.CardTypes = [firstWord == "Legendary" ? "Creature" : firstWord];
        card.ManaCost = manaCost;
        card.ManaValue = manaValue;
        return card;
    }
}
