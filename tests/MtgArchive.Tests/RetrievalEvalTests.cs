using System.Text.Json;
using MtgArchive.Api;
using MtgArchive.Embedding;

namespace MtgArchive.Tests;

/// <summary>
/// The Retrieval eval's pure parts: the metric math against
/// hand-computed examples, response scoring, the result file name, and the
/// two-model comparison. These run in the normal suite; only the live eval
/// in <see cref="RetrievalEvalLiveTests"/> is trait-gated.
/// </summary>
public sealed class RetrievalEvalTests
{
    [Fact]
    public void Ndcg_at_10_matches_a_hand_computed_example()
    {
        // Grades a=3, b=2, c=1; retrieved b, x, a, c:
        //   DCG  = 2/log2(2) + 0/log2(3) + 3/log2(4) + 1/log2(5) = 3.9307
        //   IDCG = 3/log2(2) + 2/log2(3) + 1/log2(4)             = 4.7619
        //   nDCG = 3.9307 / 4.7619                                = 0.8254
        var grades = new Dictionary<string, int> { ["a"] = 3, ["b"] = 2, ["c"] = 1 };

        Assert.Equal(0.8254, RetrievalEval.NdcgAt10(["b", "x", "a", "c"], grades), 4);
        Assert.Equal(1, RetrievalEval.NdcgAt10(["a", "b", "c"], grades), 4);
    }

    [Fact]
    public void Ndcg_ignores_hits_past_10()
    {
        var grades = new Dictionary<string, int> { ["a"] = 3 };
        var ranked = Enumerable.Range(0, 10).Select(i => $"x{i}").Append("a").ToList();

        Assert.Equal(0, RetrievalEval.NdcgAt10(ranked, grades));
    }

    [Fact]
    public void Recall_at_30_is_the_share_of_relevant_ids_in_the_top_30()
    {
        var ranked = Enumerable.Range(1, 40)
            .Select(i => i == 1 ? "a" : i == 30 ? "c" : i == 31 ? "d" : $"x{i}")
            .ToList();

        Assert.Equal(0.5, RetrievalEval.RecallAt30(ranked, ["a", "b", "c", "d"]));
    }

    [Theory]
    [InlineData("903.10a", "903.10")]
    [InlineData("704.5aa", "704.5")]
    [InlineData("119.1d.", "119.1")]
    [InlineData("Haste", "Haste")]
    public void A_rules_expectation_matches_the_chunk_or_term_a_hit_cites(string expected, string cited)
    {
        var query = new RetrievalEval.RulesSearchQuery("rs-001", "commander damage", [expected]);
        var response = Response("hits", "citation", "rule", (cited, cited), ("903.11", "903.11"));

        var score = RetrievalEval.ScoreRules(query, response);

        Assert.Equal(1, score.NdcgAt10, 4);
        Assert.Equal(1, score.RecallAt30);
        Assert.Equal(SearchSurface.RulesSearch, score.Surface);
        Assert.Null(score.ArchetypeVocab);
    }

    [Fact]
    public void Curation_slips_are_named_before_any_query_runs()
    {
        var twice = Guid.NewGuid();
        var fixture = new RetrievalEval.Fixture(
            [
                new("cs-001", "treasure", false, [new(Guid.NewGuid(), "Sol Ring", 0)]),
                new("cs-002", "treasure", false, [new(twice, "Ruthless Knave", 3), new(twice, "Ruthless Knave", 2)]),
                new("cs-003", "treasure", false, [new(Guid.NewGuid(), "Ruthless Knave", 1)]),
            ],
            [new("rs-001", "commander damage", []), new("rs-002", "commander damage", ["903.10a"])]);

        Assert.Equal(
            [
                "cs-001: no expected card graded above 0.",
                $"cs-002: Ruthless Knave ({twice}) is expected 2 times.",
                "rs-001: no expected rule.",
            ],
            RetrievalEval.Problems(fixture));
    }

    [Fact]
    public void Card_hits_score_by_oracle_id_and_keep_the_top_hits_for_review()
    {
        var wanted = Guid.NewGuid();
        var graded0 = Guid.NewGuid();
        var query = new RetrievalEval.CardSearchQuery(
            "cs-001", "treasure", true, [new(wanted, "Ruthless Knave", 3), new(graded0, "Sol Ring", 0)]);
        var response = Response(
            "candidates", "card", "card", (graded0.ToString(), "Sol Ring"), (wanted.ToString(), "Ruthless Knave"));

        var score = RetrievalEval.ScoreCards(query, response);

        // The one relevant card sits at rank 2: 3/log2(3) over an ideal of 3.
        // The grade-0 card at rank 1 adds nothing and is not a recall target.
        Assert.Equal(1 / Math.Log2(3), score.NdcgAt10, 4);
        Assert.Equal(1, score.RecallAt30);
        Assert.Equal(SearchSurface.CardSearch, score.Surface);
        Assert.True(score.ArchetypeVocab);
        Assert.Equal(["Sol Ring", "Ruthless Knave"], score.TopHits.Select(c => c.Label));
    }

    [Fact]
    public void Fixture_filters_spread_into_the_request_body_verbatim()
    {
        var fixture = JsonSerializer.Deserialize<RetrievalEval.Fixture>(
            """
            {"card_search":[
              {"id":"cs-012","query":"untapped rainbow land","archetype_vocab":false,
               "filters":{"enters_tapped":"untapped","types":["Land"]},
               "expected":[{"oracle_id":"0895c9b7-ae7d-4bb3-af17-3b75deb50a25","name":"Command Tower","grade":3}]},
              {"id":"cs-001","query":"treasure","archetype_vocab":true,
               "expected":[{"oracle_id":"0895c9b7-ae7d-4bb3-af17-3b75deb50a25","name":"Sol Ring","grade":3}]}
            ],"rules_search":[]}
            """,
            WireJson.Options)!;

        var filtered = JsonSerializer.SerializeToElement(RetrievalEval.CardSearchBody(fixture.CardSearch[0], 60), WireJson.Options);
        var plain = JsonSerializer.SerializeToElement(RetrievalEval.CardSearchBody(fixture.CardSearch[1], 60), WireJson.Options);

        Assert.Equal("untapped rainbow land", filtered.GetProperty("query").GetString());
        Assert.Equal(60, filtered.GetProperty("k").GetInt32());
        Assert.Equal("untapped", filtered.GetProperty("enters_tapped").GetString());
        Assert.Equal(["Land"], filtered.GetProperty("types").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(["query", "k"], plain.EnumerateObject().Select(m => m.Name));
    }

    [Fact]
    public void A_disabled_semantic_response_is_a_hard_error_never_zeros()
    {
        var query = new RetrievalEval.RulesSearchQuery("rs-001", "commander damage", ["903.10a"]);
        var response = JsonDocument.Parse(
            """{"semantic":{"enabled":false,"disabled_reason":"Stored vectors were produced under nomic-embed-text"},"hits":[]}""")
            .RootElement;

        var error = Assert.Throws<InvalidOperationException>(() => RetrievalEval.ScoreRules(query, response));

        Assert.Contains("Stored vectors were produced under nomic-embed-text", error.Message);
    }

    [Fact]
    public void The_model_tag_sanitizes_into_the_result_file_name()
    {
        var result = Result(EmbeddingModel.Qwen3, new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero), 0.5);

        Assert.Equal("2026-08-30-qwen3-embedding-0.6b.json", RetrievalEval.FileName(result));
        Assert.Equal("qwen3-embedding:0.6b", result.EmbeddingState.ModelTag);
    }

    [Theory]
    [InlineData(0.62, 0.58, "Verdict: qwen3-embedding:0.6b holds the model slot.")]
    [InlineData(0.58, 0.58, "Verdict: nomic-embed-text wins the model slot.")]
    public void The_comparison_states_the_gate_rule_and_its_verdict(double qwen3, double nomic, string verdict)
    {
        var now = DateTimeOffset.UtcNow;
        RetrievalEval.Result[] results =
        [
            Result(EmbeddingModel.Qwen3, now, qwen3),
            Result(EmbeddingModel.Nomic, now, nomic),
            // An older qwen3 run: the newest per model is what compares.
            Result(EmbeddingModel.Qwen3, now.AddDays(-1), 0.1),
            // A newer ablation cell: a labeled run never compares.
            Result(EmbeddingModel.Qwen3, now.AddDays(1), 0.1) with { Label = "strip-reminder-text" },
        ];

        var table = RetrievalEval.Comparison(results);

        Assert.Contains("Gate: qwen3-embedding:0.6b must beat nomic-embed-text on overall nDCG@10", table);
        Assert.Contains(verdict, table);
        Assert.Contains($"| overall | 1 | {qwen3:F3} | {nomic:F3} |", table);
    }

    [Fact]
    public void One_model_alone_states_the_gate_and_names_the_missing_pass()
    {
        var table = RetrievalEval.Comparison([Result(EmbeddingModel.Qwen3, DateTimeOffset.UtcNow, 0.5)]);

        Assert.Contains("Gate: qwen3-embedding:0.6b must beat nomic-embed-text on overall nDCG@10", table);
        Assert.Contains("the nomic-embed-text pass is missing", table);
        Assert.DoesNotContain("Verdict", table);
    }

    private static JsonElement Response(string list, string citation, string type, params (string Id, string Label)[] hits)
    {
        var payload = new Dictionary<string, object>
        {
            ["semantic"] = new { enabled = true },
            [list] = hits.Select(h => new Dictionary<string, object>
            {
                [citation] = new { type, id = h.Id, label = h.Label },
            }),
        };
        return JsonSerializer.SerializeToElement(payload);
    }

    private static RetrievalEval.Result Result(EmbeddingModel model, DateTimeOffset runAt, double ndcg)
    {
        var score = new RetrievalEval.QueryScore("cs-001", SearchSurface.CardSearch, false, ndcg, 1, []);
        return new(model.State, runAt, "http://127.0.0.1:8080", RetrievalEval.Summaries([score]), [score]);
    }
}
