using MtgArchive.Data;

namespace MtgArchive.Ingest;

/// <summary>
/// The one status rule for materialized legality — a pure function so each
/// status derives the same way in tests and in the rebuild phase. Judged in
/// order: <c>not_legal</c> when Scryfall says the card is never
/// Commander-playable or the card is a meld result (the combined back of a
/// meld pair, which no deck can hold, though Scryfall calls it legal),
/// <c>not_eligible</c> when the card fails the role's
/// requirements, <c>banned</c> when a standing ban event covers the role,
/// else <c>legal</c>. Only replayed ban events produce <c>banned</c> — the
/// curated file is banlist truth; Scryfall <c>legalities.commander</c> feeds
/// only <c>not_legal</c> (ADR 0004), so a Scryfall "banned" is Commander-
/// playable here until the events file says otherwise (drift detection's
/// job, not this rule's).
/// </summary>
public static class LegalityDerivation
{
    public static LegalityStatus Derive(
        Role role,
        bool canBeCommander,
        bool hasCompanionKeyword,
        string? scryfallCommander,
        BanScope? standingBanScope,
        bool isMeldResult)
    {
        if (scryfallCommander == "not_legal" || isMeldResult)
        {
            return LegalityStatus.NotLegal;
        }

        var eligible = role switch
        {
            Role.Commander => canBeCommander,
            Role.Companion => hasCompanionKeyword,
            _ => true,
        };
        if (!eligible)
        {
            return LegalityStatus.NotEligible;
        }

        return standingBanScope is { } scope && Covers(scope, role)
            ? LegalityStatus.Banned
            : LegalityStatus.Legal;
    }

    /// <summary>
    /// The status rule for a Brawl format (ADR 0012), judged in the same
    /// order. Scryfall's key for the format is the truth: <c>legal</c> or
    /// <c>banned</c> is in the pool, and anything else — <c>not_legal</c>,
    /// or a key the record lacks — is <c>not_legal</c>, as is a meld result.
    /// Then <c>not_eligible</c> when the card fails the role (the Brawl
    /// commander rule; the Companion keyword), then <c>banned</c> for every
    /// role when the key says banned, else <c>legal</c>.
    /// </summary>
    public static LegalityStatus DeriveBrawl(
        Role role,
        bool canBeBrawlCommander,
        bool hasCompanionKeyword,
        string? scryfallStatus,
        bool isMeldResult)
    {
        if (scryfallStatus is not ("legal" or "banned") || isMeldResult)
        {
            return LegalityStatus.NotLegal;
        }

        var eligible = role switch
        {
            Role.Commander => canBeBrawlCommander,
            Role.Companion => hasCompanionKeyword,
            _ => true,
        };
        if (!eligible)
        {
            return LegalityStatus.NotEligible;
        }

        return scryfallStatus == "banned" ? LegalityStatus.Banned : LegalityStatus.Legal;
    }

    /// <summary>Which roles a ban's scope reaches (<see cref="BanScope"/>).
    /// Exhaustive on purpose: a new scope must fail here, never silently
    /// mis-scope (the closed-vocabulary convention).</summary>
    private static bool Covers(BanScope scope, Role role) => scope switch
    {
        BanScope.All => true,
        BanScope.CommanderOnly => role == Role.Commander,
        BanScope.Companion => role == Role.Companion,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };
}
