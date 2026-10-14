using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Banlist;
using MtgArchive.Data;
using MtgArchive.Embedding;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace MtgArchive.Cards;

/// <summary>
/// The two card query endpoints over the Index. <c>POST /cards/search</c>
/// is structured filters only — no embedding call anywhere on its path.
/// <c>POST /cards/semantic-search</c> is the card Search surface: the same
/// filters composed with a pgvector KNN over card face documents in one
/// SQL statement (<c>... FROM legal_candidates(...) WHERE &lt;filters&gt;
/// ORDER BY min(embedding &lt;=&gt; :q) LIMIT :k</c>), faces collapsed to
/// cards before the LIMIT. Both start from <see cref="CandidateQuery"/>, so
/// legality and collection scope are hard filters at the boundary and stale
/// cards never appear. Hits are lean — no rulings; those live on card detail.
/// </summary>
public sealed class CardSearchEndpoints : IEndpointMapper
{
    private const int DefaultLimit = 50;

    internal const int MaxLimit = 200;

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/cards/search", async Task<Results<Ok<CardSearchResponse>, ProblemHttpResult>> (
            CardSearchRequest request,
            ArchiveDbContext db,
            BanlistVersionAxis banlist,
            CancellationToken cancellationToken) =>
        {
            if (request.Limit < 1)
            {
                return TypedResults.Problem(Problems.ParameterBelowOne("limit", request.Limit.Value));
            }

            if (request.OracleIds?.Length > MaxLimit)
            {
                return TypedResults.Problem(Problems.ParameterAboveMaximum("oracle_ids", request.OracleIds.Length, MaxLimit));
            }

            var query = await CandidateQuery.ResolveAsync(db, banlist, request, cancellationToken);
            var scoped = request.CollectionScope;
            // The format's popularity order. Every pool card has a rank once
            // an ingest derives the order; until then (and for a card the
            // order missed) edhrec_rank, then name and oracle id, order the
            // rest. Each nulls-last is explicit.
            var rows = await Profiled(query.Candidates, db.SourceProfiles, db.TagAliases, query.Popularity, request)
                .OrderBy(x => x.PopularityRank == null).ThenBy(x => x.PopularityRank)
                .ThenBy(x => x.Card.EdhrecRank == null).ThenBy(x => x.Card.EdhrecRank)
                .ThenBy(x => x.Card.Name).ThenBy(x => x.Card.OracleId)
                .Skip(Math.Max(0, request.Offset))
                .Take(Math.Min(request.Limit ?? DefaultLimit, MaxLimit))
                .Select(x => new Row(
                    x.Card,
                    x.EntersTapped,
                    scoped ? ArchiveDbContext.AvailableCopies(x.Card.CopyLimit, x.Card.OracleId) : null,
                    null,
                    null,
                    x.PopularityRank))
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            return TypedResults.Ok(new CardSearchResponse([.. rows.Select(r => r.Hit(request.Role))], query.Versions));
        });

        app.MapPost("/cards/semantic-search", async Task<Results<Ok<CardSemanticSearchResponse>, ProblemHttpResult>> (
            CardSemanticSearchRequest request,
            ArchiveDbContext db,
            BanlistVersionAxis banlist,
            EmbeddingStateGate gate,
            OllamaEmbedder embedder,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return TypedResults.Problem(Problems.MissingParameter("query"));
            }

            if (request.K < 1)
            {
                return TypedResults.Problem(Problems.ParameterBelowOne("k", request.K.Value));
            }

            if (request.OracleIds?.Length > MaxLimit)
            {
                return TypedResults.Problem(Problems.ParameterAboveMaximum("oracle_ids", request.OracleIds.Length, MaxLimit));
            }

            // The pin resolves first so an unknown version is a 400 even
            // while semantic queries are disabled.
            var query = await CandidateQuery.ResolveAsync(db, banlist, request, cancellationToken);
            // Disabled is report content, not an error: the vectors at rest
            // were not produced under the configured model and prefix set.
            var status = await gate.CheckAsync(cancellationToken);
            if (!status.Enabled)
            {
                return TypedResults.Ok(new CardSemanticSearchResponse(status, [], query.Versions));
            }

            var vector = await embedder.EmbedQueryAsync(SearchSurface.CardSearch, request.Query, cancellationToken);
            var rows = await Knn(query.Candidates, db.SourceProfiles, db.TagAliases, query.Popularity, request, vector)
                .AsNoTracking().ToListAsync(cancellationToken);
            return TypedResults.Ok(new CardSemanticSearchResponse(
                status, [.. rows.Select(r => r.Hit(request.Role))], query.Versions));
        });
    }

    /// <summary>The filtered KNN over cards, nearest first, k cards — the
    /// one statement the card Search surface runs. A card's distance is the
    /// minimum over its face documents, so faces collapse before the LIMIT.
    /// An order by an aggregate cannot use a vector index, so the statement
    /// computes the distance of every candidate. A filter never hides a card
    /// that passes it.</summary>
    internal static IQueryable<Row> Knn(
        IQueryable<Card> candidates,
        IQueryable<SourceProfile> profiles,
        IQueryable<TagAlias> aliases,
        IQueryable<PopularityRank> popularity,
        CardSemanticSearchRequest request,
        Vector query)
    {
        var scoped = request.CollectionScope;
        return Profiled(candidates, profiles, aliases, popularity, request)
            .Where(x => x.Card.Documents.Any(d => d.Embedding != null))
            .Select(x => new
            {
                x.Card,
                x.EntersTapped,
                x.PopularityRank,
                Distance = x.Card.Documents.Where(d => d.Embedding != null).Min(d => d.Embedding!.CosineDistance(query)),
            })
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.PopularityRank == null).ThenBy(x => x.PopularityRank)
            .ThenBy(x => x.Card.Name).ThenBy(x => x.Card.OracleId)
            .Take(Math.Min(request.K ?? DefaultLimit, MaxLimit))
            .Select(x => new Row(
                x.Card,
                x.EntersTapped,
                scoped ? ArchiveDbContext.AvailableCopies(x.Card.CopyLimit, x.Card.OracleId) : null,
                x.Card.Documents
                    .Where(d => d.Embedding != null && d.Embedding!.CosineDistance(query) == x.Distance)
                    .Min(d => d.FaceIndex),
                x.Distance,
                x.PopularityRank));
    }

    /// <summary>A filtered candidate beside its profile facets and its
    /// popularity rank: the one place the profile and rank tables join the
    /// query, as LEFT JOINs that stay flat under the KNN LIMIT. The next
    /// profile facet on a hit is one more member here. A class with member
    /// init, not a record: EF expands the card's navigations through member
    /// bindings, not through a constructor.</summary>
    private sealed class Profile
    {
        public required Card Card { get; init; }

        public EntersTappedState? EntersTapped { get; init; }

        public int? PopularityRank { get; init; }
    }

    private static IQueryable<Profile> Profiled(
        IQueryable<Card> candidates,
        IQueryable<SourceProfile> profiles,
        IQueryable<TagAlias> aliases,
        IQueryable<PopularityRank> popularity,
        CardFilterRequest request)
        => Filter(candidates, profiles, aliases, request)
            .GroupJoin(profiles, c => c.Id, p => p.CardId, (c, ps) => new { c, ps })
            .SelectMany(x => x.ps.DefaultIfEmpty(), (x, p) => new { x.c, p })
            .GroupJoin(popularity, x => x.c.Id, r => r.CardId, (x, rs) => new { x.c, x.p, rs })
            .SelectMany(x => x.rs.DefaultIfEmpty(), (x, r) => new Profile
            {
                Card = x.c,
                EntersTapped = x.p == null ? null : x.p.EntersTapped,
                PopularityRank = r == null ? null : r.Rank,
            });

    /// <summary>The structured predicates, composed onto the boundary query.
    /// Every listed type, keyword, and tag must be present; the name filter
    /// is a case-insensitive substring match, never semantic. A tag matches
    /// as a label, or as an alias of the label a card carries, in the same
    /// statement. The enters-tapped filter keeps lands whose stored facet
    /// equals the value. Nonlands and unmatched lands drop out.</summary>
    private static IQueryable<Card> Filter(
        IQueryable<Card> candidates, IQueryable<SourceProfile> profiles, IQueryable<TagAlias> aliases, CardFilterRequest request)
    {
        if (request.OracleIds is { } oracleIds)
        {
            candidates = candidates.Where(c => oracleIds.Contains(c.OracleId));
        }

        if (request.EntersTapped is { } entersTapped)
        {
            candidates = candidates.Where(c => profiles.Any(p => p.CardId == c.Id && p.EntersTapped == entersTapped));
        }

        foreach (var type in request.Types ?? [])
        {
            candidates = candidates.Where(c =>
                c.CardTypes.Contains(type) || c.Supertypes.Contains(type) || c.Subtypes.Contains(type));
        }

        foreach (var keyword in request.Keywords ?? [])
        {
            candidates = candidates.Where(c => c.Keywords.Contains(keyword));
        }

        foreach (var tag in request.Tags ?? [])
        {
            candidates = candidates.Where(c => c.OracleTags.Any(t =>
                t.Tag == tag || aliases.Any(a => a.Alias == tag && a.Label == t.Tag)));
        }

        if (request.MinManaValue is { } min)
        {
            candidates = candidates.Where(c => c.ManaValue >= min);
        }

        if (request.MaxManaValue is { } max)
        {
            candidates = candidates.Where(c => c.ManaValue <= max);
        }

        if (!string.IsNullOrEmpty(request.Name))
        {
            var pattern = "%" + request.Name.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
            candidates = candidates.Where(c => EF.Functions.ILike(c.Name, pattern, @"\"));
        }

        return candidates;
    }

    /// <summary>One statement row: the card, its profile facets, and what
    /// the statement computed beside it. The enters-tapped facet is null for
    /// a nonland and for an unmatched land.</summary>
    internal sealed record Row(
        Card Card, EntersTappedState? EntersTapped, int? Available, int? FaceIndex, double? Distance, int? PopularityRank)
    {
        public CardSearchHit Hit(Role role) => new()
        {
            Card = Citation.OfCard(Card),
            EntersTapped = EntersTapped,
            ManaCost = Card.ManaCost,
            ManaValue = Card.ManaValue,
            TypeLine = Card.TypeLine,
            OracleText = Card.OracleText,
            ColorIdentity = Card.ColorIdentity,
            Keywords = Card.Keywords,
            Power = Card.Power,
            Toughness = Card.Toughness,
            Loyalty = Card.Loyalty,
            EdhrecRank = Card.EdhrecRank,
            PopularityRank = PopularityRank,
            Legality = [new RoleLegality(role, LegalityStatus.Legal, null)],
            FaceIndex = FaceIndex,
            Distance = Distance,
            Available = Available,
        };
    }
}

/// <summary>The structured filters both card query endpoints share. Array
/// filters are conjunctive: every listed value must be present.</summary>
public abstract record CardFilterRequest : CandidateQueryRequest
{
    [Description("Keep only these cards, by oracle_id: at most 200, more is a 400. [] matches no cards. An unknown, "
        + "stale, or role-illegal id matches no cards and does not cause an error.")]
    public Guid[]? OracleIds { get; init; }

    [Description("Case-insensitive substring of the card name — a structured filter, never semantic. "
        + "Exact-name lookup is GET /cards?name=.")]
    public string? Name { get; init; }

    [Description("Words the type line must carry: card types, supertypes, or subtypes (e.g. "
        + "[\"Legendary\", \"Creature\"]).")]
    public string[]? Types { get; init; }

    [Description("Keywords the card must carry, as Scryfall lists them.")]
    public string[]? Keywords { get; init; }

    [Description("Oracle Tags the card must carry (\"ramp\", \"removal\"). A Scryfall tag alias also works: it "
        + "resolves to its label before matching (\"blink\" matches cards tagged \"flicker\").")]
    public string[]? Tags { get; init; }

    public decimal? MinManaValue { get; init; }

    public decimal? MaxManaValue { get; init; }

    [Description("Lands only: keep lands whose derived enters-tapped state is this value. Nonlands and unmatched "
        + "lands never pass. A value outside the vocabulary is a 400.")]
    public EntersTappedState? EntersTapped { get; init; }
}

/// <summary>POST /cards/search body: structured filters, paged in rank order.</summary>
public sealed record CardSearchRequest : CardFilterRequest
{
    [Description("Page size: default 50, capped at 200; below 1 is a 400.")]
    public int? Limit { get; init; }

    public int Offset { get; init; }
}

/// <summary>POST /cards/semantic-search body: the card Search surface. The
/// query is embedded service-side with the card-search instruction and the
/// current model's query prefix.</summary>
public sealed record CardSemanticSearchRequest : CardFilterRequest
{
    [Description("What the card should do, in natural language. The service trims it and unifies CRLF to LF before embedding. "
        + "Other interior whitespace stays.")]
    public required string Query { get; init; }

    [Description("How many nearest cards to take: default 50, capped at 200; below 1 is a 400. A card's distance is "
        + "its nearest face's, so a card with two faces takes one slot.")]
    public int? K { get; init; }
}

public sealed record CardSearchResponse(IReadOnlyList<CardSearchHit> Candidates, VersionsBlock Versions);

/// <summary>When <c>semantic.enabled</c> is false the vectors at rest do not
/// match the configured model and prefix set, <c>candidates</c> is empty,
/// and <c>semantic.disabled_reason</c> says why — report content, not an
/// error.</summary>
public sealed record CardSemanticSearchResponse(
    SemanticQueryStatus Semantic, IReadOnlyList<CardSearchHit> Candidates, VersionsBlock Versions);

/// <summary>
/// One lean hit: the card citation (resolvable via <c>GET /cards/{oracle_id}</c>),
/// its summary, its legality in the queried role, and its rank — never
/// rulings. Semantic hits add the nearest face and its cosine distance;
/// scoped hits add <c>available</c> = min(copy limit, owned quantity).
/// </summary>
public sealed record CardSearchHit
{
    public required Citation Card { get; init; }

    public string? ManaCost { get; init; }

    public required decimal ManaValue { get; init; }

    public required string TypeLine { get; init; }

    public string? OracleText { get; init; }

    public required string[] ColorIdentity { get; init; }

    public required string[] Keywords { get; init; }

    public string? Power { get; init; }

    public string? Toughness { get; init; }

    public string? Loyalty { get; init; }

    public int? EdhrecRank { get; init; }

    [Description("The card's 1-based place in the format's popularity order: the commander order under role "
        + "commander, the card order otherwise. versions.popularity_source names the source. POST /cards/search "
        + "pages in this order; semantic hits use it only to break distance ties.")]
    public int? PopularityRank { get; init; }

    [Description("Lands only: the derived enters-tapped state. A nonland and an unmatched land carry none.")]
    public EntersTappedState? EntersTapped { get; init; }

    [Description("Legality in the queried role at the resolved banlist version — always legal: "
        + "the boundary admits nothing else.")]
    public required IReadOnlyList<RoleLegality> Legality { get; init; }

    [Description("Semantic hits only: the face whose document was nearest.")]
    public int? FaceIndex { get; init; }

    [Description("Semantic hits only: cosine distance from the query.")]
    public double? Distance { get; init; }

    [Description("Under collection_scope only: min(copy limit, owned quantity).")]
    public int? Available { get; init; }
}
