using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;
using Npgsql;

namespace MtgArchive.Tests;

/// <summary>
/// Collection scope at the index boundary: <c>legal_candidates</c>
/// under <c>collection_scope</c> is exactly the legal, non-stale, owned set,
/// still one inlined statement; availability is min(copy limit, owned
/// quantity) with no infinite-basics assumption; unscoped queries never see
/// the collection; and an owned card that went stale is the ingest report's
/// business, never a candidate.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CollectionScopeTests(PostgresFixture postgres)
{
    private static readonly Role[] Roles = [Role.Commander, Role.Companion, Role.Main];

    [Fact]
    public async Task Scoped_candidates_are_exactly_the_legal_owned_set_per_version_and_role()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        await using var db = PostgresFixture.CreateContext(connectionString);

        foreach (var version in oracle.Versions)
        {
            foreach (var role in Roles)
            {
                var expected = oracle.Legal(version, role).Intersect(oracle.Owned);
                var actual = await db.LegalCandidates(Format.Commander, DateOnly.Parse(version), role, collectionScope: true)
                    .Select(c => c.OracleId)
                    .ToListAsync();
                Assert.Equal(expected.Order(), actual.Order());
                Assert.NotEmpty(actual);
            }
        }

        // Scope narrows: the corpus has legal cards the collection lacks.
        Assert.Contains(oracle.Legal(oracle.Latest, Role.Main), id => !oracle.Owned.Contains(id));
    }

    [Fact]
    public async Task Unscoped_queries_are_unaffected_by_collection_contents()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.NotEmpty(oracle.Owned);

        var unscoped = await db.LegalCandidates(Format.Commander, DateOnly.Parse(oracle.Latest), Role.Main)
            .Select(c => c.OracleId)
            .ToListAsync();

        Assert.Equal(oracle.Legal(oracle.Latest, Role.Main).Order(), unscoped.Order());
        Assert.Contains(unscoped, id => !oracle.Owned.Contains(id));
    }

    [Fact]
    public async Task Availability_is_min_of_copy_limit_and_owned_quantity()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        using var app = new ArchiveApp(autoMigrate: false, connectionString);
        using var client = app.CreateClient();

        var scoped = await PostAsync(client, new { collection_scope = true, limit = 100 });
        var available = scoped.GetProperty("candidates").EnumerateArray().ToDictionary(
            c => Guid.Parse(c.GetProperty("card").GetProperty("id").GetString()!),
            c => c.GetProperty("available").GetInt32());

        Assert.Equal(3, available[IndexCorpus.OracleIdOf(oracle, IndexCorpus.BasicLand)]);
        Assert.Equal(7, available[IndexCorpus.OracleIdOf(oracle, IndexCorpus.SevenDwarves)]);
        Assert.Equal(1, available[IndexCorpus.OracleIdOf(oracle, IndexCorpus.Singleton)]);
        var unowned = IndexCorpus.OracleIdOf(oracle, IndexCorpus.Unowned);
        Assert.Contains(unowned, oracle.Legal(oracle.Latest, Role.Main));
        Assert.DoesNotContain(unowned, available.Keys);
        Assert.All(available.Keys, id => Assert.Contains(id, oracle.Owned));
        Assert.All(available, kv => Assert.Equal(oracle.Available(kv.Key), kv.Value));

        var unscoped = await PostAsync(client, new { limit = 100 });
        var candidates = unscoped.GetProperty("candidates").EnumerateArray().ToList();
        Assert.True(candidates.Count > available.Count);
        Assert.All(candidates, c => Assert.False(c.TryGetProperty("available", out _)));
    }

    [Fact]
    public async Task An_owned_card_that_went_stale_is_reported_by_ingest_and_never_a_candidate()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        await using var db = PostgresFixture.CreateContext(connectionString);
        var stale = IndexCorpus.OracleIdOf(oracle, IndexCorpus.OwnedStale);

        Assert.Equal(2, (await db.CollectionCards.SingleAsync(o => o.OracleId == stale)).Quantity);
        foreach (var role in Roles)
        {
            Assert.DoesNotContain(stale, await db
                .LegalCandidates(Format.Commander, DateOnly.Parse(oracle.Latest), role, collectionScope: true)
                .Select(c => c.OracleId)
                .ToListAsync());
        }

        var report = (await db.IngestRuns.OrderByDescending(r => r.Id).FirstAsync()).Report!
            .RootElement.GetProperty("collection_rows_pointing_at_stale_cards");
        var row = Assert.Single(report.GetProperty("rows").EnumerateArray());
        Assert.Equal(stale, row.GetProperty("oracle_id").GetGuid());
    }

    [Fact]
    public async Task Scoped_queries_still_plan_as_one_inlined_statement()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            EXPLAIN SELECT oracle_id, available_copies(copy_limit, oracle_id)
            FROM legal_candidates('commander', $1, 'main', NULL, true)
            WHERE card_types @> ARRAY['Creature']
            """, conn);
        cmd.Parameters.AddWithValue(DateOnly.Parse(oracle.Latest));
        var plan = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }

        var text = string.Join('\n', plan);
        Assert.DoesNotContain("Function Scan", text);
        Assert.Contains("collection_card", text);
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/_seam/candidates", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
