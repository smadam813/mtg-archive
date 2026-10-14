namespace MtgArchive.Ingest;

/// <summary>
/// Where the fetch phase gets Scryfall bulk data. The one implementation
/// downloads from api.scryfall.com; tests substitute fixture files through
/// this seam.
/// </summary>
public interface IScryfallBulkSource
{
    /// <summary>
    /// Downloads one bulk file into <paramref name="workDir"/> and returns
    /// the path of the decompressed JSONL file.
    /// </summary>
    Task<string> DownloadAsync(ScryfallBulkType type, string workDir, CancellationToken cancellationToken);

    /// <summary>
    /// The newest <c>updated_at</c> across the consumed bulk files — the
    /// Scryfall freshness stamp the corpus-state gate compares, read from
    /// the /bulk-data catalog without downloading anything.
    /// </summary>
    Task<DateTimeOffset> LatestUpdatedAtAsync(CancellationToken cancellationToken);

    /// <summary>
    /// One bulk file's <c>updated_at</c> from the /bulk-data catalog. The
    /// Oracle Cards stamp dates a Brawl format's legality (GLOSSARY.md
    /// "Legality as-of").
    /// </summary>
    Task<DateTimeOffset> UpdatedAtAsync(ScryfallBulkType type, CancellationToken cancellationToken);
}

public enum ScryfallBulkType
{
    OracleCards,
    Rulings,
    OracleTags,
}
