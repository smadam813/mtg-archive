using System.Text.Json;

namespace MtgArchive.Data;

/// <summary>
/// One ingest run — the single-flight, on-demand background job that
/// refreshes the corpus. The trigger creates the row; phases update it as
/// they commit. <see cref="Report"/> holds the ingest report (snake_case,
/// the wire shape), persisted after each phase so a failed run keeps the
/// counts it measured. Recovery from any failure is running ingest again —
/// there is no resume-from-phase machinery.
/// </summary>
public sealed class IngestRun
{
    public int Id { get; set; }

    public required IngestRunStatus Status { get; set; }

    /// <summary>
    /// The trigger's force flag, plumbed through the run record. It bypasses
    /// the corpus-state no-op short-circuit.
    /// </summary>
    public required bool Force { get; set; }

    /// <summary>
    /// True when the short-circuit found every source unchanged: the run is
    /// recorded (never silently skipped) with all phases skipped.
    /// </summary>
    public bool NoOp { get; set; }

    public required DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>The ingest report as served on the wire (snake_case jsonb).</summary>
    public JsonDocument? Report { get; set; }

    public List<IngestRunPhase> Phases { get; set; } = [];
}

public enum IngestRunStatus
{
    Running,
    Succeeded,
    Failed,
}
