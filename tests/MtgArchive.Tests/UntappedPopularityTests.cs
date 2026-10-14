using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The Untapped popularity import (ADR 0012): the popularity
/// load phase reads one popularity export per Brawl format and orders that
/// format's cards by it before the EDHREC tiers. Each test writes small
/// export files in the Untapped shape (en dash blanks, <c>"4.0%"</c>,
/// <c>"5,400"</c>), ingests its own fixture snapshot through the real
/// pipeline, and asks over HTTP.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UntappedPopularityTests(PostgresFixture postgres) : IDisposable
{
    private const string Window = "2026-09-29_2026-10-04";

    private const string Header =
        "id,name,In Hand WR,Not In Hand WR,In Hand WR Delta,In Opening Hand WR,Played WR,Included WR,Popularity,In Main Deck,Total Games";

    private const string Blank = "–";

    private readonly string _dataDir = Directory.CreateTempSubdirectory("mtg-archive-untapped-").FullName;

    private readonly FakeBulkSource _source = new();

    private string PopularityDir => Path.Combine(_dataDir, "popularity");

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    // The commander row and the deck row of one card fill two seats: the
    // commander order reads the commander row first, the card order the
    // deck row. File order does not mark the seat.
    [Fact]
    public async Task A_deck_row_and_a_commander_row_for_one_card_land_in_their_seats()
    {
        var walker = Guid.NewGuid();
        var legend = Guid.NewGuid();
        WriteExport("brawl", Window,
            CommanderRow("Walker", "2.0%", "900"),
            DeckRow("Legend", "3.0%", "3.0%", "1,000"),
            DeckRow("Walker", "1.0%", "1.0%", "500"),
            CommanderRow("Legend", "1.5%", "700"));
        using var app = await IngestAsync(
            CardLine(walker, "Walker", typeLine: "Legendary Planeswalker — Test", edhrecRank: 1, brawl: BrawlKeys.Legal),
            CardLine(legend, "Legend", typeLine: "Legendary Creature — Elf", edhrecRank: 2, brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();

        Assert.Equal([(legend, 1), (walker, 2)], (await SearchAsync(client, "brawl", "main")).Select(h => (h.Id, h.Rank)));
        Assert.Equal([(walker, 1), (legend, 2)], (await SearchAsync(client, "brawl", "commander")).Select(h => (h.Id, h.Rank)));
    }

    [Fact]
    public async Task Ingest_fails_when_a_format_directory_holds_two_exports()
    {
        WriteExport("brawl", Window, DeckRow("Rock", "1.0%", "1.0%", "100"));
        WriteExport("brawl", "2026-09-22_2026-09-29", DeckRow("Rock", "1.0%", "1.0%", "100"));
        using var app = await StartAsync(CardLine(Guid.NewGuid(), "Rock", brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();

        var run = await WaitForCompletionAsync(client, await TriggerAsync(client), "failed");

        var errors = run.GetProperty("report").GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(errors, e => e!.Contains("popularity/brawl") && e.Contains("exactly one export"));
    }

    // Untapped writes a split card with " /// "; a modal double-faced card
    // under its front face. A name outside the format's pool matches
    // nothing, and a row with no stats is not a row at all.
    [Fact]
    public async Task A_split_name_and_a_front_face_name_match_and_a_name_outside_the_pool_is_skipped_and_reported()
    {
        var split = Guid.NewGuid();
        var modal = Guid.NewGuid();
        var rock = Guid.NewGuid();
        WriteExport("brawl", Window,
            DeckRow("Rock", "1.0%", "1.0%", "100"),
            DeckRow("Commit /// Memory", "3.0%", "3.0%", "300"),
            DeckRow("Valki, God of Lies", "2.0%", "2.0%", "200"),
            DeckRow("Standard Only", "9.0%", "9.0%", "900"),
            CommanderRow("Nowhere Legend", "5.0%", "500"),
            EmptyRow("Empty Stats"));
        using var app = await IngestAsync(
            CardLine(rock, "Rock", brawl: BrawlKeys.Legal),
            SplitLine(split, "Commit // Memory"),
            ModalCreatureLine(modal, "Valki, God of Lies", "Tibalt, Cosmic Impostor"),
            CardLine(Guid.NewGuid(), "Standard Only", edhrecRank: 1, brawl: new BrawlKeys("not_legal", "legal", "legal")));
        using var client = app.CreateClient();

        Assert.Equal([split, modal, rock], (await SearchAsync(client, "brawl", "main")).Take(3).Select(h => h.Id));
        var run = await LatestRunAsync(client);
        var skipped = run.GetProperty("report").GetProperty("skipped_popularity_rows").EnumerateArray()
            .Select(r => (
                r.GetProperty("format").GetString(),
                r.GetProperty("name").GetString(),
                r.GetProperty("seat").GetString(),
                r.GetProperty("reason").GetString()))
            .ToList();
        Assert.Equal(
            [("brawl", "Standard Only", "main", "no_match"), ("brawl", "Nowhere Legend", "commander", "no_match")],
            skipped);
    }

    // Tier 1 is the deck row's share, ties by total games; then edhrec_rank,
    // then name, then oracle id. A commander row alone does not lift a
    // card in the card order.
    [Fact]
    public async Task The_card_order_reads_the_deck_share_then_total_games_then_the_edhrec_tiers()
    {
        var wide = Guid.NewGuid();
        var narrow = Guid.NewGuid();
        var top = Guid.NewGuid();
        var ranked = Guid.NewGuid();
        var captain = Guid.NewGuid();
        var unranked = Guid.NewGuid();
        WriteExport("competitive_brawl", Window,
            DeckRow("Narrow", "2.0%", "2.0%", "1,000"),
            DeckRow("Top", "4.0%", "4.0%", "5,400"),
            CommanderRow("Captain", "9.0%", "9,000"),
            DeckRow("Wide", "2.0%", "2.0%", "1,500"));
        using var app = await IngestAsync(
            CardLine(unranked, "Aardvark", brawl: BrawlKeys.Legal),
            CardLine(captain, "Captain", typeLine: "Legendary Creature — Elf", edhrecRank: 50, brawl: BrawlKeys.Legal),
            CardLine(ranked, "Ranked", edhrecRank: 1, brawl: BrawlKeys.Legal),
            CardLine(narrow, "Narrow", edhrecRank: 2, brawl: BrawlKeys.Legal),
            CardLine(top, "Top", edhrecRank: 900, brawl: BrawlKeys.Legal),
            CardLine(wide, "Wide", edhrecRank: 800, brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();

        var hits = await SearchAsync(client, "competitive_brawl", "main");

        Assert.Equal([top, wide, narrow, ranked, captain, unranked], hits.Select(h => h.Id));
        Assert.Equal([1, 2, 3, 4, 5, 6], hits.Select(h => h.Rank));
    }

    // Tier 1a is the commander row's share, tier 1b the deck row's share,
    // then edhrec_rank, name, oracle id. The commander order covers the
    // whole pool.
    [Fact]
    public async Task The_commander_order_reads_the_commander_share_then_the_deck_share_then_the_edhrec_tiers()
    {
        var led = Guid.NewGuid();
        var second = Guid.NewGuid();
        var deckOnly = Guid.NewGuid();
        var famous = Guid.NewGuid();
        var obscure = Guid.NewGuid();
        WriteExport("brawl", Window,
            DeckRow("Deck Only Legend", "6.0%", "6.0%", "6,000"),
            CommanderRow("Second Legend", "1.0%", "800"),
            DeckRow("Second Legend", "8.0%", "8.0%", "8,000"),
            CommanderRow("Leading Legend", "3.0%", "2,000"));
        const string legendary = "Legendary Creature — Elf";
        using var app = await IngestAsync(
            CardLine(obscure, "Obscure Legend", typeLine: legendary, brawl: BrawlKeys.Legal),
            CardLine(famous, "Famous Legend", typeLine: legendary, edhrecRank: 1, brawl: BrawlKeys.Legal),
            CardLine(deckOnly, "Deck Only Legend", typeLine: legendary, edhrecRank: 3, brawl: BrawlKeys.Legal),
            CardLine(second, "Second Legend", typeLine: legendary, edhrecRank: 4, brawl: BrawlKeys.Legal),
            CardLine(led, "Leading Legend", typeLine: legendary, edhrecRank: 5, brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();

        var hits = await SearchAsync(client, "brawl", "commander");

        Assert.Equal([led, second, deckOnly, famous, obscure], hits.Select(h => h.Id));
        Assert.Equal([1, 2, 3, 4, 5], hits.Select(h => h.Rank));
    }

    // An export changes only its own format: Commander and Standard Brawl
    // keep the EDHREC order, and so does a Brawl format with no directory.
    [Fact]
    public async Task Formats_without_an_export_keep_the_edhrec_order_and_source()
    {
        var best = Guid.NewGuid();
        var popular = Guid.NewGuid();
        WriteExport("brawl", Window, DeckRow("Arena Darling", "7.0%", "7.0%", "7,000"));
        using var app = await IngestAsync(
            CardLine(popular, "Arena Darling", edhrecRank: 90, brawl: BrawlKeys.Legal),
            CardLine(best, "Edhrec Darling", edhrecRank: 1, brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();

        Assert.Equal([popular, best], (await SearchAsync(client, "brawl", "main")).Select(h => h.Id));
        foreach (var format in new[] { "commander", "competitive_brawl", "standard_brawl" })
        {
            Assert.Equal([best, popular], (await SearchAsync(client, format, "main")).Select(h => h.Id));
            using var body = await SearchBodyAsync(client, new { format, limit = 1 });
            var versions = body.RootElement.GetProperty("versions");
            Assert.Equal("edhrec", versions.GetProperty("popularity_source").GetString());
            Assert.Equal(_source.UpdatedAt, versions.GetProperty("popularity_as_of").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task The_versions_block_names_untapped_and_the_window_end_for_both_brawl_formats()
    {
        WriteExport("brawl", Window, DeckRow("Rock", "1.0%", "1.0%", "100"));
        WriteExport("competitive_brawl", "2026-09-28_2026-10-03", DeckRow("Rock", "1.0%", "1.0%", "100"));
        using var app = await IngestAsync(CardLine(Guid.NewGuid(), "Rock", brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();

        foreach (var (format, end) in new[] { ("brawl", "2026-10-04"), ("competitive_brawl", "2026-10-03") })
        {
            using var body = await SearchBodyAsync(client, new { format, limit = 1 });
            var versions = body.RootElement.GetProperty("versions");
            Assert.Equal("untapped", versions.GetProperty("popularity_source").GetString());
            Assert.Equal(
                DateTimeOffset.Parse(end + "T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                versions.GetProperty("popularity_as_of").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task Corpus_state_lists_each_export_window_and_file_hash()
    {
        WriteExport("brawl", Window, DeckRow("Rock", "1.0%", "1.0%", "100"));
        using var app = await IngestAsync(CardLine(Guid.NewGuid(), "Rock", brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();
        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            File.ReadAllBytes(Path.Combine(PopularityDir, "brawl", Window + ".csv"))));

        var state = await GetRunAsync(client, "/corpus-state");

        var exports = state.GetProperty("popularity_exports").EnumerateArray().ToList();
        var export = Assert.Single(exports);
        Assert.Equal("brawl", export.GetProperty("format").GetString());
        Assert.Equal("2026-09-29", export.GetProperty("window_start").GetString());
        Assert.Equal("2026-10-04", export.GetProperty("window_end").GetString());
        Assert.Equal(expectedHash, export.GetProperty("file_sha256").GetString());
    }

    [Fact]
    public async Task A_changed_export_triggers_an_ingest_without_force()
    {
        var rock = Guid.NewGuid();
        var stone = Guid.NewGuid();
        WriteExport("brawl", Window, DeckRow("Rock", "2.0%", "2.0%", "100"), DeckRow("Stone", "1.0%", "1.0%", "100"));
        using var app = await IngestAsync(
            CardLine(rock, "Rock", brawl: BrawlKeys.Legal),
            CardLine(stone, "Stone", brawl: BrawlKeys.Legal));
        using var client = app.CreateClient();
        var unchanged = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        Assert.True(unchanged.GetProperty("no_op").GetBoolean());

        WriteExport("brawl", Window, DeckRow("Rock", "1.0%", "1.0%", "100"), DeckRow("Stone", "2.0%", "2.0%", "100"));
        var changed = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        Assert.False(changed.GetProperty("no_op").GetBoolean());
        Assert.Equal([stone, rock], (await SearchAsync(client, "brawl", "main")).Select(h => h.Id));
    }

    private static string SplitLine(Guid oracleId, string name)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["oracle_id"] = oracleId,
            ["name"] = name,
            ["layout"] = "split",
            ["type_line"] = "Instant // Sorcery",
            ["cmc"] = 2,
            ["color_identity"] = Array.Empty<string>(),
            ["keywords"] = Array.Empty<string>(),
            ["released_at"] = "2020-01-01",
            ["game_changer"] = false,
            ["scryfall_uri"] = $"https://scryfall.com/card/tst/1/{oracleId}",
            ["legalities"] = new Dictionary<string, object?> { ["commander"] = "legal", ["brawl"] = "legal" },
            ["card_faces"] = name.Split(" // ").Select(f => new Dictionary<string, object?>
            {
                ["name"] = f,
                ["type_line"] = "Instant",
                ["mana_cost"] = "{1}",
                ["oracle_text"] = "Draw a card.",
            }).ToArray(),
        });

    private static string ModalCreatureLine(Guid oracleId, string front, string back)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["oracle_id"] = oracleId,
            ["name"] = $"{front} // {back}",
            ["layout"] = "modal_dfc",
            ["type_line"] = "Legendary Creature — God // Legendary Planeswalker — Tibalt",
            ["cmc"] = 2,
            ["color_identity"] = Array.Empty<string>(),
            ["keywords"] = Array.Empty<string>(),
            ["released_at"] = "2020-01-01",
            ["game_changer"] = false,
            ["scryfall_uri"] = $"https://scryfall.com/card/tst/1/{oracleId}",
            ["legalities"] = new Dictionary<string, object?> { ["commander"] = "legal", ["brawl"] = "legal" },
            ["card_faces"] = new[]
            {
                new Dictionary<string, object?> { ["name"] = front, ["type_line"] = "Legendary Creature — God", ["mana_cost"] = "{1}", ["oracle_text"] = "" },
                new Dictionary<string, object?> { ["name"] = back, ["type_line"] = "Legendary Planeswalker — Tibalt", ["mana_cost"] = "{7}", ["oracle_text"] = "" },
            },
        });

    private static async Task<JsonElement> LatestRunAsync(HttpClient client)
        => await GetRunAsync(client, "/ingest/runs/latest");

    private void WriteExport(string format, string window, params string[] rows)
    {
        var dir = Path.Combine(PopularityDir, format);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, window + ".csv"), Csv(rows), new UTF8Encoding(false));
    }

    private static string Csv(params string[] rows) => string.Join('\n', [Header, .. rows]) + "\n";

    /// <summary>A deck row: <c>In Main Deck</c> holds a value.</summary>
    private static string DeckRow(string name, string popularity, string inMainDeck, string totalGames)
        => Row(name, popularity, inMainDeck, totalGames);

    /// <summary>A commander row: <c>In Main Deck</c> is blank and
    /// <c>Popularity</c> holds the share of games with the card as commander.</summary>
    private static string CommanderRow(string name, string share, string totalGames)
        => Row(name, share, Blank, totalGames);

    /// <summary>A row with no stats: every value is blank.</summary>
    private static string EmptyRow(string name) => Row(name, Blank, Blank, Blank, winRates: Blank);

    private static string Row(string name, string popularity, string inMainDeck, string totalGames, string winRates = "55.0%")
        => string.Join(',', new[]
        {
            Random.Shared.Next(1, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture),
            name, winRates, winRates, Blank, Blank, winRates, winRates, popularity, inMainDeck, totalGames,
        }.Select(v => "\"" + v.Replace("\"", "\"\"") + "\""));

    private async Task<ArchiveApp> IngestAsync(params string[] lines)
    {
        var app = await StartAsync(lines);
        using var client = app.CreateClient();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        return app;
    }

    private async Task<ArchiveApp> StartAsync(params string[] lines)
    {
        var eventsPath = Path.Combine(_dataDir, "banlist-events.json");
        await File.WriteAllTextAsync(eventsPath, """
            [{ "effective_date": "2024-01-01", "date_precision": "day",
               "announcement_url": "https://example.org/2024", "events": [] }]
            """);
        var app = new ArchiveApp(
            autoMigrate: true,
            await postgres.CreateDatabaseAsync($"untapped_{Guid.NewGuid():N}"),
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(_source);
                services.RemoveAll<ICrSource>();
                services.AddSingleton<ICrSource>(new FakeCrSource());
            },
            new Dictionary<string, string?>
            {
                ["Banlist:EventsPath"] = eventsPath,
                ["Ingest:PopularityDir"] = PopularityDir,
            });
        _source.OracleCards = Lines(lines);
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
