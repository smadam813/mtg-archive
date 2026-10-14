using System.Net.Http.Json;
using System.Text.Json;
using MtgArchive.Api;
using Xunit.Abstractions;

namespace MtgArchive.Tests;

/// <summary>
/// The on-demand Retrieval eval against a live, ingested
/// service over plain HTTP — the real service path, never the in-process
/// host. Not the default run or CI: the project's default test filter
/// excludes <c>Category=retrieval-eval</c>. eval/retrieval/README.md has
/// the procedure and the two-model comparison.
/// </summary>
public sealed class RetrievalEvalLiveTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Pretty = new(WireJson.Options) { WriteIndented = true };

    [Fact]
    [Trait("Category", "retrieval-eval")]
    public async Task The_fixture_scores_through_the_live_service()
    {
        var evalDir = Path.Combine(RepoRoot.Find(), "eval", "retrieval");
        var fixture = JsonSerializer.Deserialize<RetrievalEval.Fixture>(
            await File.ReadAllTextAsync(Path.Combine(evalDir, "queries.json")), WireJson.Options)!;
        Assert.Empty(RetrievalEval.Problems(fixture));
        using var client = new HttpClient
        {
            BaseAddress = new(Environment.GetEnvironmentVariable("MTG_ARCHIVE_URL") ?? "http://127.0.0.1:8080"),
            // Every query goes out at once and Ollama embeds them one by one,
            // so the last answer waits on the whole queue.
            Timeout = TimeSpan.FromMinutes(10),
        };

        var corpusState = await ReadAsync(await client.GetAsync("/corpus-state"));
        var state = corpusState.TryGetProperty("embedding_state", out var embedding)
            ? embedding.Deserialize<CorpusEmbeddingState>(WireJson.Options)!
            : throw new InvalidOperationException(
                "Nothing has been embedded: /corpus-state has no embedding_state. Run ingest first.");

        var scores = await Task.WhenAll(fixture.CardSearch
            .Select(async query => RetrievalEval.ScoreCards(query, await PostAsync(
                client, "/cards/semantic-search", RetrievalEval.CardSearchBody(query, RetrievalEval.RecallK))))
            .Concat(fixture.RulesSearch.Select(async query => RetrievalEval.ScoreRules(query, await PostAsync(
                client, "/rules/search", new { query = query.Query, k = RetrievalEval.RecallK })))));

        // The glossary pass: the archetype-vocab subset again,
        // through the plugin's own expansion rule, beside the bare scores.
        var glossary = ArchetypeGlossary.Load();
        var expanded = await Task.WhenAll(fixture.CardSearch
            .Where(query => query.ArchetypeVocab)
            .Select(query => (Query: query, Expansion: ArchetypeGlossary.Expand(query.Query, glossary)))
            .Where(pair => pair.Expansion is not null)
            .Select(async pair =>
            {
                var (query, expansion) = (pair.Query, pair.Expansion!);
                var pulls = await Task.WhenAll(expansion.Entry.Tags
                    .Select(tag => PostAsync(client, "/cards/search", Pull(query, new { tags = new[] { tag } })))
                    .Concat(expansion.Entry.Types
                        .Select(type => PostAsync(client, "/cards/search", Pull(query, new { types = new[] { type } })))));
                var semantic = await PostAsync(client, "/cards/semantic-search", RetrievalEval.CardSearchBody(
                    query with { Query = expansion.Query }, ArchetypeGlossary.PullLimit));
                return RetrievalEval.ScoreExpanded(query, expansion, pulls, semantic);
            }));

        var result = new RetrievalEval.Result(
            state, DateTimeOffset.UtcNow, client.BaseAddress.ToString(), RetrievalEval.Summaries(scores, expanded), scores,
            Label: Environment.GetEnvironmentVariable("MTG_ARCHIVE_EVAL_LABEL"),
            Note: Environment.GetEnvironmentVariable("MTG_ARCHIVE_EVAL_NOTE"),
            ExpandedQueries: expanded);
        var resultsDir = Directory.CreateDirectory(Path.Combine(evalDir, "results"));
        var path = Path.Combine(resultsDir.FullName, RetrievalEval.FileName(result));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, Pretty));

        output.WriteLine($"{state} -> {path}");
        foreach (var (subset, summary) in result.Summary)
        {
            output.WriteLine(
                $"{subset}: nDCG@10 {summary.NdcgAt10:F3}, recall@30 {summary.RecallAt30:F3} ({summary.Queries} queries)");
        }

        foreach (var score in expanded)
        {
            var bare = scores.Single(s => s.Id == score.Id);
            output.WriteLine(
                $"{score.Id} {score.Term}: nDCG@10 {bare.NdcgAt10:F3} -> {score.NdcgAt10:F3}, "
                + $"recall@30 {bare.RecallAt30:F3} -> {score.RecallAt30:F3}");
        }

        var results = resultsDir.EnumerateFiles("*.json")
            .Select(f => JsonSerializer.Deserialize<RetrievalEval.Result>(File.ReadAllText(f.FullName), WireJson.Options)!)
            .ToList();
        output.WriteLine(RetrievalEval.Comparison(results));
    }

    /// <summary>One structured pull's body: the fixture's filters, the one
    /// tag or type, and the glossary's page size; no query.</summary>
    private static Dictionary<string, JsonElement> Pull(RetrievalEval.CardSearchQuery query, object filter)
    {
        var body = new Dictionary<string, JsonElement>
        {
            ["limit"] = JsonSerializer.SerializeToElement(ArchetypeGlossary.PullLimit),
        };
        foreach (var member in query.Filters?.EnumerateObject() ?? default)
        {
            body[member.Name] = member.Value;
        }

        foreach (var member in JsonSerializer.SerializeToElement(filter, WireJson.Options).EnumerateObject())
        {
            body[member.Name] = member.Value;
        }

        return body;
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
        => await ReadAsync(await client.PostAsJsonAsync(path, body, WireJson.Options));

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return response.IsSuccessStatusCode
            ? JsonDocument.Parse(body).RootElement
            : throw new InvalidOperationException(
                $"{response.RequestMessage!.Method} {response.RequestMessage.RequestUri} -> {(int)response.StatusCode}: {body}");
    }
}
