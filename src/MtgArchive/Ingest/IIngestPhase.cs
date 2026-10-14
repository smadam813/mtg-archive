using MtgArchive.Rules;

namespace MtgArchive.Ingest;

/// <summary>
/// One ingest phase. Implementations are scoped services; the runner
/// resolves each canonical slot from a fresh scope, so a phase gets its own
/// <see cref="Data.ArchiveDbContext"/> and commits as it lands. Every phase
/// must be a replay (upsert, event-log replay, staging rebuild, re-chunk):
/// the only recovery path from a failed run is running ingest again.
/// </summary>
public interface IIngestPhase
{
    /// <summary>The canonical slot this implementation fills (<see cref="IngestPhases"/>).</summary>
    string Name { get; }

    Task RunAsync(IngestContext context, CancellationToken cancellationToken);
}

/// <summary>
/// State shared across one run's phases: the trigger's force flag, the
/// validated in-memory snapshot the fetch phase hands to card upsert, the
/// accumulating report, and the temp artifacts the runner deletes after a
/// successful run (a failed run leaves them for the rerun).
/// </summary>
public sealed class IngestContext(int runId, bool force)
{
    public int RunId { get; } = runId;

    public bool Force { get; } = force;

    /// <summary>The freshness stamps the gate read at run start — written to
    /// <c>corpus_state</c> when the run succeeds.</summary>
    public SourceStamps? SourceStamps { get; set; }

    /// <summary>The CR text the gate fetched for its probe, so the refresh
    /// phase need not download it again.</summary>
    public CrFetch? CrFetch { get; set; }

    public OracleSnapshot? Snapshot { get; set; }

    /// <summary>The Oracle Cards bulk <c>updated_at</c> of the snapshot the
    /// fetch phase read — the legality as-of date of every Brawl format.</summary>
    public DateTimeOffset? OracleCardsUpdatedAt { get; set; }

    public IngestReportBuilder Report { get; } = new();

    public List<string> TempArtifacts { get; } = [];
}

/// <summary>
/// A phase failure with itemized errors for the ingest report — the fetch
/// phase throws it when the downloaded bulk data fails validation.
/// </summary>
public sealed class IngestValidationException(IReadOnlyList<string> errors)
    : Exception($"validation failed with {errors.Count} error(s)")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
