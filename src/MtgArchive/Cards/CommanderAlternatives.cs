using System.Numerics;
using MtgArchive.Api;

namespace MtgArchive.Cards;

/// <summary>
/// The commander-alternatives set arithmetic (ADR 0006, ADR 0012): pure, no
/// judgment. A candidate loses a deck member in two cases. Either the
/// request's format already loses it (banned, or outside the format's pool:
/// the <see cref="DeckMember.FormatLoss"/> it came with), or the member has
/// a color outside the candidate's identity (one bitwise test on the WUBRG
/// masks). The candidate retains every other member. The same rule judges
/// the companion, but the counts never include it.
/// Candidates rank by retained count descending. Ties go to color-identity
/// closeness to the original commander, then to the format's popularity
/// order (the commander order). Closeness is the symmetric difference of the
/// two masks; without it every five-color commander ranks at the top. A
/// candidate with no popularity rank (no ingest has derived the order yet)
/// falls back to <c>edhrec_rank</c>. Name and oracle id close the order so
/// pages are stable. Nothing here reads a database.
/// </summary>
public static class CommanderAlternatives
{
    public static IReadOnlyList<RankedAlternative> Rank(
        int originalMask, IReadOnlyList<DeckMember> deck, IEnumerable<CommanderCandidate> pool, int limit, DeckMember? companion = null)
        => [.. pool
            .Select(candidate => Evaluate(originalMask, deck, candidate, companion))
            .OrderByDescending(r => r.RetainedCount)
            .ThenBy(r => r.ColorIdentityDistance)
            .ThenBy(r => r.Candidate.PopularityRank ?? int.MaxValue)
            .ThenBy(r => r.Candidate.EdhrecRank ?? int.MaxValue)
            .ThenBy(r => r.Candidate.Name, StringComparer.Ordinal)
            .ThenBy(r => r.Candidate.OracleId)
            .Take(limit)];

    /// <summary>Partitions the deck into what the candidate retains and what
    /// it loses, and judges the companion apart. Every candidate loses a
    /// member the format loses, and that member keeps the citation it came with. A
    /// member with a color outside the candidate's identity cites itself.</summary>
    public static RankedAlternative Evaluate(
        int originalMask, IReadOnlyList<DeckMember> deck, CommanderCandidate candidate, DeckMember? companion = null)
    {
        var retained = new List<DeckMember>();
        var lost = new List<LostMember>();
        foreach (var member in deck)
        {
            if (Judge(member, candidate) is { } loss)
            {
                lost.Add(loss);
            }
            else
            {
                retained.Add(member);
            }
        }

        return new RankedAlternative(candidate, retained, lost, Distance(originalMask, candidate.ColorIdentityMask))
        {
            Companion = companion is null ? null : new CompanionOutcome(companion, Judge(companion, candidate)),
        };
    }

    /// <summary>The one loss rule: the format's loss first, then the
    /// color-identity escape; null when the candidate retains the card.</summary>
    private static LostMember? Judge(DeckMember member, CommanderCandidate candidate)
        => member.FormatLoss is { } loss ? new LostMember(member, loss.Reason, loss.Citation)
            : (member.ColorIdentityMask & ~candidate.ColorIdentityMask) != 0
                ? new LostMember(member, LossReason.ColorIdentity, Citation.OfCard(member.OracleId, member.Name))
                : null;

    /// <summary>Symmetric difference of two WUBRG masks: how many colors the
    /// two identities do not share.</summary>
    public static int Distance(int maskA, int maskB) => BitOperations.PopCount((uint)(maskA ^ maskB));
}

/// <summary>One card of the main deck (or the companion) with its summed
/// quantity, its WUBRG mask, and the loss the request's format gives it
/// whatever the commander, if any.</summary>
public sealed record DeckMember(Guid OracleId, string Name, int Quantity, int ColorIdentityMask, FormatLoss? FormatLoss);

/// <summary>Why the format loses a card for every candidate: banned, or
/// not legal (outside the format's pool), with the citation for it.</summary>
public sealed record FormatLoss(LossReason Reason, Citation Citation);

/// <summary>One commander-role-legal card from the boundary, with what the
/// ranking reads and the scoped availability the response echoes.</summary>
public sealed record CommanderCandidate(
    Guid OracleId,
    string Name,
    string[] ColorIdentity,
    int ColorIdentityMask,
    int? PopularityRank,
    int? EdhrecRank,
    int? Available);

public sealed record RankedAlternative(
    CommanderCandidate Candidate,
    IReadOnlyList<DeckMember> Retained,
    IReadOnlyList<LostMember> Lost,
    int ColorIdentityDistance)
{
    public int RetainedCount { get; } = Retained.Sum(m => m.Quantity);

    public int LostCount { get; } = Lost.Sum(m => m.Member.Quantity);

    /// <summary>The companion's outcome when the list has one; never in
    /// either count.</summary>
    public CompanionOutcome? Companion { get; init; }
}

/// <summary>The companion and its loss, or null <see cref="Loss"/> when the
/// candidate retains it.</summary>
public sealed record CompanionOutcome(DeckMember Member, LostMember? Loss);

public sealed record LostMember(DeckMember Member, LossReason Reason, Citation Citation);

public enum LossReason
{
    ColorIdentity,
    Banned,
    NotLegal,
}
