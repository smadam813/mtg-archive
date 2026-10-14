using System.Text.Json;
using System.Text.RegularExpressions;
using MtgArchive.Api;
using MtgArchive.Embedding;

namespace MtgArchive.Tests;

/// <summary>
/// The Retrieval eval (GLOSSARY.md): the fixture's graded queries scored
/// nDCG@10 and recall@30 through the real service path. Everything here is
/// pure — fixture shape, metric math, response scoring, result shape, and
/// the two-model comparison — so the normal suite covers it; only
/// <see cref="RetrievalEvalLiveTests"/> talks to a live service.
/// </summary>
public static partial class RetrievalEval
{
    public const int NdcgK = 10;

    public const int RecallK = 30;

    /// <summary>The rule the follow-up human issue applies; every output states it.</summary>
    private static readonly string GateRule =
        $"Gate: {EmbeddingModel.Qwen3.Tag} must beat {EmbeddingModel.Nomic.Tag} on overall nDCG@10 "
        + "or the fallback wins the model slot.";

    /// <summary>eval/retrieval/queries.json, consumed as curated.</summary>
    public sealed record Fixture(IReadOnlyList<CardSearchQuery> CardSearch, IReadOnlyList<RulesSearchQuery> RulesSearch);

    /// <summary><paramref name="Filters"/> is an optional object of
    /// structured <c>POST /cards/semantic-search</c> filters (<c>types</c>,
    /// <c>enters_tapped</c>, …) the live eval sends verbatim beside the
    /// query; null sends the query alone.</summary>
    public sealed record CardSearchQuery(
        string Id, string Query, bool ArchetypeVocab, IReadOnlyList<ExpectedCard> Expected, JsonElement? Filters = null);

    /// <summary>The request body for one card-search query: the query, k,
    /// and the fixture's filters spread in beside them.</summary>
    public static Dictionary<string, JsonElement> CardSearchBody(CardSearchQuery query, int k)
    {
        var body = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(query.Query),
            ["k"] = JsonSerializer.SerializeToElement(k),
        };
        foreach (var filter in query.Filters?.EnumerateObject() ?? default)
        {
            body[filter.Name] = filter.Value;
        }

        return body;
    }

    public sealed record ExpectedCard(Guid OracleId, string Name, int Grade);

    public sealed record RulesSearchQuery(string Id, string Query, IReadOnlyList<string> ExpectedRules);

    /// <summary>One scored query; <c>top_hits</c> is the top 10 that came
    /// back, for a human judging the archetype-vocabulary subset.</summary>
    public sealed record QueryScore(
        string Id,
        SearchSurface Surface,
        bool? ArchetypeVocab,
        double NdcgAt10,
        double RecallAt30,
        IReadOnlyList<Citation> TopHits);

    /// <summary>One archetype-vocab query scored through the glossary pass
    /// (<see cref="ArchetypeGlossary"/>): the term that matched, the tags
    /// and types pulled, the query sent, and the merged top 10 for review.
    /// The bare score of the same query stays in <c>queries</c>.</summary>
    public sealed record ExpandedQueryScore(
        string Id,
        string Term,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Types,
        string Query,
        double NdcgAt10,
        double RecallAt30,
        IReadOnlyList<Citation> TopHits);

    public sealed record SubsetSummary(int Queries, double NdcgAt10, double RecallAt30);

    /// <summary>One run's result file.</summary>
    /// <summary>One eval run. <paramref name="Label"/> names an ablation
    /// cell in the file name. <paramref name="Note"/> records what the state
    /// alone cannot, such as the composition and the compose overlay the
    /// re-embed ran on. Both are null on a plain regression run.</summary>
    /// <paramref name="ExpandedQueries"/> is the glossary pass over the
    /// archetype-vocab subset, beside the bare scores and never in their
    /// place; a file from before the pass has none.</summary>
    public sealed record Result(
        CorpusEmbeddingState EmbeddingState,
        DateTimeOffset RunAt,
        string ServiceUrl,
        IReadOnlyDictionary<string, SubsetSummary> Summary,
        IReadOnlyList<QueryScore> Queries,
        string? Label = null,
        string? Note = null,
        IReadOnlyList<ExpandedQueryScore>? ExpandedQueries = null);

    /// <summary>Linear gain (grade / log2(rank + 1), the trec_eval
    /// convention); the ideal ranking is the grades themselves, best
    /// first.</summary>
    public static double NdcgAt10(IReadOnlyList<string> ranked, IReadOnlyDictionary<string, int> grades)
        => Dcg(ranked.Take(NdcgK).Select(id => grades.GetValueOrDefault(id)))
            / Dcg(grades.Values.OrderDescending().Take(NdcgK));

    private static double Dcg(IEnumerable<int> gains) => gains.Select((gain, i) => gain / Math.Log2(i + 2)).Sum();

    public static double RecallAt30(IReadOnlyList<string> ranked, IReadOnlyCollection<string> relevant)
        => ranked.Take(RecallK).Count(relevant.Contains) / (double)relevant.Count;

    public static QueryScore ScoreCards(CardSearchQuery query, JsonElement response)
        => Score(SearchSurface.CardSearch, query.Id, query.ArchetypeVocab, response,
            query.Expected.ToDictionary(e => e.OracleId.ToString(), e => e.Grade));

    /// <summary>A subrule expectation ("903.10a") lives in its rule's chunk
    /// ("903.10"), which is what a hit cites; a glossary term is cited as
    /// itself. Rules relevance is binary.</summary>
    public static QueryScore ScoreRules(RulesSearchQuery query, JsonElement response)
        => Score(SearchSurface.RulesSearch, query.Id, null, response,
            query.ExpectedRules.Select(r => SubruleNumber().Replace(r, "$1")).Distinct().ToDictionary(n => n, _ => 1));

    /// <summary>Curation slips: a query with nothing relevant has no nDCG or
    /// recall, and a card expected twice has no single grade.</summary>
    public static IEnumerable<string> Problems(Fixture fixture)
    {
        foreach (var query in fixture.CardSearch)
        {
            if (!query.Expected.Any(e => e.Grade > 0))
            {
                yield return $"{query.Id}: no expected card graded above 0.";
            }

            foreach (var twice in query.Expected.GroupBy(e => e.OracleId).Where(g => g.Count() > 1))
            {
                yield return $"{query.Id}: {twice.First().Name} ({twice.Key}) is expected {twice.Count()} times.";
            }
        }

        foreach (var query in fixture.RulesSearch.Where(q => q.ExpectedRules.Count == 0))
        {
            yield return $"{query.Id}: no expected rule.";
        }
    }

    /// <summary>The glossary pass for one card query. The pulls (one
    /// <c>POST /cards/search</c> page per mapped tag or type) merge ahead
    /// of the semantic hits by the file's rule. The score is the bare
    /// pass's metrics over that order.</summary>
    public static ExpandedQueryScore ScoreExpanded(
        CardSearchQuery query,
        ArchetypeGlossary.Expansion expansion,
        IReadOnlyList<JsonElement> pulls,
        JsonElement semantic)
    {
        var hits = MergeExpanded(pulls, SemanticHits(semantic));
        var grades = query.Expected.ToDictionary(e => e.OracleId.ToString(), e => e.Grade);
        var (ndcg, recall) = Metrics(hits, grades);
        var entry = expansion.Entry;
        return new(query.Id, entry.Term, entry.Tags, entry.Types, expansion.Query, ndcg, recall, [.. hits.Take(NdcgK)]);
    }

    /// <summary>Pulled cards first, best <c>popularity_rank</c> first across
    /// the pulls (the format's popularity order, which ranks every card, so
    /// cards with no <c>edhrec_rank</c> come in name order). A card with no
    /// echoed rank goes last among them. The semantic hits follow nearest
    /// first. A card appears once.</summary>
    public static IReadOnlyList<Citation> MergeExpanded(IReadOnlyList<JsonElement> pulls, IReadOnlyList<Citation> semantic)
    {
        var pulled = pulls
            .SelectMany(page => page.GetProperty("candidates").EnumerateArray())
            .Select(hit => (
                Card: hit.GetProperty("card").Deserialize<Citation>(WireJson.Options)!,
                Rank: hit.TryGetProperty("popularity_rank", out var rank) && rank.ValueKind == JsonValueKind.Number
                    ? rank.GetInt32()
                    : int.MaxValue))
            .GroupBy(hit => hit.Card.Id)
            .Select(group => group.MinBy(hit => hit.Rank))
            .OrderBy(hit => hit.Rank)
            .Select(hit => hit.Card);
        return [.. pulled.Concat(semantic).DistinctBy(c => c.Id)];
    }

    private static QueryScore Score(
        SearchSurface surface, string id, bool? archetypeVocab, JsonElement response, Dictionary<string, int> grades)
    {
        var hits = surface == SearchSurface.CardSearch
            ? SemanticHits(response)
            : SemanticHits(response, "hits", "citation");
        var (ndcg, recall) = Metrics(hits, grades);
        return new(id, surface, archetypeVocab, ndcg, recall, [.. hits.Take(NdcgK)]);
    }

    private static (double NdcgAt10, double RecallAt30) Metrics(IReadOnlyList<Citation> hits, Dictionary<string, int> grades)
    {
        var ranked = hits.Select(h => h.Id).ToList();
        var relevant = grades.Where(g => g.Value > 0).Select(g => g.Key).ToList();
        return (NdcgAt10(ranked, grades), RecallAt30(ranked, relevant));
    }

    /// <summary>A disabled answer is an embedding-state mismatch: a hard
    /// error, never zeros.</summary>
    private static IReadOnlyList<Citation> SemanticHits(
        JsonElement response, string list = "candidates", string citation = "card")
    {
        var semantic = response.GetProperty("semantic");
        if (!semantic.GetProperty("enabled").GetBoolean())
        {
            throw new InvalidOperationException(
                "Semantic queries are disabled: " + semantic.GetProperty("disabled_reason").GetString());
        }

        return [.. response.GetProperty(list).EnumerateArray()
            .Select(hit => hit.GetProperty(citation).Deserialize<Citation>(WireJson.Options)!)];
    }

    /// <summary>The bare subsets as always, plus
    /// <c>card_search_archetype_vocab_expanded</c> for the glossary pass
    /// when it ran. The bare keys never change meaning: the ADR 0007 gate
    /// reads them.</summary>
    public static IReadOnlyDictionary<string, SubsetSummary> Summaries(
        IReadOnlyList<QueryScore> scores, IReadOnlyList<ExpandedQueryScore>? expanded = null)
    {
        var subsets = new Dictionary<string, Func<QueryScore, bool>>
        {
            ["overall"] = _ => true,
            ["card_search"] = s => s.Surface == SearchSurface.CardSearch,
            ["card_search_archetype_vocab"] = s => s.ArchetypeVocab == true,
            ["card_search_other"] = s => s.ArchetypeVocab == false,
            ["rules_search"] = s => s.Surface == SearchSurface.RulesSearch,
        };
        var summary = subsets
            .Select(subset => (subset.Key, Scores: scores.Where(subset.Value).ToList()))
            .Where(subset => subset.Scores.Count > 0)
            .ToDictionary(
                subset => subset.Key,
                subset => new SubsetSummary(
                    subset.Scores.Count, subset.Scores.Average(s => s.NdcgAt10), subset.Scores.Average(s => s.RecallAt30)));
        if (expanded is { Count: > 0 })
        {
            summary["card_search_archetype_vocab_expanded"] = new SubsetSummary(
                expanded.Count, expanded.Average(s => s.NdcgAt10), expanded.Average(s => s.RecallAt30));
        }

        return summary;
    }

    /// <summary>"qwen3-embedding:0.6b" → "2026-08-30-qwen3-embedding-0.6b.json":
    /// a colon is not a legal filename character on Windows.</summary>
    public static string FileName(Result result)
    {
        var model = UnsafeInFileName().Replace(result.EmbeddingState.ModelTag, "-");
        var label = result.Label is null ? "" : $"-{UnsafeInFileName().Replace(result.Label, "-")}";
        return $"{result.RunAt:yyyy-MM-dd}-{model}{label}.json";
    }

    /// <summary>The newest plain run per model compares. A labeled run is
    /// an ablation cell, never a regression pass.</summary>
    public static string Comparison(IReadOnlyList<Result> results)
    {
        var latest = results
            .Where(r => r.Label is null)
            .GroupBy(r => r.EmbeddingState.ModelTag)
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.RunAt)!);
        var (qwen3Tag, nomicTag) = (EmbeddingModel.Qwen3.Tag, EmbeddingModel.Nomic.Tag);
        var qwen3 = latest.GetValueOrDefault(qwen3Tag);
        var nomic = latest.GetValueOrDefault(nomicTag);
        if (qwen3 is null || nomic is null)
        {
            return $"{GateRule} No comparison yet: the {(qwen3 is null ? qwen3Tag : nomicTag)} pass is missing.";
        }

        var lines = new List<string>
        {
            $"| subset | queries | nDCG@10 {qwen3Tag} | nDCG@10 {nomicTag} | recall@30 {qwen3Tag} | recall@30 {nomicTag} |",
            "|---|---|---|---|---|---|",
        };
        foreach (var (subset, qwen3Summary) in qwen3.Summary)
        {
            if (nomic.Summary.TryGetValue(subset, out var nomicSummary))
            {
                lines.Add($"| {subset} | {qwen3Summary.Queries} | {qwen3Summary.NdcgAt10:F3} | {nomicSummary.NdcgAt10:F3} "
                    + $"| {qwen3Summary.RecallAt30:F3} | {nomicSummary.RecallAt30:F3} |");
            }
        }

        var (qwen3Ndcg, nomicNdcg) = (qwen3.Summary["overall"].NdcgAt10, nomic.Summary["overall"].NdcgAt10);
        lines.Add("");
        lines.Add($"{GateRule} {qwen3Ndcg:F3} vs {nomicNdcg:F3}. "
            + $"Verdict: {(qwen3Ndcg > nomicNdcg ? $"{qwen3Tag} holds" : $"{nomicTag} wins")} the model slot.");
        return string.Join('\n', lines);
    }

    // The service's rule-number shape (RulesEndpoints.RuleNumber): a one- or
    // two-letter subrule suffix and an optional trailing dot. Anything else
    // is a glossary term.
    [GeneratedRegex(@"^(\d+\.\d+)[a-z]{1,2}\.?$")]
    private static partial Regex SubruleNumber();

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeInFileName();
}
