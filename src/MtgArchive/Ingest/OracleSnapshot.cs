namespace MtgArchive.Ingest;

/// <summary>
/// One fully downloaded and validated Scryfall bulk snapshot — Oracle Cards,
/// Rulings, and Oracle Tags joined by <c>oracle_id</c>. Built in memory by
/// the fetch phase before the first DB write; the card upsert phase diffs it
/// against current <c>card</c> rows (the DB is the diff baseline — there is
/// no snapshot file archive).
/// </summary>
public sealed class OracleSnapshot(
    IReadOnlyList<SnapshotCard> cards,
    IReadOnlyDictionary<Guid, List<SnapshotRuling>> rulingsByOracleId,
    SnapshotTags tags)
{
    public IReadOnlyList<SnapshotCard> Cards { get; } = cards;

    public IReadOnlyDictionary<Guid, List<SnapshotRuling>> RulingsByOracleId { get; } = rulingsByOracleId;

    public IReadOnlyDictionary<Guid, List<SnapshotTag>> TagsByOracleId { get; } = tags.ByOracleId;

    /// <summary>Alias to label, one entry per alias, no alias that is itself a label.</summary>
    public IReadOnlyDictionary<string, string> TagAliases { get; } = tags.Aliases;
}

/// <summary>The Oracle Tags file read: every card's closed tag set and the
/// alias map (<c>aliases</c> on each record). An alias names one label,
/// and the first record to claim it wins. The map skips an alias that
/// equals a label, so the label matches as itself.</summary>
public sealed record SnapshotTags(
    IReadOnlyDictionary<Guid, List<SnapshotTag>> ByOracleId,
    IReadOnlyDictionary<string, string> Aliases);

/// <summary>One Oracle Cards record, parsed but underived.</summary>
public sealed class SnapshotCard
{
    public required Guid OracleId { get; init; }

    public required string Name { get; init; }

    public string? Layout { get; init; }

    public required string TypeLine { get; init; }

    public string? ManaCost { get; init; }

    /// <summary>Scryfall's top-level <c>cmc</c> (whole-card for split layouts).</summary>
    public required decimal Cmc { get; init; }

    public string? OracleText { get; init; }

    public string[]? Colors { get; init; }

    public required string[] ColorIdentity { get; init; }

    public required string[] Keywords { get; init; }

    public string? Power { get; init; }

    public string? Toughness { get; init; }

    public string? Loyalty { get; init; }

    public string[]? ProducedMana { get; init; }

    public bool GameChanger { get; init; }

    /// <summary>The record is a meld result: its <c>all_parts</c> names the
    /// card itself as the <c>meld_result</c> component. A meld result is the
    /// combined back of a meld pair, never a card on its own (CR 712.4b).</summary>
    public bool IsMeldResult { get; init; }

    /// <summary>Scryfall's <c>legalities.commander</c> claim — drift-detection
    /// input only, never a legality source (ADR 0004).</summary>
    public string? CommanderLegality { get; init; }

    /// <summary>Scryfall <c>digital</c>: the stored printing exists only in
    /// a digital game. It describes that one printing, not the card; null when
    /// the record has no boolean <c>digital</c>.</summary>
    public bool? Digital { get; init; }

    public int? EdhrecRank { get; init; }

    public required DateOnly ReleasedAt { get; init; }

    public IReadOnlyList<SnapshotFace> Faces { get; init; } = [];

    /// <summary>The verbatim JSONL line — becomes the card's <c>raw</c> jsonb.</summary>
    public required string RawJson { get; init; }
}

/// <summary>One entry of a card's <c>card_faces</c>, front first.</summary>
public sealed class SnapshotFace
{
    public required string Name { get; init; }

    public string? ManaCost { get; init; }

    public required string TypeLine { get; init; }

    public string? OracleText { get; init; }

    public string[]? Colors { get; init; }

    public string? Power { get; init; }

    public string? Toughness { get; init; }

    public string? Loyalty { get; init; }
}

/// <summary>One Rulings record.</summary>
public sealed record SnapshotRuling(string Source, DateOnly PublishedAt, string Comment);

/// <summary>One Oracle Tag on one card. A direct tagging keeps Scryfall's
/// weight (<c>median</c>, <c>strong</c>, <c>very_strong</c>: stored, never
/// enumerated); an inherited one was reached through a child's parent chain
/// and carries none.</summary>
public sealed record SnapshotTag(string Label, bool Inherited, string? Weight);
