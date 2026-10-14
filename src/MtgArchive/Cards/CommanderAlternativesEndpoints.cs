using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Banlist;
using MtgArchive.Data;
using MtgArchive.Simulation;

namespace MtgArchive.Cards;

/// <summary>
/// <c>POST /commander-alternatives</c>: the one deterministic recommendation
/// query (ADR 0006, done-query 1). The decklist resolves like tier 1. The
/// pool is every commander-role-legal card from the legality boundary
/// (<see cref="CandidateQuery"/>, role fixed to commander, original
/// commanders excluded, collection scope as a hard filter). There is no
/// color-identity pre-filter: preserving color identity is the retention
/// metric. The pool comes back as lean rows in one statement and
/// <see cref="CommanderAlternatives"/> ranks them in memory. The retained
/// count depends only on a candidate's 5-bit mask, so SQL could order by
/// it only through a 32-way lookup.
///
/// The body is not a <see cref="CandidateQueryRequest"/>. That base carries
/// a caller-chosen role and color identity, and the property harness fuzzes
/// both. Here the role is fixed and the identity is the thing measured. The
/// endpoint's own tests cover the boundary properties instead.
/// </summary>
public sealed class CommanderAlternativesEndpoints : IEndpointMapper
{
    public const int DefaultLimit = 20;

    public const int MaxLimit = 200;

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/commander-alternatives", async Task<Results<Ok<CommanderAlternativesResponse>, ProblemHttpResult>> (
            CommanderAlternativesRequest request,
            ArchiveDbContext db,
            BanlistVersionAxis banlist,
            CancellationToken cancellationToken) =>
        {
            if (request.Limit < 1)
            {
                return TypedResults.Problem(Problems.ParameterBelowOne("limit", request.Limit.Value));
            }

            // The pin resolves before any decklist work so an unknown
            // version is a 400 on its own.
            var query = await CandidateQuery.ResolveAsync(db, banlist, new CommanderPool
            {
                CollectionScope = request.CollectionScope,
                Format = request.Format,
                BanlistVersion = request.BanlistVersion,
            }, cancellationToken);

            ResolvedDecklist decklist;
            try
            {
                decklist = await new DecklistResolver(db).ResolveAsync(request.Decklist, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                return TypedResults.Problem(Problems.InvalidDecklist(ex.Message));
            }

            if (decklist.Unresolved.Count > 0)
            {
                return TypedResults.Problem(Problems.UnresolvedDecklist(decklist.Unresolved));
            }

            if (decklist.Commanders.Count == 0)
            {
                return TypedResults.Problem(Problems.InvalidDecklist(
                    "The decklist needs at least one commander: closeness is measured against it."));
            }

            var deck = await LoadDeckAsync(db, decklist, query, cancellationToken);
            var originalMask = decklist.Commanders.Aggregate(0, (mask, id) => mask | deck[id].ColorIdentityMask);
            var members = decklist.Entries.Select(e => e.OracleId).Distinct()
                .Select(id => new DeckMember(id, deck[id].Name, decklist.QuantityOf(id), deck[id].ColorIdentityMask, deck[id].MainLoss))
                .ToList();
            var companion = decklist.Companion is { } companionId
                ? new DeckMember(companionId, deck[companionId].Name, 1, deck[companionId].ColorIdentityMask, deck[companionId].CompanionLoss)
                : null;

            var commanders = decklist.Commanders.ToArray();
            var scoped = request.CollectionScope;
            var pool = await query.Candidates
                .Where(c => !commanders.Contains(c.OracleId))
                .GroupJoin(query.Popularity, c => c.Id, r => r.CardId, (c, rs) => new { c, rs })
                .SelectMany(x => x.rs.DefaultIfEmpty(), (x, r) => new CommanderCandidate(
                    x.c.OracleId,
                    x.c.Name,
                    x.c.ColorIdentity,
                    x.c.ColorIdentityMask,
                    r == null ? null : r.Rank,
                    x.c.EdhrecRank,
                    scoped ? ArchiveDbContext.AvailableCopies(x.c.CopyLimit, x.c.OracleId) : null))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var ranked = CommanderAlternatives.Rank(
                originalMask, members, pool, Math.Min(request.Limit ?? DefaultLimit, MaxLimit), companion);
            return TypedResults.Ok(new CommanderAlternativesResponse(
                new OriginalCommander(
                    [.. commanders.Select(id => Citation.OfCard(id, deck[id].Name))],
                    CandidateQuery.ColorLetters(originalMask),
                    decklist.DeckSize),
                [.. ranked.Select(ToWire)],
                query.Versions)
            {
                NameRewrites = decklist.WireRewrites,
            });
        });
    }

    private sealed record DeckRow(string Name, int ColorIdentityMask, FormatLoss? MainLoss, FormatLoss? CompanionLoss);

    /// <summary>
    /// Every decklist card's name and mask, plus the loss the format gives
    /// it: the main-role row for the deck, the companion-role row for the
    /// companion. A Commander row reads the pin: <c>banned</c> cites the
    /// standing ban event (or the card when no event backs it), and
    /// <c>not_legal</c> cites the card, because Commander has no legality
    /// as-of date. No pin (nothing replayed) means no rows and no losses. A
    /// Brawl row reads the format's one current state, and both losses cite
    /// its <c>legality</c> citation. <c>not_eligible</c> (a companion
    /// without the keyword) is no loss: eligibility is the deck verdict's
    /// call, and this query stays verdict-free.
    /// </summary>
    private static async Task<Dictionary<Guid, DeckRow>> LoadDeckAsync(
        ArchiveDbContext db, ResolvedDecklist decklist, CandidateQuery query, CancellationToken cancellationToken)
    {
        var format = query.Format;
        var versioned = FormatParameter.IsVersioned(format);
        var versionId = versioned ? query.Banlist?.Id : null;
        var ids = decklist.Commanders
            .Concat(decklist.Companion is { } companion ? [companion] : [])
            .Concat(decklist.Entries.Select(e => e.OracleId))
            .Distinct()
            .ToArray();
        var cards = await db.Cards
            .Where(c => ids.Contains(c.OracleId))
            .Select(c => new { c.Id, c.OracleId, c.Name, c.ColorIdentityMask })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var cardIds = cards.Select(c => c.Id).ToArray();
        var rows = versioned && versionId is null
            ? []
            : await db.Legalities
                .Where(l => cardIds.Contains(l.CardId) && l.Format == format && l.BanlistVersionId == versionId
                    && (l.Role == Role.Main || l.Role == Role.Companion)
                    && (l.Status == LegalityStatus.Banned || l.Status == LegalityStatus.NotLegal))
                .Select(l => new LossRow(
                    l.CardId,
                    l.Role,
                    l.Status,
                    l.ScryfallKey,
                    l.LegalityAsOf,
                    BanReasons.Query(db).FirstOrDefault(r => r.Id == l.Reason)))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

        return cards.ToDictionary(c => c.OracleId, c =>
        {
            FormatLoss? LossIn(Role role) => rows.FirstOrDefault(r => r.CardId == c.Id && r.Role == role) is { } r
                ? new FormatLoss(
                    r.Status == LegalityStatus.Banned ? LossReason.Banned : LossReason.NotLegal,
                    // The query reads only banned and not_legal rows. In a
                    // Brawl format the key gives both statuses to every role,
                    // so the row status is also the key status.
                    LegalityCitation.Pick(c.OracleId, c.Name, r.ScryfallKey, r.LegalityAsOf, r.Status, r.Role, r.Status, r.Event))
                : null;

            return new DeckRow(c.Name, c.ColorIdentityMask, LossIn(Role.Main), LossIn(Role.Companion));
        });
    }

    private sealed record LossRow(
        int CardId, Role Role, LegalityStatus Status, string? ScryfallKey, DateTimeOffset? LegalityAsOf, BanReason? Event);

    private static CommanderAlternative ToWire(RankedAlternative ranked) => new()
    {
        Card = Citation.OfCard(ranked.Candidate.OracleId, ranked.Candidate.Name),
        ColorIdentity = ranked.Candidate.ColorIdentity,
        PopularityRank = ranked.Candidate.PopularityRank,
        Available = ranked.Candidate.Available,
        RetainedCount = ranked.RetainedCount,
        LostCount = ranked.LostCount,
        ColorIdentityDistance = ranked.ColorIdentityDistance,
        Retained = [.. ranked.Retained.Select(m => new RetainedCard(Citation.OfCard(m.OracleId, m.Name), m.Quantity))],
        Lost = [.. ranked.Lost.Select(l => new LostCard(
            Citation.OfCard(l.Member.OracleId, l.Member.Name), l.Member.Quantity, l.Reason, l.Citation))],
        Companion = ranked.Companion is { } companion
            ? new CompanionCard(
                Citation.OfCard(companion.Member.OracleId, companion.Member.Name),
                companion.Loss is null,
                companion.Loss?.Reason,
                companion.Loss?.Citation)
            : null,
    };

    /// <summary>The boundary inputs for the pool: commander role, no color
    /// identity, the caller's scope and pin.</summary>
    private sealed record CommanderPool : CandidateQueryRequest
    {
        public CommanderPool()
        {
            Role = Role.Commander;
        }
    }
}

/// <summary>POST /commander-alternatives body.</summary>
public sealed record CommanderAlternativesRequest
{
    [Description("The deck as it stands: its commander(s) and the cards to retain. Closeness is "
        + "measured against the commanders' combined color identity.")]
    public required Decklist Decklist { get; init; }

    [Description("Hard-filter candidates to owned cards (the collection). Set by the caller, "
        + "never inferred. Under scope each candidate carries available = min(copy limit, "
        + "owned quantity).")]
    public bool CollectionScope { get; init; } = false;

    [Description("The format the deck moves to: commander, brawl, competitive_brawl, or "
        + "standard_brawl. Commander when omitted, and the versions block echoes it. Candidates "
        + "are the format's commander-legal cards, and each card is judged in the format. An "
        + "unknown format is a 400.")]
    public string? Format { get; init; }

    [Description("Banlist version pin (effective_date, yyyy-MM-dd); latest when omitted. "
        + "Commander only: a Brawl format with a pin is a 400. An unknown pin is a 400, "
        + "never a silent fallback.")]
    public string? BanlistVersion { get; init; }

    [Description("How many ranked candidates to return: default 20, capped at 200; below 1 is a 400.")]
    public int? Limit { get; init; }
}

public sealed record CommanderAlternativesResponse(
    OriginalCommander Original, IReadOnlyList<CommanderAlternative> Candidates, VersionsBlock Versions)
{
    [System.ComponentModel.Description("The A- names that resolved only without the prefix, each with the card it resolved to; absent when there is none.")]
    public IReadOnlyList<NameRewrite>? NameRewrites { get; init; }
}

/// <summary>The decklist's commander(s) as resolved, their combined color
/// identity, and the size of the list being retained.</summary>
public sealed record OriginalCommander(IReadOnlyList<Citation> Commanders, string[] ColorIdentity, int DeckSize);

/// <summary>
/// One ranked candidate: the card citation, what the rank read, and the
/// partition of the decklist into retained and lost cards. Counts sum entry
/// quantities (ten Forests count ten), so the two counts add up to the deck
/// size.
/// </summary>
public sealed record CommanderAlternative
{
    public required Citation Card { get; init; }

    public required string[] ColorIdentity { get; init; }

    [Description("The candidate's 1-based place in the format's commander popularity order — the last "
        + "tie-break. versions.popularity_source names the source.")]
    public int? PopularityRank { get; init; }

    [Description("Under collection_scope only: min(copy limit, owned quantity).")]
    public int? Available { get; init; }

    public required int RetainedCount { get; init; }

    public required int LostCount { get; init; }

    [Description("Symmetric difference between this candidate's color identity and the original "
        + "commander's — the first tie-break.")]
    public required int ColorIdentityDistance { get; init; }

    public required IReadOnlyList<RetainedCard> Retained { get; init; }

    public required IReadOnlyList<LostCard> Lost { get; init; }

    [Description("The companion's outcome when the decklist has one: retained, or lost with "
        + "a reason and a citation by the same rules as the deck. It is in neither count.")]
    public CompanionCard? Companion { get; init; }
}

public sealed record RetainedCard(Citation Card, int Quantity);

/// <summary>A lost card and why. A color-identity loss cites the card. In
/// Commander a ban loss cites the ban event and a <c>not_legal</c> loss
/// cites the card; in a Brawl format both cite the <c>legality</c>
/// citation.</summary>
public sealed record LostCard(Citation Card, int Quantity, LossReason Reason, Citation Citation);

/// <summary>The companion against one candidate: retained, or lost with
/// the reason and the citation a deck card would get.</summary>
public sealed record CompanionCard(Citation Card, bool Retained, LossReason? Reason, Citation? Citation);
