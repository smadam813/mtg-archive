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
/// Collection storage and the import surface: PUT /collection
/// full-replace import over both formats, aggregation to oracle-level
/// quantities, the quarantine report, JSONB provenance, GET /collection, and
/// the ingest report's stale-collection-rows field. Each test gets its own
/// migrated database — the collection is global singleton state.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CollectionTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Manabox_import_resolves_aggregates_and_quarantines()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();
        var isperia = Guid.NewGuid();
        var tutor = Guid.NewGuid();
        var fireIce = Guid.NewGuid();
        var bo = Guid.NewGuid();
        var clueBait = Guid.NewGuid();
        var clueTreasure = Guid.NewGuid();
        var bolt = Guid.NewGuid();
        await SeedAsync(connectionString,
            Named(isperia, "Isperia, Supreme Judge"),
            Named(tutor, "Demonic Tutor"),
            Named(fireIce, "Fire // Ice"),
            Named(bo, "Bespoke Bō"),
            // Two tokens sharing a name, with no real card to win — the
            // ambiguous case.
            Named(clueBait, "Clue", "token"),
            Named(clueTreasure, "Clue", "token"),
            Named(bolt, "Lightning Bolt"));

        var result = await PutAsync(client, "manabox_csv", Fixture("manabox-15-column.csv"));

        Assert.Equal(7, result.GetProperty("total_rows").GetInt32());
        Assert.Equal(5, result.GetProperty("imported_rows").GetInt32());
        Assert.Equal(2, result.GetProperty("quarantined_rows").GetInt32());
        Assert.Equal(5, result.GetProperty("distinct_cards").GetInt32());
        Assert.Equal(5, result.GetProperty("total_quantity").GetInt32());

        // Success-with-quarantine: the failed rows ride inside the 2xx with
        // input line numbers and reasons; the resolvable rows imported.
        var quarantine = result.GetProperty("quarantine").EnumerateArray().ToList();
        Assert.Equal(2, quarantine.Count);
        Assert.Equal(6, quarantine[0].GetProperty("line_number").GetInt32());
        Assert.Equal("Clue", quarantine[0].GetProperty("name").GetString());
        Assert.Equal("ambiguous", quarantine[0].GetProperty("reason").GetString());
        Assert.Equal(new[] { clueBait, clueTreasure }.Order(),
            quarantine[0].GetProperty("candidate_oracle_ids").EnumerateArray().Select(c => c.GetGuid()).Order());
        Assert.Equal(8, quarantine[1].GetProperty("line_number").GetInt32());
        Assert.Equal("Lighming Bolt", quarantine[1].GetProperty("name").GetString());
        Assert.Equal("no_match", quarantine[1].GetProperty("reason").GetString());
        Assert.False(quarantine[1].TryGetProperty("candidate_oracle_ids", out _));

        var collection = await GetCollectionAsync(client);
        var cards = collection.GetProperty("cards").EnumerateArray().ToList();
        Assert.Equal(
            new[] { "Bespoke Bō", "Demonic Tutor", "Fire // Ice", "Isperia, Supreme Judge", "Lightning Bolt" },
            cards.Select(c => c.GetProperty("name").GetString()).ToArray());
        Assert.All(cards, c => Assert.Equal(1, c.GetProperty("quantity").GetInt32()));
        Assert.Equal(fireIce, cards[2].GetProperty("oracle_id").GetGuid());

        var import = collection.GetProperty("import");
        Assert.Equal("manabox_csv", import.GetProperty("format").GetString());
        Assert.Equal(7, import.GetProperty("total_rows").GetInt32());
        Assert.Equal(5, import.GetProperty("imported_rows").GetInt32());
        Assert.Equal(2, import.GetProperty("quarantined_rows").GetInt32());
        Assert.True(import.TryGetProperty("imported_at", out _));
    }

    [Fact]
    public async Task Rows_across_binders_finishes_and_languages_sum_into_one_quantity()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();
        var tutor = Guid.NewGuid();
        await SeedAsync(connectionString, Named(tutor, "Demonic Tutor"));

        var result = await PutAsync(client, "manabox_csv", Lines(
            "Binder Name,Name,Foil,Condition,Language,Quantity",
            "Trade Binder,Demonic Tutor,normal,near_mint,en,1",
            "Deck Box,Demonic Tutor,foil,good,en,2",
            "Trade Binder,Demonic Tutor,normal,played,ja,3"));

        Assert.Equal(3, result.GetProperty("imported_rows").GetInt32());
        Assert.Equal(1, result.GetProperty("distinct_cards").GetInt32());
        Assert.Equal(6, result.GetProperty("total_quantity").GetInt32());

        var row = Assert.Single((await GetCollectionAsync(client)).GetProperty("cards").EnumerateArray());
        Assert.Equal(tutor, row.GetProperty("oracle_id").GetGuid());
        Assert.Equal(6, row.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Importing_the_same_file_twice_yields_identical_state()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();
        await SeedAsync(connectionString,
            Named(Guid.NewGuid(), "Pithing Needle"),
            Named(Guid.NewGuid(), "Demonic Tutor"),
            Named(Guid.NewGuid(), "Fire // Ice"),
            Named(Guid.NewGuid(), "Island"),
            Named(Guid.NewGuid(), "Bespoke Bō"));
        var content = Fixture("decklist.txt");

        var first = await PutAsync(client, "list", content);
        var firstCards = (await GetCollectionAsync(client)).GetProperty("cards").GetRawText();
        var second = await PutAsync(client, "list", content);
        var secondCards = (await GetCollectionAsync(client)).GetProperty("cards").GetRawText();

        Assert.Equal(17, first.GetProperty("total_quantity").GetInt32());
        Assert.Equal(first.GetRawText(), second.GetRawText());
        Assert.Equal(firstCards, secondCards);
    }

    [Fact]
    public async Task A_second_import_removes_every_prior_row()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();
        var relic = Guid.NewGuid();
        var bear = Guid.NewGuid();
        await SeedAsync(connectionString, Named(relic, "Ancient Relic"), Named(bear, "Old Bear"));

        await PutAsync(client, "list", "2 Ancient Relic");
        await PutAsync(client, "list", "1 Old Bear");

        var row = Assert.Single((await GetCollectionAsync(client)).GetProperty("cards").EnumerateArray());
        Assert.Equal(bear, row.GetProperty("oracle_id").GetGuid());
        Assert.Equal(1, row.GetProperty("quantity").GetInt32());

        // The raw provenance rows were replaced too; plain-text rows carry
        // no provenance columns.
        await using var db = PostgresFixture.CreateContext(connectionString);
        var stored = Assert.Single(await db.CollectionImportRows.ToListAsync());
        Assert.Equal("Old Bear", stored.Name);
        Assert.False(stored.Provenance.RootElement.EnumerateObject().Any());
    }

    [Fact]
    public async Task Face_names_fall_back_for_decklists_but_never_for_manabox_rows()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();
        var fireIce = Guid.NewGuid();
        var card = Named(fireIce, "Fire // Ice");
        card.Faces =
        [
            new CardFace { FaceIndex = 0, Name = "Fire", TypeLine = "Instant" },
            new CardFace { FaceIndex = 1, Name = "Ice", TypeLine = "Instant" },
        ];
        await SeedAsync(connectionString, card);

        var result = await PutAsync(client, "list", "1 Fire");
        var row = Assert.Single((await GetCollectionAsync(client)).GetProperty("cards").EnumerateArray());
        Assert.Equal(fireIce, row.GetProperty("oracle_id").GetGuid());

        // The bare face name resolves nothing as a ManaBox row, and an
        // import resolving nothing is refused with the quarantine attached.
        var response = await client.PutAsJsonAsync("/collection",
            new { format = "manabox_csv", content = Lines("Name,Quantity", "Fire,1") });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entry = Assert.Single(problem.RootElement.GetProperty("quarantine").EnumerateArray());
        Assert.Equal("no_match", entry.GetProperty("reason").GetString());
        Assert.Equal(2, entry.GetProperty("line_number").GetInt32());
    }

    [Fact]
    public async Task Imports_resolving_nothing_are_refused_and_leave_the_collection_whole()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();
        var bear = Guid.NewGuid();
        await SeedAsync(connectionString, Named(bear, "Old Bear"));
        await PutAsync(client, "list", "1 Old Bear");

        // Explicit null content, blank content, a header-only CSV, and a
        // list of unknown names all resolve nothing; none of them wipes.
        foreach (var body in new object[]
        {
            new { format = "list", content = (string?)null },
            new { format = "list", content = "" },
            new { format = "list", content = "\n\n" },
            new { format = "manabox_csv", content = "Name,Quantity\n" },
            new { format = "list", content = "1 Nobody" },
        })
        {
            var response = await client.PutAsJsonAsync("/collection", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("urn:mtg-archive:problem:empty-collection-import",
                problem.RootElement.GetProperty("type").GetString());
            Assert.True(problem.RootElement.TryGetProperty("quarantine", out _));
        }

        var collection = await GetCollectionAsync(client);
        var row = Assert.Single(collection.GetProperty("cards").EnumerateArray());
        Assert.Equal(bear, row.GetProperty("oracle_id").GetGuid());
        Assert.Equal(1, collection.GetProperty("import").GetProperty("total_rows").GetInt32());
    }

    [Fact]
    public async Task Concurrent_imports_serialize_into_one_whole_collection()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();
        var cards = Enumerable.Range(0, 8).Select(i => Named(Guid.NewGuid(), $"Card {i}")).ToArray();
        await SeedAsync(connectionString, cards);

        // Eight full replaces race the first-ever import; each must land
        // whole (no 500 on the singleton row, no interleaved merge).
        var results = await Task.WhenAll(cards.Select(c => PutAsync(client, "list", $"1 {c.Name}")));

        Assert.All(results, r => Assert.Equal(1, r.GetProperty("imported_rows").GetInt32()));
        var row = Assert.Single((await GetCollectionAsync(client)).GetProperty("cards").EnumerateArray());
        Assert.Contains(row.GetProperty("name").GetString(), cards.Select(c => c.Name));
    }

    [Fact]
    public async Task Raw_rows_with_printing_level_scryfall_ids_survive_in_jsonb()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();
        var fireIce = Guid.NewGuid();
        await SeedAsync(connectionString,
            Named(Guid.NewGuid(), "Isperia, Supreme Judge"),
            Named(Guid.NewGuid(), "Demonic Tutor"),
            Named(fireIce, "Fire // Ice"),
            Named(Guid.NewGuid(), "Bespoke Bō"),
            Named(Guid.NewGuid(), "Lightning Bolt"));

        await PutAsync(client, "manabox_csv", Fixture("manabox-15-column.csv"));

        await using var db = PostgresFixture.CreateContext(connectionString);
        var rows = await db.CollectionImportRows.ToListAsync();
        Assert.Equal(5, rows.Count);
        var fire = rows.Single(r => r.Name == "Fire // Ice");
        Assert.Equal(fireIce, fire.OracleId);
        Assert.Equal(4, fire.LineNumber);
        // Header names verbatim, never snake_cased: the printing-level
        // English Scryfall ID is preserved for a future printing effort.
        Assert.Equal("3e1c7dbd-8dd1-425a-9b4d-8c8befb2b103",
            fire.Provenance.RootElement.GetProperty("Scryfall ID").GetString());
        Assert.Equal("etched", fire.Provenance.RootElement.GetProperty("Foil").GetString());
    }

    [Fact]
    public async Task Structural_corruption_and_bad_requests_are_problem_400s()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();

        // A ragged CSV is structural corruption — an error, never quarantine.
        var response = await client.PutAsJsonAsync("/collection",
            new { format = "manabox_csv", content = "Name,Quantity\nAbrade\n" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using (var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal("urn:mtg-archive:problem:invalid-collection-import",
                problem.RootElement.GetProperty("type").GetString());
            Assert.Contains("Line 2", problem.RootElement.GetProperty("detail").GetString());
        }

        response = await client.PutAsJsonAsync("/collection", new { format = "list", content = "x Needle" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Missing members and an unknown format literal fail body binding.
        foreach (var body in new object[]
        {
            new { },
            new { format = "manabox_csv" },
            new { format = "mtga", content = "1 Island" },
        })
        {
            response = await client.PutAsJsonAsync("/collection", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        // Nothing imported through any of it, and no metadata to serve yet.
        var collection = await GetCollectionAsync(client);
        Assert.Empty(collection.GetProperty("cards").EnumerateArray());
        Assert.False(collection.TryGetProperty("import", out _));
    }

    [Fact]
    public async Task Next_ingest_report_lists_collection_rows_pointing_at_stale_cards()
    {
        var connectionString = await SetUpAsync();
        var source = new FakeBulkSource();
        using var app = new ArchiveApp(autoMigrate: true, connectionString, services =>
        {
            services.RemoveAll<IScryfallBulkSource>();
            services.AddSingleton<IScryfallBulkSource>(source);
            services.RemoveAll<ICrSource>();
            services.AddSingleton<ICrSource>(new FakeCrSource());
        });
        using var client = app.CreateClient();
        var relic = Guid.NewGuid();
        var bear = Guid.NewGuid();

        source.OracleCards = Lines(
            CardLine(relic, "Ancient Relic"),
            CardLine(bear, "Old Bear"));
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        await PutAsync(client, "list", Lines("1 Ancient Relic", "2 Old Bear"));

        // The bear vanishes from the next snapshot; the report points at the
        // collection rows now referencing a stale card.
        source.OracleCards = Lines(CardLine(relic, "Ancient Relic"));
        var run = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        var report = run.GetProperty("report").GetProperty("collection_rows_pointing_at_stale_cards");
        Assert.True(report.GetProperty("populated").GetBoolean());
        var row = Assert.Single(report.GetProperty("rows").EnumerateArray());
        Assert.Equal(bear, row.GetProperty("oracle_id").GetGuid());
        Assert.Equal("Old Bear", row.GetProperty("name").GetString());
        Assert.Equal(2, row.GetProperty("quantity").GetInt32());

        // Resolution to a stale card stays valid at import time — the
        // collection keeps serving the row; queries exclude it downstream.
        Assert.Equal(2, (await GetCollectionAsync(client)).GetProperty("cards").EnumerateArray().Count());
    }

    private async Task<string> SetUpAsync()
        => await postgres.CreateDatabaseAsync($"collection_{Guid.NewGuid():N}");

    private static ArchiveApp NewApp(string connectionString) => new(autoMigrate: true, connectionString);

    private static async Task SeedAsync(string connectionString, params Card[] cards)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        db.Cards.AddRange(cards);
        await db.SaveChangesAsync();
    }

    private static Card Named(Guid oracleId, string name, string? layout = null)
    {
        var card = TestCards.Minimal(oracleId);
        card.Name = name;
        card.Layout = layout;
        return card;
    }

    private static string Fixture(string file)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "collection-import", file));

    private static async Task<JsonElement> PutAsync(HttpClient client, string format, string content)
    {
        var response = await client.PutAsJsonAsync("/collection", new { format, content });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static async Task<JsonElement> GetCollectionAsync(HttpClient client)
    {
        var response = await client.GetAsync("/collection");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }
}
