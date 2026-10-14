using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.CollectionImport;
using MtgArchive.Data;

namespace MtgArchive.Simulation;

/// <summary>
/// Resolves a <see cref="Decklist"/>'s card references to <c>oracle_id</c>s
/// through <see cref="CardNameResolver"/> — the same resolution path as
/// collection import, with the plain-text front-face fallback. A name that
/// does not resolve and starts with <c>A-</c> (an Arena rebalanced name)
/// resolves again without the prefix; each such rewrite is in
/// <see cref="ResolvedDecklist.NameRewrites"/> for the deck route to report.
/// A full name that resolves wins first, so <c>A-Town</c> stays itself. An entry by
/// <c>oracle_id</c> and the same card by exact name resolve identically:
/// both are checked against the corpus. Cards that fail to resolve to a
/// known oracle identity are surfaced in
/// <see cref="ResolvedDecklist.Unresolved"/> with their commander role
/// intact; their entries are excluded from the resolved list.
/// </summary>
public sealed class DecklistResolver(ArchiveDbContext db)
{
    private readonly CardNameResolver _names = new(db);

    public async Task<ResolvedDecklist> ResolveAsync(
        Decklist decklist, CancellationToken cancellationToken = default)
        => Resolve(decklist, await LookupAsync(Cards(decklist), cancellationToken));

    /// <summary>Resolves the decklist through a lookup from
    /// <see cref="LookupAsync"/> and carries the lookup's rewrites.</summary>
    public static ResolvedDecklist Resolve(Decklist decklist, DecklistLookup lookup)
        => Resolve(decklist, lookup.Resolve) with { NameRewrites = lookup.Rewrites };

    /// <summary>Every card reference in the decklist, for
    /// <see cref="LookupAsync"/>. Validates quantities first, so a bad
    /// request fails before any lookup.</summary>
    public static IEnumerable<DecklistCard> Cards(Decklist decklist)
    {
        Validate(decklist);
        return decklist.Commanders
            .Concat(decklist.Companion is { } companion ? [companion] : [])
            .Concat(decklist.Entries.Select(e => e.Card));
    }

    private static void Validate(Decklist decklist)
    {
        var invalid = decklist.Entries.FirstOrDefault(e => e.Quantity < 1);
        if (invalid is not null)
        {
            throw new ArgumentException($"Quantity {invalid.Quantity} is not a positive integer.");
        }
    }

    /// <summary>Resolves the decklist through a lookup from
    /// <see cref="LookupAsync"/>, so a caller with more cards to resolve
    /// (categories) makes one batch call for all of them.</summary>
    public static ResolvedDecklist Resolve(Decklist decklist, Func<DecklistCard, Guid?> resolve)
    {
        Validate(decklist);

        var commanders = decklist.Commanders
            .Select(c => (Card: c, OracleId: resolve(c)))
            .ToList();
        var entries = decklist.Entries
            .Select(e => (e.Card, OracleId: resolve(e.Card), e.Quantity))
            .ToList();
        var companion = decklist.Companion is { } companionCard
            ? (Card: companionCard, OracleId: resolve(companionCard))
            : default;

        // A commander is one card outside the library. The same oracle_id
        // twice among the commanders, or once as a commander and once as an
        // entry, is not a decklist the deck loader can represent.
        var seen = new HashSet<Guid>();
        var entryIds = entries.Select(e => e.OracleId).OfType<Guid>().ToHashSet();
        foreach (var (card, oracleId) in commanders)
        {
            if (oracleId is not { } id)
            {
                continue;
            }

            var label = card.Name ?? id.ToString();
            if (!seen.Add(id))
            {
                throw new ArgumentException($"Commander {label} is listed twice.");
            }

            if (entryIds.Contains(id))
            {
                throw new ArgumentException($"Commander {label} is also a library entry.");
            }
        }

        return new ResolvedDecklist
        {
            Commanders = [.. commanders.Where(c => c.OracleId is not null).Select(c => c.OracleId!.Value)],
            Companion = companion.OracleId,
            Entries =
            [
                .. entries
                    .Where(e => e.OracleId is not null)
                    .Select(e => new ResolvedEntry(e.OracleId!.Value, e.Quantity)),
            ],
            Unresolved =
            [
                .. commanders
                    .Where(c => c.OracleId is null)
                    .Select(c => new UnresolvedCard(c.Card, IsCommander: true))
                    .Concat(companion.Card is not null && companion.OracleId is null
                        ? [new UnresolvedCard(companion.Card, IsCommander: false)]
                        : [])
                    .Concat(entries
                        .Where(e => e.OracleId is null)
                        .Select(e => new UnresolvedCard(e.Card, IsCommander: false)))
                    .Distinct(),
            ],
        };
    }

    /// <summary>
    /// Resolves a batch of card references (names, then the <c>A-</c>
    /// retries, then supplied oracle_ids) and returns the per-card lookup:
    /// the known oracle_id, or null when the reference resolves to nothing.
    /// </summary>
    public async Task<DecklistLookup> LookupAsync(
        IEnumerable<DecklistCard> cards, CancellationToken cancellationToken = default)
    {
        var list = cards.ToList();
        var names = list
            .Where(c => c.OracleId is null)
            .Select(c => c.Name
                ?? throw new ArgumentException("A decklist card needs a name or an oracle_id."))
            .Distinct()
            .ToList();
        var outcomes = await _names.ResolveAsync(names, faceNameFallback: true, cancellationToken);
        var resolutions = outcomes
            .ToDictionary(r => r.Key, r => r.Value.Outcome == NameResolutionOutcome.Resolved ? r.Value.OracleId : null);
        var rewrites = await RetryRebalancedAsync(
            [.. names.Where(n => outcomes[n].Outcome == NameResolutionOutcome.Unresolved)], resolutions, cancellationToken);

        var suppliedIds = list
            .Select(c => c.OracleId)
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var knownIds = (await db.Cards
            .Where(c => suppliedIds.Contains(c.OracleId))
            .Select(c => c.OracleId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        return new DecklistLookup(
            card => card.OracleId is { } oracleId
                ? knownIds.Contains(oracleId) ? oracleId : null
                : resolutions[card.Name!],
            rewrites);
    }

    private const string RebalancedPrefix = "A-";

    /// <summary>Resolves each <c>A-</c> name in <paramref name="unresolved"/>
    /// again without the prefix, updates <paramref name="resolutions"/> with
    /// every hit, and returns the rewrites in decklist order. The caller
    /// passes only names that matched nothing: an ambiguous full name matched,
    /// just not once, so it is not retried.</summary>
    private async Task<IReadOnlyList<NameRewrite>> RetryRebalancedAsync(
        IReadOnlyList<string> unresolved, Dictionary<string, Guid?> resolutions, CancellationToken cancellationToken)
    {
        var retries = unresolved
            .Where(n => n.Length > RebalancedPrefix.Length
                && n.StartsWith(RebalancedPrefix, StringComparison.Ordinal))
            .ToList();
        if (retries.Count == 0)
        {
            return [];
        }

        var originals = await _names.ResolveAsync(
            [.. retries.Select(n => n[RebalancedPrefix.Length..]).Distinct()], faceNameFallback: true, cancellationToken);
        var hits = retries
            .Select(n => (Name: n, Resolution: originals[n[RebalancedPrefix.Length..]]))
            .Where(h => h.Resolution.Outcome == NameResolutionOutcome.Resolved)
            .Select(h => (h.Name, OracleId: h.Resolution.OracleId!.Value))
            .ToList();
        if (hits.Count == 0)
        {
            return [];
        }

        var ids = hits.Select(h => h.OracleId).Distinct().ToList();
        var labels = (await db.Cards
                .Where(c => ids.Contains(c.OracleId))
                .Select(c => new { c.OracleId, c.Name })
                .ToListAsync(cancellationToken))
            .ToDictionary(c => c.OracleId, c => c.Name);
        foreach (var (name, oracleId) in hits)
        {
            resolutions[name] = oracleId;
        }

        return [.. hits.Select(h => new NameRewrite(h.Name, Citation.OfCard(h.OracleId, labels[h.OracleId])))];
    }
}

/// <summary>The per-card lookup <see cref="DecklistResolver.LookupAsync"/>
/// returns, with the <c>A-</c> rewrites it made.</summary>
public sealed record DecklistLookup(Func<DecklistCard, Guid?> Resolve, IReadOnlyList<NameRewrite> Rewrites);

/// <summary>
/// A decklist with every card reference resolved to a corpus
/// <c>oracle_id</c>. <see cref="DeckSize"/> is the sum of entry quantities —
/// the echo tier-1 output carries. No legality verdict lives here: the
/// contract is tier-agnostic, and tier 1 attaches none.
/// </summary>
public sealed record ResolvedDecklist
{
    private Dictionary<Guid, int>? _quantities;
    private int? _deckSize;

    public IReadOnlyList<Guid> Commanders { get; init; } = [];

    /// <summary>The companion, outside the deck: not in
    /// <see cref="DeckSize"/>.</summary>
    public Guid? Companion { get; init; }

    public IReadOnlyList<ResolvedEntry> Entries { get; init; } = [];

    public IReadOnlyList<UnresolvedCard> Unresolved { get; init; } = [];

    /// <summary>The <c>A-</c> names that resolved only without the prefix.</summary>
    public IReadOnlyList<NameRewrite> NameRewrites { get; init; } = [];

    /// <summary><see cref="NameRewrites"/> for a response: null when there
    /// is none, so the member stays off the wire.</summary>
    public IReadOnlyList<NameRewrite>? WireRewrites => NameRewrites.Count == 0 ? null : NameRewrites;

    public int DeckSize => _deckSize ??= Entries.Sum(e => e.Quantity);

    /// <summary>
    /// Total quantity of the entries carrying this oracle_id — the per-card
    /// term the tier-1 screen sums per category, cached so the
    /// recommendation loop never rescans the entry list.
    /// </summary>
    public int QuantityOf(Guid oracleId) => (_quantities ??= Entries
        .GroupBy(e => e.OracleId)
        .ToDictionary(g => g.Key, g => g.Sum(e => e.Quantity)))
        .GetValueOrDefault(oracleId);
}

/// <summary>
/// A card that failed to resolve to a known oracle identity — unknown
/// <c>oracle_id</c>, unresolved name, or ambiguous namesake — with its
/// commander role, so a dropped commander is never silent.
/// </summary>
public sealed record UnresolvedCard(DecklistCard Card, bool IsCommander)
{
    public UnresolvedName ToWire() => new(Card.Name, Card.OracleId, IsCommander);
}

/// <summary>One <c>unresolved_names</c> entry: the reference as sent and its role.</summary>
public sealed record UnresolvedName(string? Name, Guid? OracleId, bool IsCommander);

public sealed record ResolvedEntry(Guid OracleId, int Quantity);
