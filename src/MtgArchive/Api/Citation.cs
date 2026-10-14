using System.Globalization;
using MtgArchive.Data;

namespace MtgArchive.Api;

/// <summary>
/// The one citation shape used everywhere: <c>{ type, id, label }</c>.
/// Every <see cref="Id"/> must be fetchable from a reference endpoint
/// (<c>GET /cards/{oracle_id}</c>, <c>GET /rules/{number}</c>,
/// <c>GET /ban-events/{id}</c>, ...) — no prose-only citations.
/// </summary>
public sealed record Citation(CitationType Type, string Id, string Label)
{
    /// <summary>The one <c>{ type: card }</c> citation shape: oracle id, card name.</summary>
    public static Citation OfCard(Guid oracleId, string name) => new(CitationType.Card, oracleId.ToString(), name);

    public static Citation OfCard(Card card) => OfCard(card.OracleId, card.Name);

    /// <summary>
    /// The one <c>{ type: legality }</c> citation shape: a Brawl format's
    /// legality status for one card (GLOSSARY.md "Citation", ADR 0012). The id
    /// is the oracle id, so it resolves through card detail, whose row for the
    /// key holds the Scryfall URL and the as-of date. The label states only
    /// what the key says: "<c>&lt;key&gt; &lt;key status&gt; as of &lt;yyyy-MM-dd&gt;</c>",
    /// the date in UTC. The key never holds <c>not_eligible</c>, so a row with
    /// that status adds the role fact the service derives:
    /// "<c>; not eligible as &lt;role&gt;</c>".
    /// </summary>
    /// <param name="keyStatus">The status of the card's main row in the
    /// format. The main role has no requirements, so that row holds what
    /// the key says.</param>
    /// <param name="role">The role of the cited row.</param>
    /// <param name="status">The status of the cited row.</param>
    public static Citation OfLegality(
        Guid oracleId, string scryfallKey, LegalityStatus keyStatus, DateTimeOffset asOf, Role role, LegalityStatus status)
        => new(
            CitationType.Legality,
            oracleId.ToString(),
            $"{scryfallKey} {ArchiveDbContext.WireName(keyStatus)} as of "
                + asOf.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + (status == LegalityStatus.NotEligible ? $"; not eligible as {ArchiveDbContext.WireName(role)}" : ""));
}

public enum CitationType
{
    Card,
    Rule,
    BanEvent,
    Legality,
    Snapshot,
}
