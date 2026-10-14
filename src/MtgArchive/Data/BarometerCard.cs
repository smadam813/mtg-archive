namespace MtgArchive.Data;

/// <summary>
/// One card on one curated barometer list — <c>mass_land_denial</c> from
/// <c>data/curated/mass-land-denial.json</c>, <c>chained_extra_turns</c>
/// from <c>data/curated/chained-extra-turns.json</c>. The lists are not
/// versioned: the load phase replaces each list's rows in place every
/// ingest (the banlist-events pattern: the file is truth). The Game
/// Changers barometer reads the dated snapshots instead.
/// </summary>
public sealed class BarometerCard
{
    /// <summary>The barometer name as the bracket spec names it.</summary>
    public required string Barometer { get; set; }

    public required int CardId { get; set; }

    public Card Card { get; set; } = null!;
}
