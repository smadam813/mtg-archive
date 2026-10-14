namespace MtgArchive.Data;

/// <summary>
/// One archived Comprehensive Rules version, keyed by the effective date
/// parsed from the file header ("These rules are effective as of …").
/// Every version's full text lives here — ~1MB × 5–6 per year — because
/// <c>./data</c> is a read-only mount and the service never writes CR text
/// to disk. Rows are append-only archives; only the chunk tables track the
/// current version.
/// </summary>
public sealed class CrDocument
{
    public required DateOnly EffectiveDate { get; set; }

    /// <summary>The full CR text, normalized to LF with the BOM stripped —
    /// the exact text the chunker consumed, so chunks are substrings.</summary>
    public required string FullText { get; set; }

    /// <summary>Hex SHA-256 of <see cref="FullText"/>. The refresh phase
    /// compares hashes for its no-change check, so the ~1MB text never
    /// leaves the database on an unchanged run.</summary>
    public required string FullTextSha256 { get; set; }

    /// <summary>The TXT link scraped from the WotC rules landing page.</summary>
    public required string SourceUrl { get; set; }

    public required DateTimeOffset FetchedAt { get; set; }
}
