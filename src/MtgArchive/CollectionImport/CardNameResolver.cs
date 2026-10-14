using System.Text;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Cards;
using MtgArchive.Data;

namespace MtgArchive.CollectionImport;

/// <summary>
/// Maps card names to <c>oracle_id</c> by exact NFC match against the corpus
/// cards' Scryfall name — the sole resolution strategy for ManaBox rows,
/// where multi-face names are always the full <c>Front // Back</c>. Shared
/// by collection import and the Decklist simulation input contract, which
/// resolves names service-side through this same component.
///
/// With <c>faceNameFallback</c> a bare face name like <c>Fire</c> resolves
/// to its full-name card <c>Fire // Ice</c> when unambiguous. The parameter
/// is the surface of a pinned product decision the calling composition
/// enforces: plain-text decklist rows pass true, ManaBox rows never do.
///
/// Stale cards resolve — they are never deleted, so collection rows never
/// dangle; the ingest report, not the import, surfaces rows pointing at
/// them. When a name matches both a live card and a stale namesake (Scryfall
/// oracle_id migrations), the live row wins; of the rest, a non-token row
/// wins over a token namesake. This is <see cref="NamesakePrecedence"/>, the
/// same rule as <c>GET /cards?name=</c>.
///
/// The resolver never reads the CSV's <c>Scryfall ID</c> or <c>Set code</c>:
/// the corpus has no printing tables, so neither value can pick a candidate.
/// The accepted cost: a ManaBox row for an owned token resolves
/// to the non-token card of the same name. An example is the <c>ttsr</c>
/// Llanowar Elves token. The corpus has no data to separate the two.
/// </summary>
public sealed class CardNameResolver(ArchiveDbContext db)
{
    public async Task<NameResolution> ResolveAsync(
        string name, bool faceNameFallback, CancellationToken cancellationToken = default)
    {
        var resolutions = await ResolveAsync([name], faceNameFallback, cancellationToken);
        return resolutions[name];
    }

    /// <summary>Resolves a batch in two queries; the result is keyed by the caller's names.</summary>
    public async Task<IReadOnlyDictionary<string, NameResolution>> ResolveAsync(
        IReadOnlyCollection<string> names, bool faceNameFallback, CancellationToken cancellationToken = default)
    {
        var normalized = names.Distinct()
            .ToDictionary(n => n, n => n.Normalize(NormalizationForm.FormC));
        var lookup = normalized.Values.Distinct().ToList();

        var cardMatches = await db.Cards
            .Where(c => lookup.Contains(c.Name))
            .Select(c => new { c.Name, c.OracleId, c.Stale, c.Layout })
            .ToListAsync(cancellationToken);
        var byName = cardMatches
            .GroupBy(m => m.Name)
            .ToDictionary(g => g.Key, g => Classify(g.Select(m => (m.OracleId, m.Stale, m.Layout))));

        if (faceNameFallback)
        {
            var missing = lookup.Where(n => !byName.ContainsKey(n)).ToList();
            if (missing.Count > 0)
            {
                var faceMatches = await db.CardFaces
                    .Where(f => missing.Contains(f.Name))
                    .Join(db.Cards, f => f.CardId, c => c.Id,
                        (f, c) => new { f.Name, c.OracleId, c.Stale, c.Layout })
                    .ToListAsync(cancellationToken);
                foreach (var group in faceMatches.GroupBy(m => m.Name))
                {
                    // A face name can recur across faces of one card; oracle
                    // identity is what ambiguity is counted over.
                    byName[group.Key] = Classify(group
                        .DistinctBy(m => m.OracleId)
                        .Select(m => (m.OracleId, m.Stale, m.Layout)));
                }
            }
        }

        return normalized.ToDictionary(
            n => n.Key,
            n => byName.GetValueOrDefault(n.Value, NameResolution.Unresolved));
    }

    private static NameResolution Classify(IEnumerable<(Guid OracleId, bool Stale, string? Layout)> matches)
    {
        var candidates = NamesakePrecedence.Candidates(matches.ToList(), m => m.Stale, m => m.Layout);

        return candidates switch
        {
            [var single] => NameResolution.Resolved(single.OracleId),
            _ => NameResolution.Ambiguous(candidates.Select(m => m.OracleId)),
        };
    }
}
