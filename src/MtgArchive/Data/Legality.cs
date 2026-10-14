namespace MtgArchive.Data;

/// <summary>
/// One materialized legality fact: the status of one card in one format and
/// role, at one banlist version for Commander, derived at ingest by the
/// legality rebuild phase — never a boolean per card, never a model judgment
/// (GLOSSARY.md "Legality", ADR 0012). Fully materialized (Commander: every
/// card × 3 roles × every version, ~9M narrow rows; each Brawl format: every
/// card × 3 roles, one current state with no version) so
/// <c>legal_candidates(format, banlist_version, role)</c> resolves as one
/// indexed scan; the table is derived data, so the choice stays reversible
/// by replay. A Commander <c>banned</c> status cites the ban event in force
/// (<see cref="Reason"/>); every Brawl status cites its own row: the Scryfall
/// key and the card's Scryfall URL, dated by <see cref="LegalityAsOf"/>.
///
/// Each swapped table is one CLR shape with a live and a staging leaf (the
/// derived-table pattern): the abstract row carries the columns, the leaves the
/// table identities, so the twins can never drift apart property-by-property.
/// </summary>
public abstract class LegalityRow
{
    public const string VersionKeyContract =
        "Key column the database generates: banlist_version_id, or 0 for a Brawl "
        + "format, which has no banlist version (ADR 0012).";

    public const string LegalityAsOfContract =
        "The Scryfall bulk updated_at that dates a Brawl format's legality; "
        + "NULL for Commander. Current state only: nobody can pin it.";

    public required int CardId { get; set; }

    /// <summary>The format the status holds in. Part of the key.</summary>
    public Format Format { get; set; } = Format.Commander;

    /// <summary>The Commander banlist version the status holds at; null for
    /// a Brawl format, which has no version axis. A check constraint ties
    /// it to the format.</summary>
    public required int? BanlistVersionId { get; set; }

    /// <summary>See <see cref="VersionKeyContract"/>. Generated, so a key
    /// can hold the versionless Brawl rows.</summary>
    public int VersionKey { get; private set; }

    public required Role Role { get; set; }

    public required LegalityStatus Status { get; set; }

    /// <summary>The standing ban event a Commander <c>banned</c> status
    /// cites — the citation the banned-commander done-query needs; null
    /// otherwise.</summary>
    public int? Reason { get; set; }

    /// <summary>See <see cref="LegalityAsOfContract"/>.</summary>
    public DateTimeOffset? LegalityAsOf { get; set; }

    /// <summary>The Scryfall legality key a Brawl row's <c>legality</c>
    /// citation names (<c>brawl</c>, <c>competitivebrawl</c>,
    /// <c>standardbrawl</c>), whatever the status; null for
    /// Commander.</summary>
    public string? ScryfallKey { get; set; }

    /// <summary>The card's Scryfall URL that card detail serves on a Brawl
    /// row, the target of its <c>legality</c> citation; null for
    /// Commander.</summary>
    public string? ScryfallUri { get; set; }
}

/// <summary>See <see cref="LegalityRow"/>.</summary>
public sealed class Legality : LegalityRow;

/// <summary>
/// Staging twin of <see cref="Legality"/> — table <c>legality_staging</c>.
/// The rebuild phase regenerates the whole table here and publishes via
/// <see cref="Ingest.DerivedStateSwap"/>.
/// </summary>
public sealed class LegalityStaging : LegalityRow;

/// <summary>The seat a card occupies in a deck; legality is judged per role.</summary>
public enum Role
{
    Commander,
    Companion,
    Main,
}

public enum LegalityStatus
{
    Legal,
    Banned,
    NotLegal,
    NotEligible,
}
