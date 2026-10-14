using Microsoft.EntityFrameworkCore;
using MtgArchive.Banlist;
using MtgArchive.Data;

namespace MtgArchive.Api;

/// <summary>
/// Where every index query starts: the shared candidate-query inputs
/// resolved into <see cref="ArchiveDbContext.LegalCandidates"/> and the
/// versions echo. The banlist pin resolves latest-by-default (an unknown
/// pin throws the 400 problem) and the deck color identity becomes the
/// WUBRG mask, and the caller's collection scope passes straight through;
/// the endpoint composes its own predicates onto <see cref="Candidates"/>.
/// <see cref="Format"/> (Commander when the request names none) and
/// <see cref="Banlist"/>, the resolved pin (null while nothing is
/// replayed, and always null in a Brawl format), serve an endpoint that
/// also reads legality rows directly. A Brawl format takes no banlist
/// pin (a 400) and echoes its legality as-of date instead.
/// <see cref="Popularity"/> holds the format's popularity ranks in the
/// order the request reads: the commander order for role commander, the
/// card order otherwise. An endpoint left-joins it on the card id and
/// sorts by it; the versions block names its source and as-of date.
/// </summary>
public sealed record CandidateQuery(
    IQueryable<Card> Candidates,
    VersionsBlock Versions,
    Format Format,
    BanlistVersionPin? Banlist,
    IQueryable<PopularityRank> Popularity)
{
    public static async Task<CandidateQuery> ResolveAsync(
        ArchiveDbContext db, BanlistVersionAxis banlist, CandidateQueryRequest request, CancellationToken cancellationToken)
    {
        var format = FormatParameter.Resolve(request.Format);
        var mask = request.ColorIdentity is null ? (int?)null : ColorIdentityMask(request.ColorIdentity);
        var order = request.Role == Role.Commander ? PopularityOrder.Commander : PopularityOrder.Card;
        var popularity = db.PopularityRanks.Where(r => r.Format == format && r.Order == order);
        DateOnly? effectiveDate = null;
        BanlistVersionPin? pin = null;
        DateTimeOffset? legalityAsOf = null;
        if (!FormatParameter.IsVersioned(format))
        {
            // A Brawl format has one current state: no banlist version to
            // pin, and the legality as-of date dates it instead.
            FormatParameter.RejectBanlistPin(format, request.BanlistVersion);
            legalityAsOf = await LegalityAsOfAsync(db, format, cancellationToken);
        }
        else
        {
            pin = await banlist.ResolveAsync(db, request.BanlistVersion, cancellationToken);
            effectiveDate = pin?.EffectiveDate;
        }

        var source = await db.PopularitySources.AsNoTracking()
            .Where(s => s.Format == format)
            .FirstOrDefaultAsync(cancellationToken);
        return new CandidateQuery(
            db.LegalCandidates(format, effectiveDate, request.Role, mask, request.CollectionScope),
            new VersionsBlock
            {
                Format = FormatParameter.WireName(format),
                BanlistVersion = pin?.Wire,
                LegalityAsOf = legalityAsOf,
                PopularitySource = source is null ? null : ArchiveDbContext.WireName(source.Source),
                PopularityAsOf = source?.AsOf,
            },
            format,
            pin,
            popularity);
    }

    /// <summary>The as-of date of a Brawl format's legality rows; null
    /// while the rebuild has written none.</summary>
    public static Task<DateTimeOffset?> LegalityAsOfAsync(ArchiveDbContext db, Format format, CancellationToken cancellationToken)
        => db.Legalities
            .Where(l => l.Format == format && l.BanlistVersionId == null)
            .Select(l => l.LegalityAsOf)
            .FirstOrDefaultAsync(cancellationToken);

    // The same encoding the database generates into card.color_identity_mask
    // (Card.ColorIdentityMaskContract), in WUBRG order: W=16, U=8, B=4, R=2, G=1.
    private static readonly (string Letter, int Bit)[] ColorBits = [("W", 16), ("U", 8), ("B", 4), ("R", 2), ("G", 1)];

    /// <summary>The readable form of a mask, in WUBRG order.</summary>
    public static string[] ColorLetters(int mask)
        => [.. ColorBits.Where(c => (mask & c.Bit) != 0).Select(c => c.Letter)];

    private static int ColorIdentityMask(string[] colorIdentity)
    {
        var mask = 0;
        List<string>? unknown = null;
        foreach (var color in colorIdentity)
        {
            if (Array.FindIndex(ColorBits, c => c.Letter == color) is var index && index >= 0)
            {
                mask |= ColorBits[index].Bit;
            }
            else
            {
                (unknown ??= []).Add(color);
            }
        }

        return unknown is null
            ? mask
            : throw new ProblemException(Problems.InvalidColorIdentity(unknown.Distinct().ToList()));
    }
}
