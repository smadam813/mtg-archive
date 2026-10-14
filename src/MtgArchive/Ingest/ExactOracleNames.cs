using Microsoft.EntityFrameworkCore;
using MtgArchive.Cards;
using MtgArchive.Data;

namespace MtgArchive.Ingest;

/// <summary>
/// The curated-file name lookup: exact Oracle name to corpus card, under
/// <see cref="NamesakePrecedence"/>. Namesakes that survive the rule go to
/// the error list, never a silent pick (the <see cref="CardEndpoints"/>
/// convention). A name matching nothing is simply absent from the result,
/// so <see cref="Require"/> reports it under the caller's file name.
/// </summary>
public static class ExactOracleNames
{
    /// <summary>The corpus row behind a name, with the text the pinned-text
    /// drift detector hashes (faces in face order).</summary>
    public sealed record Match(
        int Id, Guid OracleId, string Name, int? EdhrecRank, string? OracleText, IReadOnlyList<string?> FaceTexts);

    /// <summary>The id for a curated name, or an error naming the file: an
    /// unresolvable name is a hard ingest failure (the banlist-events pattern).</summary>
    public static int? Require(
        string file, string name, IReadOnlyDictionary<string, Match> resolved, List<string> errors)
    {
        if (resolved.TryGetValue(name, out var match))
        {
            return match.Id;
        }

        errors.Add($"{file}: no card named '{name}' — the reference is exact Oracle name");
        return null;
    }

    public static async Task<Dictionary<string, Match>> ResolveAsync(
        ArchiveDbContext db, IEnumerable<string> names, List<string> errors, CancellationToken cancellationToken)
    {
        var wanted = names.Distinct(StringComparer.Ordinal).ToArray();
        var matches = await db.Cards
            .Where(c => wanted.Contains(c.Name))
            .Select(c => new
            {
                c.Id,
                c.OracleId,
                c.Name,
                c.Stale,
                c.Layout,
                c.EdhrecRank,
                c.OracleText,
                FaceTexts = c.Faces.OrderBy(f => f.FaceIndex).Select(f => f.OracleText).ToList(),
            })
            .ToListAsync(cancellationToken);

        var resolved = new Dictionary<string, Match>(StringComparer.Ordinal);
        foreach (var group in matches.GroupBy(c => c.Name, StringComparer.Ordinal))
        {
            var candidates = NamesakePrecedence.Candidates(group.ToList(), c => c.Stale, c => c.Layout);
            if (candidates.Count > 1)
            {
                errors.Add($"ambiguous card name '{group.Key}': {candidates.Count} corpus cards share it");
            }
            else
            {
                var card = candidates[0];
                resolved[group.Key] = new Match(
                    card.Id, card.OracleId, card.Name, card.EdhrecRank, card.OracleText, card.FaceTexts);
            }
        }

        return resolved;
    }
}
