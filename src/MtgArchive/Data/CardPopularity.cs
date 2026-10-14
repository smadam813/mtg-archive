namespace MtgArchive.Data;

/// <summary>
/// One matched row of a popularity export (GLOSSARY.md "Popularity
/// export", ADR 0012): a card's Untapped stats in one Brawl format and one
/// seat. The deck row (<c>In Main Deck</c> holds a value) is seat
/// <see cref="Role.Main"/>; the commander row (<c>In Main Deck</c> blank,
/// <c>Popularity</c> set) is seat <see cref="Role.Commander"/>. The
/// popularity load phase rebuilds it every run from the local export
/// and orders the format's cards by it.
///
/// Each swapped table is one CLR shape with a live and a staging leaf (the
/// derived-table pattern; see <see cref="LegalityRow"/>).
/// </summary>
public abstract class CardPopularityRow
{
    public const string ShareContract =
        "The export's Popularity value in percent as Untapped wrote it (4.0 for \"4.0%\"): "
        + "for the main seat the share of decks that play the card, for the commander seat "
        + "the share of games with the card as commander.";

    public required int CardId { get; set; }

    public required Format Format { get; set; }

    public required Role Seat { get; set; }

    /// <summary>See <see cref="ShareContract"/>. Null when the row has no
    /// Popularity value.</summary>
    public decimal? Share { get; set; }

    /// <summary>The export's <c>Total Games</c>; null when blank.</summary>
    public int? TotalGames { get; set; }
}

/// <summary>See <see cref="CardPopularityRow"/>.</summary>
public sealed class CardPopularity : CardPopularityRow;

/// <summary>Staging twin of <see cref="CardPopularity"/> — table
/// <c>card_popularity_staging</c>.</summary>
public sealed class CardPopularityStaging : CardPopularityRow;
