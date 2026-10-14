using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.Bracket;

/// <summary>
/// The two bracket axes for <c>bracket_spec_version</c> and
/// <c>gc_snapshot</c> pins: the loaded spec versions, each parsed once, and
/// the loaded snapshot dates. Latest is the newest. Empty until the first
/// load; then there is no latest and every pin is unknown
/// (<see cref="VersionParameter"/>). Every classification resolves through
/// here, so the small, rarely changing set is loaded once per process and
/// held until the load phase invalidates it after committing. Mirrors
/// <see cref="Banlist.BanlistVersionAxis"/>.
/// </summary>
public sealed class BracketVersionAxis
{
    private Held? _held;
    private int _generation;

    /// <summary>Null only while no spec version has been loaded.</summary>
    public async Task<BracketSpec?> ResolveSpecAsync(ArchiveDbContext db, string? pinned, CancellationToken cancellationToken)
    {
        var held = _held ?? await LoadAsync(db, cancellationToken);
        var resolved = Resolve(BracketEndpoints.SpecParameter, pinned, held.Specs.Keys);
        return resolved is null ? null : held.Specs[resolved.Value];
    }

    /// <summary>Null only while no snapshot has been loaded.</summary>
    public async Task<DateOnly?> ResolveSnapshotAsync(ArchiveDbContext db, string? pinned, CancellationToken cancellationToken)
    {
        var held = _held ?? await LoadAsync(db, cancellationToken);
        return Resolve(BracketEndpoints.SnapshotParameter, pinned, held.SnapshotDates);
    }

    /// <summary>Drops the held set; the next request reloads it.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _held = null;
    }

    private static DateOnly? Resolve(string parameter, string? pinned, IEnumerable<DateOnly> dates)
    {
        var known = dates.Select(Wire).ToList();
        var resolved = VersionParameter.Resolve(parameter, pinned, known.Count == 0 ? null : known[^1], known);
        return resolved is null ? null : DateOnly.ParseExact(resolved, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string Wire(DateOnly date) => date.ToString("O", CultureInfo.InvariantCulture);

    private async Task<Held> LoadAsync(ArchiveDbContext db, CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _generation);
        var documents = await db.BracketSpecVersions
            .OrderBy(v => v.Date)
            .Select(v => new { v.Date, v.Document })
            .ToListAsync(cancellationToken);
        var specs = new SortedDictionary<DateOnly, BracketSpec>();
        foreach (var row in documents)
        {
            specs[row.Date] = Parse(row.Date, row.Document);
        }

        var snapshots = await db.GameChangersSnapshots
            .OrderBy(s => s.Date)
            .Select(s => s.Date)
            .ToListAsync(cancellationToken);
        var held = new Held(specs, snapshots);
        // A load committing during this read bumps the generation; its rows
        // are what the next load sees, so this pre-commit read is not kept.
        if (generation == Volatile.Read(ref _generation))
        {
            _held = held;
        }

        return held;
    }

    /// <summary>The load phase validated the document; a parse failure
    /// here is corpus corruption, not a caller error.</summary>
    private static BracketSpec Parse(DateOnly date, string document)
    {
        var errors = new List<string>();
        return BracketSpec.Parse($"bracket-spec/{Wire(date)}", document, errors)
            ?? throw new InvalidOperationException($"Stored bracket spec {Wire(date)} is invalid: {string.Join("; ", errors)}");
    }

    private sealed record Held(SortedDictionary<DateOnly, BracketSpec> Specs, IReadOnlyList<DateOnly> SnapshotDates);
}
