namespace MtgArchive.Data;

/// <summary>
/// One Oracle Tag on a card — Scryfall's community-curated function labels
/// ("ramp", "removal"). A structured filter and appended card-document text.
/// The set is closed over Scryfall's tag ancestry: a direct tagging carries
/// its upstream weight; a tag reached only through a child's parent chain
/// is inherited and carries none.
/// </summary>
public sealed class CardOracleTag
{
    public int CardId { get; set; }

    public required string Tag { get; set; }

    public bool Inherited { get; set; }

    public string? Weight { get; set; }
}
