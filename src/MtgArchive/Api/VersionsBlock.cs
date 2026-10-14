namespace MtgArchive.Api;

/// <summary>
/// Top-level <c>versions</c> block: every response that consumed a versioned
/// input echoes the versions actually used. Unused axes stay null and never
/// appear on the wire.
/// </summary>
public sealed record VersionsBlock
{
    /// <summary>The format a candidate query judged legality in
    /// (<see cref="FormatParameter"/>).</summary>
    public string? Format { get; init; }

    public string? BanlistVersion { get; init; }

    /// <summary>The Scryfall bulk <c>updated_at</c> that dates a Brawl
    /// format's legality; null in Commander. Nobody can pin it.</summary>
    public DateTimeOffset? LegalityAsOf { get; init; }

    /// <summary>The signal the format's popularity order reads first:
    /// <c>edhrec</c> (the proxy) or <c>untapped</c> (a popularity export).
    /// Null before the first ingest derives an order.</summary>
    public string? PopularitySource { get; init; }

    /// <summary>The date the popularity order is current to: the Scryfall
    /// bulk <c>updated_at</c> for <c>edhrec</c>, the export window end for
    /// <c>untapped</c>.</summary>
    public DateTimeOffset? PopularityAsOf { get; init; }

    public string? CrEffectiveDate { get; init; }
    public string? BracketSpecVersion { get; init; }
    public string? GcSnapshot { get; init; }
}
