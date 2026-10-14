using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Banlist;
using MtgArchive.Data;

namespace MtgArchive.Tests;

/// <summary>
/// The banlist replay property test: each Wayback fixture is a
/// dated full banlist captured from the announcements' own era, and
/// replaying <c>data/banlist-events.json</c> up to that date must reproduce
/// it exactly — set equality per scope (ADR 0004: transcription drift fails
/// loudly). The derivation is the real pipeline — the ingest replay phase
/// into <c>banlist_version</c>/<c>ban_event</c> rows, then
/// <see cref="BanState.Replay"/> — never replay logic of the test's own.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BanlistWaybackReplayTests(PostgresFixture postgres)
{
    private static readonly string WaybackDirectory =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "banlist", "wayback");

    // One replayed database serves every case; the gate makes the first
    // arrival build it even if the cases ever run in parallel.
    private static readonly SemaphoreSlim ReplayGate = new(1, 1);
    private static string? _replayed;

    /// <summary>Every fixture file is a case; a new file needs no code change.</summary>
    public static TheoryData<string> Snapshots => new(
        Directory.EnumerateFiles(WaybackDirectory, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f)));

    [Theory]
    [MemberData(nameof(Snapshots))]
    public async Task Replaying_history_up_to_a_snapshot_reproduces_its_full_banlist(string snapshotDate)
    {
        var snapshot = JsonSerializer.Deserialize<WaybackSnapshot>(
            await File.ReadAllTextAsync(Path.Combine(WaybackDirectory, $"{snapshotDate}.json")),
            WireJson.Options)!;

        await using var db = PostgresFixture.CreateContext(await ReplayedHistoryAsync());
        var history = await db.BanlistVersions
            .Where(v => v.EffectiveDate <= snapshot.EffectiveDate)
            .Include(v => v.Events).ThenInclude(e => e.Card)
            .AsNoTracking()
            .ToListAsync();
        var inForce = BanState.Replay(history).ToLookup(e => e.Scope, e => e.Card.Name);

        var problems = new List<string>();
        Compare(problems, snapshotDate, "banned", snapshot.Banned, inForce[BanScope.All]);
        Compare(problems, snapshotDate, "banned_as_commander", snapshot.BannedAsCommander, inForce[BanScope.CommanderOnly]);
        Compare(problems, snapshotDate, "banned_as_companion", snapshot.BannedAsCompanion, inForce[BanScope.Companion]);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>A corpus of every transcribed name, replayed by the ingest phase.</summary>
    private async Task<string> ReplayedHistoryAsync()
    {
        await ReplayGate.WaitAsync();
        try
        {
            if (_replayed is not null)
            {
                return _replayed;
            }

            var errors = new List<string>();
            var history = BanlistEventsFile.Read(BanlistEventsFileTests.TranscribedEventsPath, errors);
            Assert.Empty(errors);

            var connectionString = await postgres.CreateDatabaseAsync($"banlist_wayback_{Guid.NewGuid():N}");
            await using (var db = PostgresFixture.CreateContext(connectionString))
            {
                await db.Database.MigrateAsync();
                db.Cards.AddRange(history
                    .SelectMany(v => v.Events).Select(e => e.Card)
                    .Distinct(StringComparer.Ordinal)
                    .Select(name => BanlistReplayTests.Named(Guid.NewGuid(), name)));
                await db.SaveChangesAsync();
            }

            await BanlistReplayTests.ReplayAsync(connectionString, BanlistEventsFileTests.TranscribedEventsPath);
            return _replayed = connectionString;
        }
        finally
        {
            ReplayGate.Release();
        }
    }

    /// <summary>Set equality; a mismatch itemizes names under its fixture list.</summary>
    private static void Compare(List<string> problems, string snapshotDate, string list,
        IEnumerable<string> snapshot, IEnumerable<string> replayed)
    {
        var expected = snapshot.ToHashSet(StringComparer.Ordinal);
        var actual = replayed.ToHashSet(StringComparer.Ordinal);
        Itemize(problems, snapshotDate, list, "missing", expected.Except(actual));
        Itemize(problems, snapshotDate, list, "extra", actual.Except(expected));
    }

    private static void Itemize(List<string> problems, string snapshotDate, string list,
        string kind, IEnumerable<string> names)
    {
        var sorted = names.Order(StringComparer.Ordinal).ToList();
        if (sorted.Count > 0)
        {
            problems.Add($"{snapshotDate} {list}: {kind} {string.Join(", ", sorted.Select(n => $"'{n}'"))}");
        }
    }

    // wayback_url is provenance for humans; the comparison never reads it.
    private sealed record WaybackSnapshot(
        DateOnly EffectiveDate,
        string[] Banned,
        string[] BannedAsCommander,
        string[] BannedAsCompanion);
}
