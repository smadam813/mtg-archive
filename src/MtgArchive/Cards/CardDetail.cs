using System.ComponentModel;
using System.Text.Json.Serialization;
using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.Cards;

/// <summary>
/// Full card detail — the citation target every <c>{ type: card, id }</c>
/// citation resolves to. Rulings are served here and nowhere else. The shape
/// is frozen. Only new members may join it (<c>legality</c>;
/// <c>digital_only</c>).
/// </summary>
public sealed record CardDetailResponse
{
    public required Guid OracleId { get; init; }

    public required string Name { get; init; }

    public string? ManaCost { get; init; }

    public required decimal ManaValue { get; init; }

    public required string TypeLine { get; init; }

    public required string[] Supertypes { get; init; }

    public required string[] CardTypes { get; init; }

    public required string[] Subtypes { get; init; }

    public string? OracleText { get; init; }

    public string[]? Colors { get; init; }

    public required string[] ColorIdentity { get; init; }

    public required string[] Keywords { get; init; }

    public string? Power { get; init; }

    public string? Toughness { get; init; }

    public string? Loyalty { get; init; }

    public string[]? ProducedMana { get; init; }

    public required bool GameChanger { get; init; }

    [Description(Data.Card.DigitalOnlyContract)]
    public required bool DigitalOnly { get; init; }

    public int? EdhrecRank { get; init; }

    public required DateOnly ReleasedAt { get; init; }

    /// <summary>
    /// See <see cref="Data.Card.CopyLimitContract"/>. The null (= unlimited)
    /// is a meaningful value, so this member is always written instead of
    /// omitted like other null members.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [Description(Data.Card.CopyLimitContract)]
    public required int? CopyLimit { get; init; }

    /// <summary>Stale cards keep resolving here — see <see cref="Data.Card.StaleContract"/>.</summary>
    public required bool Stale { get; init; }

    /// <summary>Per-face detail, front face first; empty for single-faced cards.</summary>
    public required IReadOnlyList<CardFaceDetail> Faces { get; init; }

    [Description("Per-role legality in each of the five formats, ordered commander, brawl, "
        + "competitive_brawl, standard_brawl, standard, then by role. The banlist_version pin applies to the "
        + "commander rows only. Absent until a legality rebuild has run.")]
    public IReadOnlyList<RoleLegality>? Legality { get; init; }

    public required IReadOnlyList<CardRulingDetail> Rulings { get; init; }

    [Description("Oracle Tags the card carries, sorted: direct taggings closed over Scryfall's tag "
        + "ancestry. `taggings` tells the two apart.")]
    public required IReadOnlyList<string> Tags { get; init; }

    [Description("One entry per tag in `tags`, same order: whether the card carries it only by "
        + "inheritance, and the upstream weight of a direct tagging (null when inherited).")]
    public required IReadOnlyList<CardTagging> Taggings { get; init; }

    public required VersionsBlock Versions { get; init; }
}

/// <summary>One face of a multi-faced card, mirroring <see cref="Data.CardFace"/>.</summary>
public sealed record CardFaceDetail
{
    public required int FaceIndex { get; init; }

    public required string Name { get; init; }

    public string? ManaCost { get; init; }

    public decimal? ManaValue { get; init; }

    public required string TypeLine { get; init; }

    public string? OracleText { get; init; }

    public string[]? Colors { get; init; }

    public string? Power { get; init; }

    public string? Toughness { get; init; }

    public string? Loyalty { get; init; }
}

/// <summary>One Scryfall ruling — served only on card detail.</summary>
public sealed record CardRulingDetail(string Source, DateOnly PublishedAt, string Comment);

/// <summary>One Oracle Tag on a card with its provenance: inherited through
/// Scryfall's tag ancestry, or direct with the upstream tagging weight.</summary>
public sealed record CardTagging(string Tag, bool Inherited, string? Weight);

/// <summary>
/// One row of per-role legality. A Commander row holds at the resolved
/// banlist version, and its <see cref="LegalityStatus.Banned"/> status
/// carries its dated ban event as a <c>{ type: ban_event, id, label }</c>
/// citation. A Brawl or Standard row (card detail only) holds the format's one current
/// state: the Scryfall key, the card's Scryfall URL, the as-of date, and its
/// <c>{ type: legality }</c> citation, whatever the status (ADR 0012). A
/// search hit leaves <see cref="Format"/> out: the versions block names it.
/// </summary>
public sealed record RoleLegality(Role Role, LegalityStatus Status, Citation? BanEvent)
{
    public Format? Format { get; init; }

    [Description("An unversioned format's Scryfall legality key: brawl, competitivebrawl, standardbrawl, or standard.")]
    public string? ScryfallKey { get; init; }

    [Description("The card's Scryfall URL. It shows the current state; legality_as_of dates this row.")]
    public string? ScryfallUri { get; init; }

    [Description(LegalityRow.LegalityAsOfContract)]
    public DateTimeOffset? LegalityAsOf { get; init; }

    [Description("A Brawl or Standard row's { type: legality } citation. Its id is the oracle id, which resolves "
        + "here; its label is \"<key> <status> as of <yyyy-MM-dd>\".")]
    public Citation? Citation { get; init; }
}
