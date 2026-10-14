using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MtgArchive.Embedding;

namespace MtgArchive.Tests;

/// <summary>
/// The vector storage schema and the embedding-state mismatch gate:
/// three HNSW cosine columns at vector(1024), the singleton record,
/// semantic queries disabled on any disagreement with configuration, and
/// GET /corpus-state serving the record.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EmbeddingStateTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Exactly_three_tables_carry_a_1024_dim_hnsw_cosine_embedding()
    {
        await using var db = postgres.CreateCorpusContext();

        var columns = await db.Database.SqlQueryRaw<string>(
            """
            SELECT c.relname || ' ' || format_type(a.atttypid, a.atttypmod) AS "Value"
            FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
            WHERE a.attname = 'embedding' AND c.relkind = 'r'
            ORDER BY c.relname
            """).ToListAsync();
        var indexed = await db.Database.SqlQueryRaw<string>(
            """
            SELECT tablename AS "Value" FROM pg_indexes
            WHERE indexdef LIKE '%USING hnsw (embedding vector_cosine_ops)' ORDER BY 1
            """).ToListAsync();
        var singleton = await db.Database.SqlQueryRaw<long>(
            """SELECT COUNT(*) AS "Value" FROM pg_constraint WHERE conname = 'ck_embedding_state_singleton'""")
            .SingleAsync();

        Assert.Equal(
            ["card_face_document vector(1024)", "cr_glossary_chunk vector(1024)", "cr_rule_chunk vector(1024)"],
            columns);
        Assert.Equal(["card_face_document", "cr_glossary_chunk", "cr_rule_chunk"], indexed);
        Assert.Equal(1L, singleton);
    }

    [Fact]
    public async Task Semantic_queries_are_disabled_until_the_stored_state_matches_configuration()
    {
        var connectionString = await postgres.CreateDatabaseAsync($"embedding_state_{Guid.NewGuid():N}");
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();
        var configured = app.Services.GetRequiredService<EmbeddingModel>().State;

        // Nothing embedded yet: disabled, with the configured state named.
        var nothing = await CheckAsync(app);
        Assert.False(nothing.Enabled);
        Assert.Contains("no embedding_state", nothing.DisabledReason);
        Assert.Contains(configured.ModelTag, nothing.DisabledReason);

        // The re-embed phase records what it produced under: a match enables.
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            db.EmbeddingStates.Add(configured.ToStored());
            await db.SaveChangesAsync();
        }

        var matching = await CheckAsync(app);
        Assert.True(matching.Enabled);
        Assert.Null(matching.DisabledReason);

        // Served at rest, verbatim.
        using var state = JsonDocument.Parse(await client.GetStringAsync("/corpus-state"));
        var served = state.RootElement.GetProperty("embedding_state");
        Assert.Equal(configured.ModelTag, served.GetProperty("model_tag").GetString());
        Assert.Equal(configured.Dimensions, served.GetProperty("dimensions").GetInt32());
        Assert.Equal(configured.PrefixSetVersion, served.GetProperty("prefix_set_version").GetString());

        // A prefix change (or a model switch) under stored vectors: disabled
        // — not an error, not an automatic re-embed — until ingest re-embeds.
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var stored = await db.EmbeddingStates.SingleAsync();
            stored.PrefixSetVersion = "0000000000000000";
            await db.SaveChangesAsync();
        }

        var stale = await CheckAsync(app);
        Assert.False(stale.Enabled);
        Assert.Contains("0000000000000000", stale.DisabledReason);
        Assert.Contains(configured.PrefixSetVersion, stale.DisabledReason);

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var stored = await db.EmbeddingStates.SingleAsync();
            stored.PrefixSetVersion = configured.PrefixSetVersion;
            stored.ModelTag = "nomic-embed-text";
            stored.Dimensions = 768;
            await db.SaveChangesAsync();
        }

        var switched = await CheckAsync(app);
        Assert.False(switched.Enabled);
        Assert.Contains("nomic-embed-text", switched.DisabledReason);
    }

    private static async Task<SemanticQueryStatus> CheckAsync(ArchiveApp app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EmbeddingStateGate>().CheckAsync(CancellationToken.None);
    }
}
