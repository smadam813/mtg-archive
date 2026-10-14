namespace MtgArchive.Data;

/// <summary>
/// One dated change to the Commander banlist: card + action + scope + the
/// source announcement (via <see cref="BanlistVersionId"/>). The unit of
/// banlist history and the citation target for a <c>banned</c> legality
/// status, resolved at <c>GET /ban-events/{id}</c>. Rows are derived from
/// <c>data/banlist-events.json</c> and from the category rules in
/// <c>data/banlist-categories.json</c>; the replay phase rebuilds them in place,
/// keyed by card within the announcement, so ids survive re-runs and
/// transcription corrections alike.
/// </summary>
public sealed class BanEvent
{
    public int Id { get; set; }

    public int BanlistVersionId { get; set; }

    public int CardId { get; set; }

    public Card Card { get; set; } = null!;

    public required BanAction Action { get; set; }

    public required BanScope Scope { get; set; }

    /// <summary>The <c>data/banlist-categories.json</c> key this event was
    /// expanded from (ADR 0010); null for an event the events file names.</summary>
    public string? Category { get; set; }
}

public enum BanAction
{
    Ban,
    Unban,
}

/// <summary>
/// Which seats the ban covers: every role, commander-only (the 2006–2014
/// era's "banned as commander"), or companion-only (2026–).
/// </summary>
public enum BanScope
{
    All,
    CommanderOnly,
    Companion,
}
