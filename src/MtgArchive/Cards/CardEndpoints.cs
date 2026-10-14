using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Banlist;
using MtgArchive.Data;

namespace MtgArchive.Cards;

/// <summary>
/// The card reference endpoints. GET /cards/{oracle_id} serves full card
/// detail — the resolution target for card citations; GET /cards?name= is
/// the exact-name variant for citation-following and resolution checks (not
/// search — no fuzziness, no filters). Stale cards keep resolving here, so
/// collection rows and ban history never dangle.
/// </summary>
public sealed class CardEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/cards/{oracle_id:guid}", async Task<Results<Ok<CardDetailResponse>, ProblemHttpResult>> (
            [FromRoute(Name = "oracle_id")] Guid oracleId,
            [FromQuery(Name = "banlist_version")] string? banlistVersion,
            ArchiveDbContext db,
            BanlistVersionAxis banlist,
            CancellationToken cancellationToken) =>
        {
            // The axis resolves after the card lookup so a miss never pays
            // the extra version query.
            var card = await Details(db).SingleOrDefaultAsync(c => c.OracleId == oracleId, cancellationToken);
            return card is null
                ? TypedResults.Problem(Problems.UnknownCard(oracleId))
                : TypedResults.Ok(await DetailAsync(db, banlist, card, banlistVersion, cancellationToken));
        });

        app.MapGet("/cards", async Task<Results<Ok<CardDetailResponse>, ProblemHttpResult>> (
            string? name,
            [FromQuery(Name = "banlist_version")] string? banlistVersion,
            ArchiveDbContext db,
            BanlistVersionAxis banlist,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrEmpty(name))
            {
                return TypedResults.Problem(Problems.MissingParameter("name"));
            }

            // The corpus is NFC; normalize the incoming name so a
            // canonically-equivalent NFD spelling still matches.
            var normalizedName = name.Normalize(NormalizationForm.FormC);

            // The endpoint returns every namesake that survives the namesake
            // rule and never picks one. Nothing enforces name uniqueness.
            var matches = await Details(db).Where(c => c.Name == normalizedName).ToListAsync(cancellationToken);
            var candidates = NamesakePrecedence.Candidates(matches, c => c.Stale, c => c.Layout);

            // The axis resolves after the card lookup so a miss never pays
            // the extra version query.
            return candidates switch
            {
                [] => TypedResults.Problem(Problems.UnknownCardName(name)),
                [var card] => TypedResults.Ok(await DetailAsync(db, banlist, card, banlistVersion, cancellationToken)),
                _ => TypedResults.Problem(
                    Problems.AmbiguousCardName(name, candidates.Select(c => c.OracleId))),
            };
        });
    }

    private static async Task<CardDetailResponse> DetailAsync(
        ArchiveDbContext db, BanlistVersionAxis banlist, Card card, string? banlistVersion, CancellationToken cancellationToken)
    {
        var pin = await banlist.ResolveAsync(db, banlistVersion, cancellationToken);
        return ToDetail(
            card,
            pin?.Wire,
            await LegalityAsync(db, card, pin?.Id, cancellationToken));
    }

    /// <summary>
    /// The card's materialized legality rows in all four formats (ADR 0012):
    /// Commander at the resolved version, each banned status carrying its
    /// ban-event citation; each Brawl format's one current state, every row
    /// carrying its <c>legality</c> citation. One statement: the citation
    /// subquery rides along, so a replay committing between round trips can
    /// never pair a row with a retired event. With no resolved version only
    /// the Brawl rows come back. Null (absent on the wire) while the rebuild
    /// has not run.
    /// </summary>
    private static async Task<IReadOnlyList<RoleLegality>?> LegalityAsync(
        ArchiveDbContext db, Card card, int? banlistVersionId, CancellationToken cancellationToken)
    {
        var versioned = FormatParameter.Materialized.Where(FormatParameter.IsVersioned).ToList();
        var rows = await db.Legalities
            .Where(l => l.CardId == card.Id
                && (versioned.Contains(l.Format) ? l.BanlistVersionId == banlistVersionId : l.BanlistVersionId == null))
            .Select(l => new
            {
                l.Format,
                l.Role,
                l.Status,
                Reason = BanReasons.Query(db).FirstOrDefault(r => r.Id == l.Reason),
                l.ScryfallKey,
                l.ScryfallUri,
                l.LegalityAsOf,
            })
            .ToListAsync(cancellationToken);
        return rows.Count == 0
            ? null
            : [.. rows
                .OrderBy(r => r.Format)
                .ThenBy(r => r.Role)
                .Select(r => new RoleLegality(r.Role, r.Status, r.Reason is null
                    ? null
                    : BanlistEndpoints.EventCitation(r.Reason, card.Name))
                {
                    Format = r.Format,
                    ScryfallKey = r.ScryfallKey,
                    ScryfallUri = r.ScryfallUri,
                    LegalityAsOf = r.LegalityAsOf,
                    Citation = LegalityCitation.OfRow(
                        card.OracleId,
                        r.ScryfallKey,
                        r.LegalityAsOf,
                        rows.First(m => m.Format == r.Format && m.Role == Role.Main).Status,
                        r.Role,
                        r.Status),
                })];
    }

    private static IQueryable<Card> Details(ArchiveDbContext db) => db.Cards
        .Include(c => c.Faces)
        .Include(c => c.Rulings)
        .Include(c => c.OracleTags)
        .AsSplitQuery()
        .AsNoTracking();

    private static CardDetailResponse ToDetail(
        Card card, string? banlistVersion, IReadOnlyList<RoleLegality>? legality) => new()
        {
            OracleId = card.OracleId,
            Name = card.Name,
            ManaCost = card.ManaCost,
            ManaValue = card.ManaValue,
            TypeLine = card.TypeLine,
            Supertypes = card.Supertypes,
            CardTypes = card.CardTypes,
            Subtypes = card.Subtypes,
            OracleText = card.OracleText,
            Colors = card.Colors,
            ColorIdentity = card.ColorIdentity,
            Keywords = card.Keywords,
            Power = card.Power,
            Toughness = card.Toughness,
            Loyalty = card.Loyalty,
            ProducedMana = card.ProducedMana,
            GameChanger = card.GameChanger,
            DigitalOnly = card.DigitalOnly,
            EdhrecRank = card.EdhrecRank,
            ReleasedAt = card.ReleasedAt,
            CopyLimit = card.CopyLimit,
            Stale = card.Stale,
            Faces = [.. card.Faces
            .OrderBy(f => f.FaceIndex)
            .Select(f => new CardFaceDetail
            {
                FaceIndex = f.FaceIndex,
                Name = f.Name,
                ManaCost = f.ManaCost,
                ManaValue = f.ManaValue,
                TypeLine = f.TypeLine,
                OracleText = f.OracleText,
                Colors = f.Colors,
                Power = f.Power,
                Toughness = f.Toughness,
                Loyalty = f.Loyalty,
            })],
            Legality = legality,
            Rulings = [.. card.Rulings
            .OrderBy(r => r.PublishedAt).ThenBy(r => r.Id)
            .Select(r => new CardRulingDetail(r.Source, r.PublishedAt, r.Comment))],
            Tags = [.. card.OracleTags.Select(t => t.Tag).Order(StringComparer.Ordinal)],
            Taggings = [.. card.OracleTags
            .OrderBy(t => t.Tag, StringComparer.Ordinal)
            .Select(t => new CardTagging(t.Tag, t.Inherited, t.Weight))],
            Versions = new VersionsBlock { BanlistVersion = banlistVersion },
        };
}
