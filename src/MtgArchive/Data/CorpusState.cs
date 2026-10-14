using System.Text.Json;

namespace MtgArchive.Data;

/// <summary>
/// The corpus-state singleton (GLOSSARY.md "Corpus state"): the last
/// successful ingest run per phase plus the source freshness stamps the
/// no-op short-circuit compares — Scryfall bulk <c>updated_at</c>, CR
/// effective date and text hash, curated-file hashes. Stamps are written only by a
/// successful full run; per-phase records land as each phase commits, so a
/// failed run still credits the phases that finished.
/// </summary>
public sealed class CorpusState
{
    public int Id { get; set; }

    /// <summary>The newest <c>updated_at</c> across the consumed Scryfall bulk files.</summary>
    public DateTimeOffset? ScryfallUpdatedAt { get; set; }

    /// <summary>The CR header's effective date at the last successful run.</summary>
    public DateOnly? CrEffectiveDate { get; set; }

    /// <summary>SHA-256 of the normalized CR text at the last successful
    /// run — the same hash <c>cr_document</c> archives — so a same-date
    /// republish still reads as changed.</summary>
    public string? CrTextSha256 { get; set; }

    /// <summary>SHA-256 per curated file, keyed by data-relative path
    /// (snake_case jsonb, e.g. <c>{"banlist-events.json": "AB12…"}</c>).</summary>
    public JsonDocument? CuratedFileHashes { get; set; }

    /// <summary>Last successful run per phase (snake_case jsonb: phase name →
    /// <c>{run_id, finished_at}</c>).</summary>
    public JsonDocument? LastPhaseRuns { get; set; }
}
