using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Data;
using MtgArchive.Embedding;

namespace MtgArchive.Rules;

/// <summary>
/// GET /rules/{number} — the resolution target for rule citations. Serves
/// one CR rule chunk or glossary entry verbatim with the CR effective date
/// it belongs to. <c>{number}</c> is a rule number ("601.2"; a subrule
/// number like "601.2b" resolves to the chunk containing it) or an exact
/// glossary term.
/// </summary>
public sealed partial class RulesEndpoints : IEndpointMapper
{
    // Rule numbers, including the file's anomalies: an optional one- or
    // two-letter subrule suffix ("704.5aa") and an optional trailing dot
    // ("119.1d."). A bare section number ("601") matches no chunk and is
    // unknown; anything not number-shaped is a glossary term.
    [GeneratedRegex(@"^(?<chunk>\d+\.\d+)(?<subrule>[a-z]{1,2})?\.?$")]
    private static partial Regex RuleNumber();

    private const int MaxK = 200;

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/rules/{number}", async Task<Results<Ok<RuleEntryResponse>, ProblemHttpResult>> (
            string number,
            ArchiveDbContext db,
            CancellationToken cancellationToken) =>
        {
            // The chunk row carries its own CR effective date, so the text
            // and the echoed date are one atomic read — a concurrent CR
            // replace can never mismatch them. Before the first CR ingest
            // every lookup is an unknown rule.
            var entry = await LookupAsync(db, number, cancellationToken);
            return entry is null
                ? TypedResults.Problem(Problems.UnknownRule(number))
                : TypedResults.Ok(entry);
        });

        app.MapPost("/rules/search", async Task<Results<Ok<RulesSearchResponse>, ProblemHttpResult>> (
            RulesSearchRequest request,
            ArchiveDbContext db,
            EmbeddingStateGate gate,
            OllamaEmbedder embedder,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return TypedResults.Problem(Problems.MissingParameter("query"));
            }

            // Disabled is report content, not an error: the vectors at rest
            // were not produced under the configured model and prefix set.
            var status = await gate.CheckAsync(cancellationToken);
            if (!status.Enabled)
            {
                return TypedResults.Ok(new RulesSearchResponse(status, []));
            }

            var k = Math.Clamp(request.K ?? 50, 1, MaxK);
            var query = await embedder.EmbedQueryAsync(SearchSurface.RulesSearch, request.Query, cancellationToken);
            // One statement over the whole rules-search corpus: each chunk
            // table takes its own k nearest through its HNSW index, and the
            // union keeps the k nearest overall. No legality, no fusion, no
            // re-ranker — the consuming agent re-ranks.
            var rows = await db.Database.SqlQuery<RulesSearchRow>(
                $"""
                SELECT * FROM (
                    (SELECT 'rule' AS kind, number AS id, text, cr_effective_date, embedding <=> {query} AS distance
                     FROM cr_rule_chunk WHERE embedding IS NOT NULL ORDER BY embedding <=> {query} LIMIT {k})
                    UNION ALL
                    (SELECT 'glossary', term, text, cr_effective_date, embedding <=> {query}
                     FROM cr_glossary_chunk WHERE embedding IS NOT NULL ORDER BY embedding <=> {query} LIMIT {k})
                ) AS hits
                ORDER BY distance
                LIMIT {k}
                """).ToListAsync(cancellationToken);

            return TypedResults.Ok(new RulesSearchResponse(
                status,
                [.. rows.Select(r => new RulesSearchHit(
                    Enum.Parse<RuleEntryKind>(r.Kind, ignoreCase: true),
                    new Citation(CitationType.Rule, r.Id, r.Id),
                    r.Text,
                    r.Distance))])
            {
                Versions = new VersionsBlock { CrEffectiveDate = rows.FirstOrDefault()?.CrEffectiveDate.ToString("O") },
            });
        });
    }

    private static async Task<RuleEntryResponse?> LookupAsync(
        ArchiveDbContext db, string number, CancellationToken cancellationToken)
    {
        var rule = RuleNumber().Match(number);
        if (rule.Success)
        {
            var chunkNumber = rule.Groups["chunk"].Value;
            var chunk = await db.CrRuleChunks.AsNoTracking()
                .SingleOrDefaultAsync(c => c.Number == chunkNumber, cancellationToken);
            if (chunk is null
                || (rule.Groups["subrule"].Success
                    && !ContainsSubrule(chunk.Text, chunkNumber + rule.Groups["subrule"].Value)))
            {
                return null;
            }

            return new RuleEntryResponse(RuleEntryKind.Rule, chunk.Number, null, chunk.Text)
            {
                Versions = new VersionsBlock { CrEffectiveDate = chunk.CrEffectiveDate.ToString("O") },
            };
        }

        var entry = await db.CrGlossaryChunks.AsNoTracking()
            .SingleOrDefaultAsync(g => g.Term == number, cancellationToken);
        return entry is null
            ? null
            : new RuleEntryResponse(RuleEntryKind.Glossary, null, entry.Term, entry.Text)
            {
                Versions = new VersionsBlock { CrEffectiveDate = entry.CrEffectiveDate.ToString("O") },
            };
    }

    /// <summary>A cited subrule must exist in the chunk, or the citation is
    /// unknown: a line opens with the subrule number followed by a space or
    /// the "119.1d." anomaly's period.</summary>
    private static bool ContainsSubrule(string chunkText, string subruleNumber)
        => chunkText.Split('\n').Any(line =>
            line.StartsWith(subruleNumber, StringComparison.Ordinal)
            && line.Length > subruleNumber.Length
            && line[subruleNumber.Length] is ' ' or '.');
}

/// <summary>One CR rule chunk or glossary entry, verbatim. <c>Number</c> is
/// set for rules, <c>Term</c> for glossary entries.</summary>
public sealed record RuleEntryResponse(
    RuleEntryKind Kind,
    string? Number,
    string? Term,
    string Text)
{
    public VersionsBlock Versions { get; init; } = new();
}

public enum RuleEntryKind
{
    Rule,
    Glossary,
}

/// <summary>POST /rules/search body: the rules Search surface. The query is
/// embedded service-side with the rules-search instruction and the current
/// model's query prefix.</summary>
public sealed record RulesSearchRequest
{
    [Description("The rules question, in natural language. The service trims it and unifies CRLF to LF before embedding. "
        + "Other interior whitespace stays.")]
    public string? Query { get; init; }

    [Description("How many nearest chunks to return: default 50, capped at 200.")]
    public int? K { get; init; }
}

/// <summary>Hits over rule chunks and glossary entries together, nearest
/// first, with the CR effective date the served chunks belong to. When
/// <c>semantic.enabled</c> is false the vectors at rest do not match the
/// configured model and prefix set, <c>hits</c> is empty, and
/// <c>semantic.disabled_reason</c> says why — report content, not an
/// error.</summary>
public sealed record RulesSearchResponse(SemanticQueryStatus Semantic, IReadOnlyList<RulesSearchHit> Hits)
{
    public VersionsBlock Versions { get; init; } = new();
}

/// <summary>One chunk, verbatim, cited as <c>{ type: rule, id, label }</c>
/// (id resolves via <c>GET /rules/{number}</c>) with its cosine distance
/// from the query.</summary>
public sealed record RulesSearchHit(RuleEntryKind Kind, Citation Citation, string Text, double Distance);

/// <summary>The KNN statement's row shape (an EF ad-hoc query type).</summary>
internal sealed class RulesSearchRow
{
    public required string Kind { get; init; }
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required DateOnly CrEffectiveDate { get; init; }
    public required double Distance { get; init; }
}
