namespace MtgArchive.Data;

/// <summary>
/// One dated Game Changers snapshot, as the bracket load phase replayed it
/// from <c>data/game-changers/&lt;date&gt;.json</c>. ADR 0004: the curated
/// snapshots are truth. A Scryfall <c>game_changer</c> flip is report-only
/// drift and never writes here. The announcement date is the key and the
/// <c>gc_snapshot</c> pin's wire address.
/// </summary>
public sealed class GameChangersSnapshot
{
    public required DateOnly Date { get; set; }

    public required string Source { get; set; }

    public required int IngestRunId { get; set; }

    public List<GameChangersSnapshotCard> Cards { get; set; } = [];
}

/// <summary>One card on one snapshot.</summary>
public sealed class GameChangersSnapshotCard
{
    public required DateOnly SnapshotDate { get; set; }

    public required int CardId { get; set; }

    public Card Card { get; set; } = null!;
}
