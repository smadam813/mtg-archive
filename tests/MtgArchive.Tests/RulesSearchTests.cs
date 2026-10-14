using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MtgArchive.Data;
using MtgArchive.Embedding;
using Pgvector;

namespace MtgArchive.Tests;

/// <summary>
/// POST /rules/search: one KNN over both chunk kinds, the
/// query prefixed service-side, k defaulted and capped, and the
/// embedding-state gate answered as report content. Each test gets its own
/// migrated database and an in-process Ollama.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RulesSearchTests(PostgresFixture postgres)
{
    private static readonly DateOnly EffectiveDate = new(2026, 8, 7);

    [Fact]
    public async Task Rule_and_glossary_chunks_are_both_hits_nearest_first_with_rule_citations()
    {
        var connectionString = await NewDatabaseAsync();
        var fake = new FakeOllama();
        using var app = NewApp(connectionString, fake);
        using var client = app.CreateClient();
        await SeedAsync(connectionString);

        var response = await client.PostAsJsonAsync("/rules/search", new { query = "how does deathtouch work" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.True(root.GetProperty("semantic").GetProperty("enabled").GetBoolean());
        Assert.False(root.GetProperty("semantic").TryGetProperty("disabled_reason", out _));
        Assert.Equal("2026-08-07", root.GetProperty("versions").GetProperty("cr_effective_date").GetString());

        // Nearest first: the unembedded chunk never appears.
        var hits = root.GetProperty("hits").EnumerateArray().ToArray();
        Assert.Equal(["rule", "glossary", "rule"], hits.Select(h => h.GetProperty("kind").GetString()));
        Assert.Equal(["601.2", "Deathtouch", "702.2"],
            hits.Select(h => h.GetProperty("citation").GetProperty("id").GetString()));
        Assert.All(hits, h => Assert.Equal("rule", h.GetProperty("citation").GetProperty("type").GetString()));
        Assert.Equal("Deathtouch", hits[1].GetProperty("citation").GetProperty("label").GetString());
        Assert.StartsWith("Deathtouch\n", hits[1].GetProperty("text").GetString());
        var distances = hits.Select(h => h.GetProperty("distance").GetDouble()).ToArray();
        Assert.Equal(distances.OrderBy(d => d), distances);
        Assert.Equal(0, distances[0], 3);

        // Every citation resolves through the reference endpoint.
        foreach (var hit in hits)
        {
            var id = hit.GetProperty("citation").GetProperty("id").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/rules/{Uri.EscapeDataString(id)}")).StatusCode);
        }

        // The query reached Ollama with the rules-search instruction and the
        // model's query prefix, applied service-side.
        var (path, request) = Assert.Single(fake.Requests);
        Assert.Equal("/api/embed", path);
        Assert.Equal(
            $"Instruct: {EmbeddingModel.RulesSearchInstruction}\nQuery: how does deathtouch work",
            request.GetProperty("input")[0].GetString());
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData(3, 3)]
    [InlineData(500, 200)]
    public async Task K_defaults_to_50_and_caps_at_200(int? k, int expected)
    {
        var connectionString = await NewDatabaseAsync();
        var fake = new FakeOllama();
        using var app = NewApp(connectionString, fake);
        using var client = app.CreateClient();
        await SeedAsync(connectionString, padding: 210);

        var response = await client.PostAsJsonAsync("/rules/search", new { query = "padding", k });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, body.RootElement.GetProperty("hits").GetArrayLength());
    }

    [Fact]
    public async Task A_mismatched_embedding_state_answers_disabled_as_report_content()
    {
        var connectionString = await NewDatabaseAsync();
        var fake = new FakeOllama();
        using var app = NewApp(connectionString, fake);
        using var client = app.CreateClient();
        await SeedAsync(connectionString);
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            (await db.EmbeddingStates.SingleAsync()).PrefixSetVersion = "0000000000000000";
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/rules/search", new { query = "deathtouch" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var semantic = body.RootElement.GetProperty("semantic");
        Assert.False(semantic.GetProperty("enabled").GetBoolean());
        Assert.Contains("0000000000000000", semantic.GetProperty("disabled_reason").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("hits").GetArrayLength());
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task A_missing_query_is_a_problem_400()
    {
        var connectionString = await NewDatabaseAsync();
        var fake = new FakeOllama();
        using var app = NewApp(connectionString, fake);
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/rules/search", new { k = 5 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("query", body.RootElement.GetProperty("parameter").GetString());
    }

    private Task<string> NewDatabaseAsync() => postgres.CreateDatabaseAsync($"rules_search_{Guid.NewGuid():N}");

    /// <summary>The host with <paramref name="fake"/> behind the embedder, so
    /// the test can read the query Ollama saw.</summary>
    private static ArchiveApp NewApp(string connectionString, FakeOllama fake)
        => new(autoMigrate: true, connectionString, services =>
            services.AddHttpClient<OllamaEmbedder>().ConfigurePrimaryHttpMessageHandler(() => fake));

    /// <summary>Three rule chunks and a glossary entry at fixed cosine
    /// distances from <see cref="FakeOllama"/>'s all-ones query (601.2
    /// nearest, then Deathtouch, then 702.2; 999.1 unembedded), the
    /// embedding_state record matching configuration, and
    /// <paramref name="padding"/> further glossary entries at distance 0.</summary>
    private static async Task SeedAsync(string connectionString, int padding = 0)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        db.CrRuleChunks.AddRange(
            new CrRuleChunk { Number = "601.2", Text = "601.2. Rule text.", CrEffectiveDate = EffectiveDate, Embedding = Ones(1024) },
            new CrRuleChunk { Number = "702.2", Text = "702.2. Other rule text.", CrEffectiveDate = EffectiveDate, Embedding = Ones(256) },
            new CrRuleChunk { Number = "999.1", Text = "999.1. Late rule.", CrEffectiveDate = EffectiveDate });
        db.CrGlossaryChunks.Add(
            new CrGlossaryChunk { Term = "Deathtouch", Text = "Deathtouch\nA keyword.", CrEffectiveDate = EffectiveDate, Embedding = Ones(512) });
        db.CrGlossaryChunks.AddRange(Enumerable.Range(0, padding).Select(i =>
            new CrGlossaryChunk { Term = $"Padding {i}", Text = $"Padding {i}\nFiller.", CrEffectiveDate = EffectiveDate, Embedding = Ones(1024) }));
        db.EmbeddingStates.Add(EmbeddingModel.Qwen3.State.ToStored());
        await db.SaveChangesAsync();
    }

    /// <summary>A 1024-dim vector with its first <paramref name="ones"/>
    /// components set: cosine distance from the all-ones vector grows as
    /// fewer are set.</summary>
    private static Vector Ones(int ones)
        => new(Enumerable.Range(0, 1024).Select(i => i < ones ? 1f : 0f).ToArray());
}
