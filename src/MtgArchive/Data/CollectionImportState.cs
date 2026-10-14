namespace MtgArchive.Data;

/// <summary>
/// The one-row record (id = 1, single user) of the last full-replace
/// collection import: format, time, and row counts — the import metadata
/// GET /collection serves. Quarantine details beyond the count live only in
/// the import response; recovery from a lost report is re-importing.
/// </summary>
public sealed class CollectionImportState
{
    public int Id { get; set; } = 1;

    public required CollectionImportFormat Format { get; set; }

    public required DateTimeOffset ImportedAt { get; set; }

    public required int TotalRows { get; set; }

    public required int ImportedRows { get; set; }

    public required int QuarantinedRows { get; set; }
}

/// <summary>The two accepted import formats: a ManaBox CSV export
/// (canonical) and a plain-text decklist-format list.</summary>
public enum CollectionImportFormat
{
    ManaboxCsv,
    List,
}
