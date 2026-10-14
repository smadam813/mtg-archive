using MtgArchive.Banlist;
using MtgArchive.Data;

namespace MtgArchive.Api;

/// <summary>
/// Binding for the <c>format</c> body field of a candidate query. A missing
/// format means Commander (ADR 0012), and the response echoes the format in
/// its versions block, so the default stays visible. A value outside
/// <see cref="Materialized"/> is a 400 problem that names it — never a
/// silent fallback. The field is a string on the wire, not a bound enum, so
/// the problem can name the value: a bound enum fails body binding with a
/// bodiless 400.
/// </summary>
public static class FormatParameter
{
    /// <summary>The formats whose legality the rebuild materializes. Only
    /// these resolve; every other wire value is unknown to a request.</summary>
    public static readonly IReadOnlyList<Format> Materialized =
        [Format.Commander, Format.Brawl, Format.CompetitiveBrawl, Format.StandardBrawl];

    /// <summary>Whether the format has the banlist version axis. Only
    /// Commander does; a Brawl format has one current state (ADR 0012).</summary>
    public static bool IsVersioned(Format format) => format == Format.Commander;

    public static Format Resolve(string? value)
    {
        if (value is null)
        {
            return Format.Commander;
        }

        foreach (var format in Materialized)
        {
            if (WireName(format) == value)
            {
                return format;
            }
        }

        throw new ProblemException(Problems.UnknownFormat(value, [.. Materialized.Select(WireName)]));
    }

    public static string WireName(Format format) => ArchiveDbContext.WireName(format);

    /// <summary>Throws the 400 problem when a Brawl format comes with a
    /// banlist version pin: only Commander has the version axis.</summary>
    public static void RejectBanlistPin(Format format, string? banlistVersion)
    {
        if (!IsVersioned(format) && banlistVersion is not null)
        {
            throw new ProblemException(Problems.UnversionedFormat("banlist_version", WireName(format)));
        }
    }

    /// <summary>The banlist pin and the versions block of a deck route: a
    /// Commander request resolves the pin and echoes it; a Brawl request has
    /// no pin and echoes the legality as-of date instead.</summary>
    public static async Task<(BanlistVersionPin? Pin, VersionsBlock Versions)> ResolveDeckVersionsAsync(
        ArchiveDbContext db, BanlistVersionAxis banlist, Format format, string? banlistVersion, CancellationToken cancellationToken)
    {
        if (!IsVersioned(format))
        {
            return (null, new VersionsBlock
            {
                Format = WireName(format),
                LegalityAsOf = await CandidateQuery.LegalityAsOfAsync(db, format, cancellationToken),
            });
        }

        var pin = await banlist.ResolveAsync(db, banlistVersion, cancellationToken);
        return (pin, new VersionsBlock { Format = WireName(format), BanlistVersion = pin?.Wire });
    }
}
