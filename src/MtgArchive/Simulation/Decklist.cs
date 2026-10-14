namespace MtgArchive.Simulation;

/// <summary>
/// The shared deck input contract across the deck routes: optional explicit
/// commander(s), an optional companion, plus entries of card and quantity.
/// The contract is tier-agnostic — the deck verdict is per-route (the
/// deck verdict route returns one, tier 1 never does), and no size or
/// commander requirement exists here: partial, mid-build lists are
/// first-class.
/// </summary>
public sealed record Decklist
{
    public IReadOnlyList<DecklistCard> Commanders { get; init; } = [];

    /// <summary>The companion: outside the deck, so it never counts toward
    /// the deck size. The deck verdict judges it in the companion role and
    /// against the commander's color identity; its own deck condition is
    /// out of scope.</summary>
    [System.ComponentModel.Description("Optional companion. It does not count toward the deck size. The deck verdict checks it in the companion role and against the commander's color identity, not its own deck condition.")]
    public DecklistCard? Companion { get; init; }

    public IReadOnlyList<DecklistEntry> Entries { get; init; } = [];
}

/// <summary>
/// One card reference: an exact Oracle name or an <c>oracle_id</c>. Names
/// resolve service-side through the same path as collection import.
/// </summary>
public sealed record DecklistCard
{
    public string? Name { get; init; }

    public Guid? OracleId { get; init; }
}

public sealed record DecklistEntry
{
    public required DecklistCard Card { get; init; }

    public required int Quantity { get; init; }
}
