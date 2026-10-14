namespace MtgArchive.Data;

/// <summary>
/// One phase slot of an ingest run. Every run carries a row per canonical
/// phase (fetch+validate → card upsert → … → legality rebuild → bracket
/// load → re-embed) in <see cref="Position"/> order; a phase
/// with no implementation yet is recorded as skipped, so the canonical order
/// is visible in the run record before the later issues fill the slots.
/// </summary>
public sealed class IngestRunPhase
{
    public int Id { get; set; }

    public int IngestRunId { get; set; }

    /// <summary>Zero-based slot in the canonical phase order.</summary>
    public required int Position { get; set; }

    /// <summary>Canonical phase name, e.g. "fetch_validate".</summary>
    public required string Phase { get; set; }

    public required IngestPhaseStatus Status { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? Error { get; set; }
}

public enum IngestPhaseStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped,
}
