using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Data;
using MtgArchive.Ingest;

namespace MtgArchive.Bracket;

/// <summary>
/// Loads the bracket data into the derived tables: every dated bracket
/// spec version, every dated Game Changers snapshot, and the two curated
/// barometer lists. This is the curated-truth pattern of ADR 0004, like the
/// banlist replay. A malformed file or an unresolvable Oracle name is a
/// hard ingest failure that lists every problem; the previous load stays in
/// place. The replay diffs by date (or barometer) and card, so a re-run
/// ends in the same state with no duplicate rows. One SaveChanges commits
/// the phase.
///
/// After the load, the Game Changers drift detector compares Scryfall's
/// <c>game_changer</c> flag with the latest snapshot. A flip the snapshot
/// does not carry is report-only drift: the snapshot files stay the source
/// of truth until a human appends a dated snapshot with real provenance and
/// re-ingests. The detector never writes to the tables or the files.
/// </summary>
public sealed class BracketLoadPhase(
    ArchiveDbContext db,
    IConfiguration configuration,
    IHostEnvironment environment,
    BracketVersionAxis axis) : IIngestPhase
{
    public const string DriftKind = "game_changers";

    public string Name => IngestPhases.BracketLoad;

    public async Task RunAsync(IngestContext context, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var specs = BracketFiles.ReadSpecs(BracketFiles.ResolveSpecDir(configuration, environment), errors);
        var snapshots = BracketFiles.ReadSnapshots(BracketFiles.ResolveGameChangersDir(configuration, environment), errors);
        var lists = BracketFiles.ReadCuratedLists(BracketFiles.ResolveCuratedDir(configuration, environment), errors);
        if (errors.Count > 0)
        {
            throw new IngestValidationException(errors);
        }

        var names = snapshots.SelectMany(s => s.Cards).Concat(lists.SelectMany(l => l.Cards));
        var resolved = await ExactOracleNames.ResolveAsync(db, names, errors, cancellationToken);
        var snapshotCards = snapshots.ToDictionary(
            s => s.Date,
            s => s.Cards.Select(name => ExactOracleNames.Require($"game-changers/{s.Date:yyyy-MM-dd}.json", name, resolved, errors)).ToList());
        var listCards = lists.ToDictionary(
            l => l.Barometer,
            l => l.Cards.Select(name => ExactOracleNames.Require(ListFile(l.Barometer), name, resolved, errors)).ToList());
        if (errors.Count > 0)
        {
            throw new IngestValidationException(errors);
        }

        await ReplaySpecsAsync(context, specs, cancellationToken);
        await ReplaySnapshotsAsync(context, snapshots, snapshotCards, cancellationToken);
        await ReplayListsAsync(listCards, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        axis.Invalidate();

        try
        {
            DetectDrift(context, snapshots, resolved);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Report-only diagnostic: its failure lands in the report's
            // errors and never fails the phase whose load already committed.
            context.Report.Errors.Add($"Game Changers drift detection failed: {ex.Message}");
        }
    }

    private static string ListFile(string barometer)
        => barometer == BracketSpec.MassLandDenial ? BracketFiles.MassLandDenialFile : BracketFiles.ChainedExtraTurnsFile;

    private async Task ReplaySpecsAsync(
        IngestContext context, IReadOnlyList<BracketFiles.SpecFile> specs, CancellationToken cancellationToken)
    {
        var existing = await db.BracketSpecVersions.ToDictionaryAsync(v => v.Date, cancellationToken);
        foreach (var spec in specs)
        {
            if (!existing.Remove(spec.Spec.Date, out var row))
            {
                row = new BracketSpecVersion
                {
                    Date = spec.Spec.Date,
                    Source = spec.Spec.Source,
                    Document = spec.Document,
                    IngestRunId = context.RunId,
                };
                db.BracketSpecVersions.Add(row);
            }

            row.Source = spec.Spec.Source;
            row.Document = spec.Document;
            row.IngestRunId = context.RunId;
        }

        // Versions the directory no longer holds are derived rows to drop.
        db.BracketSpecVersions.RemoveRange(existing.Values);
    }

    private async Task ReplaySnapshotsAsync(
        IngestContext context,
        IReadOnlyList<BracketFiles.SnapshotFile> snapshots,
        Dictionary<DateOnly, List<int?>> cardIds,
        CancellationToken cancellationToken)
    {
        var existing = await db.GameChangersSnapshots
            .Include(s => s.Cards)
            .ToDictionaryAsync(s => s.Date, cancellationToken);
        foreach (var snapshot in snapshots)
        {
            if (!existing.Remove(snapshot.Date, out var row))
            {
                row = new GameChangersSnapshot
                {
                    Date = snapshot.Date,
                    Source = snapshot.Source,
                    IngestRunId = context.RunId,
                };
                db.GameChangersSnapshots.Add(row);
            }

            row.Source = snapshot.Source;
            row.IngestRunId = context.RunId;
            var desired = cardIds[snapshot.Date].Select(id => id!.Value).ToHashSet();
            row.Cards.RemoveAll(c => !desired.Contains(c.CardId));
            var current = row.Cards.Select(c => c.CardId).ToHashSet();
            row.Cards.AddRange(desired.Where(id => !current.Contains(id))
                .Select(id => new GameChangersSnapshotCard { SnapshotDate = snapshot.Date, CardId = id }));
        }

        db.GameChangersSnapshots.RemoveRange(existing.Values);
    }

    private async Task ReplayListsAsync(Dictionary<string, List<int?>> cardIds, CancellationToken cancellationToken)
    {
        var existing = await db.BarometerCards.ToListAsync(cancellationToken);
        var desired = cardIds
            .SelectMany(pair => pair.Value.Select(id => (Barometer: pair.Key, CardId: id!.Value)))
            .ToHashSet();
        var current = existing.Select(c => (c.Barometer, c.CardId)).ToHashSet();
        db.BarometerCards.RemoveRange(existing.Where(c => !desired.Contains((c.Barometer, c.CardId))));
        db.BarometerCards.AddRange(desired.Where(d => !current.Contains(d))
            .Select(d => new BarometerCard { Barometer = d.Barometer, CardId = d.CardId }));
    }

    /// <summary>
    /// The Game Changers drift detector (ADR 0004). A Scryfall card whose
    /// <c>game_changer</c> flag disagrees with the latest snapshot, in
    /// either direction, goes into the report's drift detections in name
    /// order. It reads only the snapshot the fetch phase validated and the
    /// rows the name lookup fetched.
    /// </summary>
    private static void DetectDrift(
        IngestContext context,
        IReadOnlyList<BracketFiles.SnapshotFile> snapshots,
        IReadOnlyDictionary<string, ExactOracleNames.Match> resolved)
    {
        if (context.Snapshot is not { } scryfall || snapshots.Count == 0)
        {
            return;
        }

        var latest = snapshots.MaxBy(s => s.Date)!;
        var members = latest.Cards.Select(name => resolved[name].OracleId).ToHashSet();
        var file = $"data/game-changers/{latest.Date:yyyy-MM-dd}.json";
        foreach (var card in scryfall.Cards.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            var listed = members.Contains(card.OracleId);
            if (card.GameChanger == listed)
            {
                continue;
            }

            context.Report.DriftDetections.Add(JsonSerializer.SerializeToElement(
                new GameChangersDriftDetection(
                    Kind: DriftKind,
                    OracleId: card.OracleId,
                    Name: card.Name,
                    ScryfallGameChanger: card.GameChanger,
                    SnapshotMember: listed,
                    Snapshot: latest.Date,
                    Resolution: $"report-only: {file} stays the source of truth. Append a dated snapshot with "
                        + "announcement provenance under data/game-changers/ and re-ingest."),
                WireJson.Options));
        }
    }
}

/// <summary>One Game Changers drift detection as reported (snake_case in
/// the ingest report's <c>drift_detections</c>); <c>kind</c> discriminates
/// it from the banlist and pinned-text drifts.</summary>
public sealed record GameChangersDriftDetection(
    string Kind,
    Guid OracleId,
    string Name,
    bool ScryfallGameChanger,
    bool SnapshotMember,
    DateOnly Snapshot,
    string Resolution);
