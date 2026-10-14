using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.Banlist;

/// <summary>GET /banlist/versions — every replayed version, oldest first.</summary>
public sealed record BanlistVersionsResponse(IReadOnlyList<BanlistVersionSummary> Versions);

/// <summary>
/// One dated announcement. <see cref="EffectiveDate"/> is the version's
/// address (<c>GET /banlist/{effective_date}</c>); month-precision dates
/// carry day 1 with <see cref="DatePrecision.Month"/> recording the caveat.
/// </summary>
public sealed record BanlistVersionSummary(
    DateOnly EffectiveDate,
    DatePrecision DatePrecision,
    string AnnouncementUrl);

/// <summary>
/// GET /banlist/{version} — the citation target a banlist version resolves
/// to: the announcement, its own events, and the full ban state produced by
/// replaying every event up to and including this date.
/// </summary>
public sealed record BanlistVersionDetail
{
    public required DateOnly EffectiveDate { get; init; }

    public required DatePrecision DatePrecision { get; init; }

    public required string AnnouncementUrl { get; init; }

    /// <summary>This announcement's changes, by card name.</summary>
    public required IReadOnlyList<BanEventDetail> Events { get; init; }

    /// <summary>
    /// The whole banlist as of this version, by card name: each card's
    /// latest event at or before this date, where that event is a ban.
    /// </summary>
    public required IReadOnlyList<BannedCard> Banned { get; init; }
}

/// <summary>One ban event as listed on a banlist version;
/// <see cref="Category"/> names the category rule it expanded from, absent
/// for an event the list names card by card.</summary>
public sealed record BanEventDetail(int Id, Citation Card, BanAction Action, BanScope Scope, string? Category);

/// <summary>
/// One entry of a version's ban state; <see cref="BanEvent"/> cites the
/// dated event that produced it, and <see cref="Category"/> names that
/// event's category rule, if any.
/// </summary>
public sealed record BannedCard(Citation Card, BanScope Scope, Citation BanEvent, string? Category);

/// <summary>
/// GET /ban-events/{id} — the citation target every
/// <c>{ type: ban_event, id }</c> citation resolves to: card, action, scope,
/// the category rule it expanded from (absent for a named event), and the
/// source announcement.
/// </summary>
public sealed record BanEventResponse(
    int Id,
    Citation Card,
    BanAction Action,
    BanScope Scope,
    string? Category,
    BanlistVersionSummary Announcement);
