namespace MtgArchive.Data;

/// <summary>
/// A dated snapshot of the Commander banlist, produced by replaying the ban
/// events in <c>data/banlist-events.json</c> — one row per dated
/// announcement, addressed on the wire by <see cref="EffectiveDate"/>. Rows
/// are derived; the banlist replay phase fully rebuilds them from the file.
/// A pinned historical version means today's card pool under that date's ban
/// state — replay never time-travels the card pool.
/// </summary>
public sealed class BanlistVersion
{
    public int Id { get; set; }

    /// <summary>The announcement's effective date — the version's wire address.</summary>
    public required DateOnly EffectiveDate { get; set; }

    public required DatePrecision DatePrecision { get; set; }

    /// <summary>The announcement this version cites (typically a Wayback URL).</summary>
    public required string AnnouncementUrl { get; set; }

    public List<BanEvent> Events { get; set; } = [];
}

/// <summary>
/// How precise <see cref="BanlistVersion.EffectiveDate"/> is: the 2002–2009
/// era announced by month only, so those dates carry day 1 with
/// <see cref="Month"/> recording the caveat.
/// </summary>
public enum DatePrecision
{
    Day,
    Month,
}
