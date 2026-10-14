namespace MtgArchive.Data;

/// <summary>
/// One face of a multi-faced card: per-face cost, type, and text. The parent
/// <see cref="Card"/> carries the derived aggregates the index filters on
/// (combined type line, front-face mana value, whole-card color identity).
/// Single-faced cards have no rows here.
/// </summary>
public sealed class CardFace
{
    public int Id { get; set; }

    public int CardId { get; set; }

    /// <summary>Face order as printed; 0 is the front face.</summary>
    public required int FaceIndex { get; set; }

    public required string Name { get; set; }

    public string? ManaCost { get; set; }

    public decimal? ManaValue { get; set; }

    public required string TypeLine { get; set; }

    public string? OracleText { get; set; }

    public string[]? Colors { get; set; }

    /// <summary>The colors a land face of a modal double-faced card makes:
    /// the card's <c>produced_mana</c> symbols this face's own "Add" text
    /// names. The source-profile phase derives it; null on every other face.</summary>
    public string[]? ProducedColors { get; set; }

    public string? Power { get; set; }

    public string? Toughness { get; set; }

    public string? Loyalty { get; set; }
}
