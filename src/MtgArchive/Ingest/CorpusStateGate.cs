using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Banlist;
using MtgArchive.Bracket;
using MtgArchive.Data;
using MtgArchive.Rules;

namespace MtgArchive.Ingest;

/// <summary>
/// The no-op short-circuit's source-freshness probe. At every run start the
/// runner gathers the current stamps — Scryfall bulk <c>updated_at</c> from
/// the catalog, the CR header's effective date plus the normalized text's
/// SHA-256, SHA-256 of each curated file and popularity export — and, unless the trigger forced,
/// compares them against the stored <c>corpus_state</c> row: all unchanged
/// after a successful previous run means a recorded no-op run — unless the
/// stored embedding state disagrees with the configured model, because a
/// model or prefix change is recovered only by the re-embed phase. A probe
/// failure never fails the run here — the component stays null or absent,
/// which can only force a full run, and the owning phase then fails loudly.
/// </summary>
public sealed class CorpusStateGate(
    ArchiveDbContext db,
    IScryfallBulkSource bulkSource,
    ICrSource crSource,
    IConfiguration configuration,
    IHostEnvironment environment,
    Embedding.EmbeddingStateGate embeddingGate,
    ILogger<CorpusStateGate> logger)
{
    /// <summary>Reads the current stamps into the context. The three probes
    /// are independent, so they overlap; the CR fetch stays on the context
    /// for the refresh phase to reuse.</summary>
    public async Task ReadCurrentAsync(IngestContext context, CancellationToken cancellationToken)
    {
        var scryfall = ProbeScryfallAsync(cancellationToken);
        var cr = ProbeCrAsync(cancellationToken);
        var hashes = HashCuratedFilesAsync(cancellationToken);
        await Task.WhenAll(scryfall, cr, hashes);
        var (fetch, effectiveDate, textSha256) = cr.Result;
        context.CrFetch = fetch;
        context.SourceStamps = new SourceStamps(scryfall.Result, effectiveDate, textSha256, hashes.Result);
    }

    /// <summary>Unchanged means the previous run succeeded and every
    /// component matched. A failed previous run, a missing stored row, or a
    /// null probe result can only produce a full run, so triggering ingest
    /// again after a failure is always the rerun the runner promises.</summary>
    public async Task<bool> IsUnchangedAsync(IngestContext context, CancellationToken cancellationToken)
    {
        if (context.SourceStamps is not { } current)
        {
            return false;
        }

        var previous = await db.IngestRuns
            .Where(r => r.Id < context.RunId)
            .OrderByDescending(r => r.Id)
            .Select(r => (IngestRunStatus?)r.Status)
            .FirstOrDefaultAsync(cancellationToken);
        var stored = await db.CorpusStates.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (previous != IngestRunStatus.Succeeded || stored is null
            || current.ScryfallUpdatedAt is null || stored.ScryfallUpdatedAt != current.ScryfallUpdatedAt
            || current.CrEffectiveDate is null || stored.CrEffectiveDate != current.CrEffectiveDate
            || current.CrTextSha256 is null || stored.CrTextSha256 != current.CrTextSha256)
        {
            return false;
        }

        var storedHashes = Hashes(stored.CuratedFileHashes);
        return storedHashes.Count == current.CuratedFileHashes.Count
            && current.CuratedFileHashes.All(pair =>
                storedHashes.TryGetValue(pair.Key, out var hash) && hash == pair.Value)
            && (await embeddingGate.CheckAsync(cancellationToken)).Enabled;
    }

    /// <summary>The singleton row, created on first write.</summary>
    public static async Task<CorpusState> SingletonAsync(ArchiveDbContext db, CancellationToken cancellationToken)
    {
        var state = await db.CorpusStates.SingleOrDefaultAsync(cancellationToken);
        if (state is null)
        {
            state = new CorpusState { Id = 1 };
            db.CorpusStates.Add(state);
        }

        return state;
    }

    public static void SaveStamps(CorpusState state, SourceStamps stamps)
    {
        state.ScryfallUpdatedAt = stamps.ScryfallUpdatedAt;
        state.CrEffectiveDate = stamps.CrEffectiveDate;
        state.CrTextSha256 = stamps.CrTextSha256;
        state.CuratedFileHashes = JsonSerializer.SerializeToDocument(stamps.CuratedFileHashes, WireJson.Options);
    }

    public static JsonDocument RecordPhase(JsonDocument? current, string phase, int runId, DateTimeOffset finishedAt)
    {
        // PhaseRuns drops the retired phases, so the stored row loses them too.
        var runs = PhaseRuns(current);
        runs[phase] = new CorpusPhaseRun(runId, finishedAt);
        return JsonSerializer.SerializeToDocument(runs, WireJson.Options);
    }

    public static Dictionary<string, CorpusPhaseRun> PhaseRuns(JsonDocument? stored)
    {
        var runs = stored?.Deserialize<Dictionary<string, CorpusPhaseRun>>(WireJson.Options) ?? [];
        foreach (var retired in runs.Keys.Where(k => !IngestPhases.CanonicalOrder.Contains(k)).ToList())
        {
            runs.Remove(retired);
        }

        return runs;
    }

    public static Dictionary<string, string> Hashes(JsonDocument? stored)
        => stored?.Deserialize<Dictionary<string, string>>(WireJson.Options) ?? [];

    private async Task<DateTimeOffset?> ProbeScryfallAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Truncated to whole microseconds so the value survives the
            // timestamptz round trip and still compares equal next run.
            var updatedAt = (await bulkSource.LatestUpdatedAtAsync(cancellationToken)).ToUniversalTime();
            return new DateTimeOffset(updatedAt.Ticks - updatedAt.Ticks % 10, TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Scryfall bulk catalog probe failed; treating the source as changed");
            return null;
        }
    }

    /// <summary>The CR stamp is the header's effective date plus the
    /// normalized text's SHA-256 (the hash the refresh phase archives), so a
    /// same-date republish — a wording correction — reads as changed. The
    /// fetch is handed back either way: a text that fails to parse is the
    /// refresh phase's to fail loudly on.</summary>
    private async Task<(CrFetch? Fetch, DateOnly? EffectiveDate, string? TextSha256)> ProbeCrAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var fetch = await crSource.FetchCurrentAsync(cancellationToken);
            var text = CrChunker.Normalize(fetch.RawText);
            var errors = new List<string>();
            var effectiveDate = CrChunker.ParseEffectiveDate(text, errors);
            if (errors.Count > 0)
            {
                return (fetch, null, null);
            }

            return (fetch, effectiveDate, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "CR probe failed; treating the source as changed");
            return (null, null, null);
        }
    }

    /// <summary>Every curated file is required by its phase, so a successful
    /// run stored all of them; a file that cannot be hashed (missing, or
    /// locked by an editor) is simply absent here, which can only read as
    /// changed.</summary>
    private async Task<Dictionary<string, string>> HashCuratedFilesAsync(CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, path) in CuratedFiles())
        {
            try
            {
                await using var stream = File.OpenRead(path);
                hashes[key] = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Curated file probe failed for {Path}; treating the source as changed", path);
            }
        }

        return hashes;
    }

    /// <summary>The curated files ingest replays, keyed by their identity in
    /// the repo's <c>data/</c> directory — stable across deployments however
    /// the paths are configured.</summary>
    private IEnumerable<(string Key, string Path)> CuratedFiles()
    {
        yield return ("banlist-events.json", configuration["Banlist:EventsPath"] ?? BanlistEventsFile.DefaultPath);
        yield return ("banlist-categories.json", configuration["Banlist:CategoriesPath"] ?? BanlistCategoriesFile.DefaultPath);
        var curatedDir = BracketFiles.ResolveCuratedDir(configuration, environment);
        foreach (var file in BracketFiles.CuratedFiles(
                     BracketFiles.ResolveSpecDir(configuration, environment),
                     BracketFiles.ResolveGameChangersDir(configuration, environment),
                     curatedDir))
        {
            yield return file;
        }

        // Each popularity export joins the curated files, so a new export
        // triggers a normal ingest. A removed export drops its key, which
        // reads as a change too.
        foreach (var file in PopularityExports.Files(PopularityExports.ResolveDir(configuration, environment)))
        {
            yield return file;
        }
    }
}

/// <summary>
/// One reading of the source freshness stamps. Doubles as the wire shape of
/// GET /corpus-state's <c>freshness</c> block, so what the gate compares and
/// what the API reports can never drift apart.
/// </summary>
public sealed record SourceStamps(
    DateTimeOffset? ScryfallUpdatedAt,
    DateOnly? CrEffectiveDate,
    string? CrTextSha256,
    IReadOnlyDictionary<string, string> CuratedFileHashes);

/// <summary>One phase's last successful run — jsonb and wire shape alike.</summary>
public sealed record CorpusPhaseRun(int RunId, DateTimeOffset FinishedAt);
