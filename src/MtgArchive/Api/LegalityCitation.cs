using MtgArchive.Banlist;
using MtgArchive.Data;

namespace MtgArchive.Api;

/// <summary>
/// The one rule that picks the citation for a legality status. A Brawl row
/// cites its <c>legality</c> row (<see cref="Citation.OfLegality"/>). A
/// Commander row has no Scryfall key, so it cites its ban event, else the
/// card.
/// </summary>
public static class LegalityCitation
{
    /// <summary>The <c>legality</c> citation of one row; null for a
    /// Commander row, which has no Scryfall key or as-of date.</summary>
    /// <param name="keyStatus">The status of the card's main row in the
    /// same format (see <see cref="Citation.OfLegality"/>).</param>
    public static Citation? OfRow(
        Guid oracleId, string? scryfallKey, DateTimeOffset? legalityAsOf, LegalityStatus keyStatus, Role role, LegalityStatus status)
        => scryfallKey is { } key && legalityAsOf is { } asOf
            ? Citation.OfLegality(oracleId, key, keyStatus, asOf, role, status)
            : null;

    /// <summary>The citation for one status: the <c>legality</c> row, else
    /// the ban event, else the card.</summary>
    public static Citation Pick(
        Guid oracleId,
        string cardName,
        string? scryfallKey,
        DateTimeOffset? legalityAsOf,
        LegalityStatus keyStatus,
        Role role,
        LegalityStatus status,
        BanReason? banEvent)
        => OfRow(oracleId, scryfallKey, legalityAsOf, keyStatus, role, status)
            ?? (banEvent is { } e ? BanlistEndpoints.EventCitation(e, cardName) : Citation.OfCard(oracleId, cardName));
}
