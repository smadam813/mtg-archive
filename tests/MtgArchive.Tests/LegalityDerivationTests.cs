using MtgArchive.Data;
using MtgArchive.Ingest;

namespace MtgArchive.Tests;

/// <summary>
/// The pure status rule behind materialized legality: each
/// status, the role-eligibility requirements, ban-scope coverage, and the
/// judging order not_legal → not_eligible → banned → legal.
/// </summary>
public class LegalityDerivationTests
{
    [Theory]
    [InlineData(Role.Commander)]
    [InlineData(Role.Companion)]
    [InlineData(Role.Main)]
    public void Scryfall_not_legal_is_not_legal_for_every_role(Role role)
        => Assert.Equal(LegalityStatus.NotLegal, LegalityDerivation.Derive(
            role, canBeCommander: true, hasCompanionKeyword: true, "not_legal", BanScope.All, isMeldResult: false));

    // A meld result is never a card of its own (CR 712.4b), so it is never
    // Commander-playable, though Scryfall calls it legal.
    [Theory]
    [InlineData(Role.Commander)]
    [InlineData(Role.Companion)]
    [InlineData(Role.Main)]
    public void A_meld_result_is_not_legal_for_every_role(Role role)
        => Assert.Equal(LegalityStatus.NotLegal, LegalityDerivation.Derive(
            role, canBeCommander: true, hasCompanionKeyword: true, "legal", standingBanScope: null, isMeldResult: true));

    // ADR 0004: the curated file is banlist truth. A Scryfall "banned" flip
    // the events file doesn't explain is drift, never a banned status here.
    [Fact]
    public void Scryfall_banned_without_a_ban_event_stays_legal()
        => Assert.Equal(LegalityStatus.Legal, LegalityDerivation.Derive(
            Role.Main, canBeCommander: false, hasCompanionKeyword: false, "banned", standingBanScope: null, isMeldResult: false));

    [Fact]
    public void Commander_role_requires_can_be_commander()
    {
        Assert.Equal(LegalityStatus.NotEligible, LegalityDerivation.Derive(
            Role.Commander, canBeCommander: false, hasCompanionKeyword: false, "legal", null, isMeldResult: false));
        Assert.Equal(LegalityStatus.Legal, LegalityDerivation.Derive(
            Role.Commander, canBeCommander: true, hasCompanionKeyword: false, "legal", null, isMeldResult: false));
    }

    [Fact]
    public void Companion_role_requires_the_companion_keyword()
    {
        Assert.Equal(LegalityStatus.NotEligible, LegalityDerivation.Derive(
            Role.Companion, canBeCommander: true, hasCompanionKeyword: false, "legal", null, isMeldResult: false));
        Assert.Equal(LegalityStatus.Legal, LegalityDerivation.Derive(
            Role.Companion, canBeCommander: false, hasCompanionKeyword: true, "legal", null, isMeldResult: false));
    }

    [Fact]
    public void Main_role_has_no_eligibility_requirement()
        => Assert.Equal(LegalityStatus.Legal, LegalityDerivation.Derive(
            Role.Main, canBeCommander: false, hasCompanionKeyword: false, "legal", null, isMeldResult: false));

    [Theory]
    [InlineData(BanScope.All, Role.Commander, LegalityStatus.Banned)]
    [InlineData(BanScope.All, Role.Companion, LegalityStatus.Banned)]
    [InlineData(BanScope.All, Role.Main, LegalityStatus.Banned)]
    [InlineData(BanScope.CommanderOnly, Role.Commander, LegalityStatus.Banned)]
    [InlineData(BanScope.CommanderOnly, Role.Companion, LegalityStatus.Legal)]
    [InlineData(BanScope.CommanderOnly, Role.Main, LegalityStatus.Legal)]
    [InlineData(BanScope.Companion, Role.Commander, LegalityStatus.Legal)]
    [InlineData(BanScope.Companion, Role.Companion, LegalityStatus.Banned)]
    [InlineData(BanScope.Companion, Role.Main, LegalityStatus.Legal)]
    public void A_standing_ban_reaches_exactly_the_roles_its_scope_covers(
        BanScope scope, Role role, LegalityStatus expected)
        => Assert.Equal(expected, LegalityDerivation.Derive(
            role, canBeCommander: true, hasCompanionKeyword: true, "legal", scope, isMeldResult: false));

    // An all-scope ban on a card that fails the role's requirements: the
    // role status stays not_eligible — eligibility is judged before bans.
    [Fact]
    public void Not_eligible_wins_over_banned()
        => Assert.Equal(LegalityStatus.NotEligible, LegalityDerivation.Derive(
            Role.Commander, canBeCommander: false, hasCompanionKeyword: false, "legal", BanScope.All, isMeldResult: false));

    [Fact]
    public void Not_legal_wins_over_everything()
        => Assert.Equal(LegalityStatus.NotLegal, LegalityDerivation.Derive(
            Role.Commander, canBeCommander: false, hasCompanionKeyword: false, "not_legal", BanScope.All, isMeldResult: false));

    // Fixture corpora may lack legalities in raw entirely; absence never
    // reads as not_legal.
    [Fact]
    public void A_missing_scryfall_legality_stays_legal()
        => Assert.Equal(LegalityStatus.Legal, LegalityDerivation.Derive(
            Role.Main, canBeCommander: false, hasCompanionKeyword: false, null, null, isMeldResult: false));
}
