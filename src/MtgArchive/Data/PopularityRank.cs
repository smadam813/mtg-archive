namespace MtgArchive.Data;

/// <summary>
/// One card's place in a format's popularity order (GLOSSARY.md
/// "Popularity order", ADR 0012): a total, 1-based rank over the format's
/// pool, per order. Derived at ingest by the popularity load phase and
/// rebuilt every run into the staging twin. The candidate routes sort by it
/// and echo it as <c>popularity_rank</c>, so no endpoint orders by
/// <c>edhrec_rank</c> itself.
///
/// Each swapped table is one CLR shape with a live and a staging leaf (the
/// derived-table pattern; see <see cref="LegalityRow"/>).
/// </summary>
public abstract class PopularityRankRow
{
    public required int CardId { get; set; }

    public required Format Format { get; set; }

    public required PopularityOrder Order { get; set; }

    /// <summary>1-based; unique within (format, order).</summary>
    public required int Rank { get; set; }
}

/// <summary>See <see cref="PopularityRankRow"/>.</summary>
public sealed class PopularityRank : PopularityRankRow;

/// <summary>Staging twin of <see cref="PopularityRank"/> — table
/// <c>popularity_rank_staging</c>.</summary>
public sealed class PopularityRankStaging : PopularityRankRow;

/// <summary>Which popularity order a request reads. A request with role
/// commander reads <see cref="Commander"/>; every other request reads
/// <see cref="Card"/>. With no popularity export the two are equal.</summary>
public enum PopularityOrder
{
    Card,
    Commander,
}

/// <summary>
/// Where a format's popularity order comes from and how current it is: the
/// <c>popularity_source</c> and <c>popularity_as_of</c> every candidate
/// query echoes. One row per format, rebuilt with the ranks.
/// </summary>
public abstract class PopularitySourceRow
{
    public const string AsOfContract =
        "The date that the popularity order is current to: the Scryfall bulk "
        + "updated_at for the edhrec source, the export window end for untapped.";

    public required Format Format { get; set; }

    public required PopularitySourceKind Source { get; set; }

    /// <summary>See <see cref="AsOfContract"/>.</summary>
    public required DateTimeOffset AsOf { get; set; }
}

/// <summary>See <see cref="PopularitySourceRow"/>.</summary>
public sealed class PopularitySource : PopularitySourceRow;

/// <summary>Staging twin of <see cref="PopularitySource"/> — table
/// <c>popularity_source_staging</c>.</summary>
public sealed class PopularitySourceStaging : PopularitySourceRow;

/// <summary>The signal a popularity order reads first. <see cref="Edhrec"/>
/// is the proxy: Commander popularity, used for a Brawl format that has no
/// export.</summary>
public enum PopularitySourceKind
{
    Edhrec,
    Untapped,
}
