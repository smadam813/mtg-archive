using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.Banlist;

/// <summary>
/// The banlist axis for <c>banlist_version</c> pins: the replayed version
/// set, named on the wire by effective date; latest is the newest. Empty
/// until the first replay — then no latest to default to, and every pin is
/// unknown (<see cref="VersionParameter"/>). Card detail and every
/// candidate query resolve through here, so the (small, rarely changing)
/// version set is loaded once per process and held until the replay phase
/// invalidates it after committing.
/// </summary>
public sealed class BanlistVersionAxis
{
    private IReadOnlyList<BanlistVersionPin>? _known;
    private int _generation;

    /// <summary>Null only while no version has been replayed.</summary>
    public async Task<BanlistVersionPin?> ResolveAsync(
        ArchiveDbContext db, string? pinned, CancellationToken cancellationToken)
    {
        var known = _known ?? await LoadAsync(db, cancellationToken);
        var names = known.Select(v => v.Wire).ToList();
        var resolved = VersionParameter.Resolve(
            "banlist_version", pinned, names.Count == 0 ? null : names[^1], names);
        return resolved is null ? null : known[names.IndexOf(resolved)];
    }

    /// <summary>Drops the held set; the next request reloads it.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _known = null;
    }

    private async Task<IReadOnlyList<BanlistVersionPin>> LoadAsync(ArchiveDbContext db, CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _generation);
        var known = await db.BanlistVersions
            .OrderBy(v => v.EffectiveDate)
            .Select(v => new BanlistVersionPin(v.Id, v.EffectiveDate))
            .ToListAsync(cancellationToken);
        // A replay committing during this load bumps the generation; its
        // rows are what the next load sees, so this pre-commit read is not kept.
        if (generation == Volatile.Read(ref _generation))
        {
            _known = known;
        }

        return known;
    }
}

/// <summary>A resolved banlist version: its row id and its wire name.</summary>
public sealed record BanlistVersionPin(int Id, DateOnly EffectiveDate)
{
    public string Wire => EffectiveDate.ToString("O", CultureInfo.InvariantCulture);
}
