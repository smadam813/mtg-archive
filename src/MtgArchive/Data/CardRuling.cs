namespace MtgArchive.Data;

/// <summary>
/// One Scryfall ruling on a card. Served with a card hit, never embedded.
/// </summary>
public sealed class CardRuling
{
    public int Id { get; set; }

    public int CardId { get; set; }

    /// <summary>Scryfall ruling source ("wotc" or "scryfall").</summary>
    public required string Source { get; set; }

    public required DateOnly PublishedAt { get; set; }

    public required string Comment { get; set; }
}
