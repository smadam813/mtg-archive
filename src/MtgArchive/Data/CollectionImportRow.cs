using System.Text.Json;

namespace MtgArchive.Data;

/// <summary>
/// One raw import row that resolved to a corpus card, kept so printing data
/// (including the printing-level English Scryfall ID) survives the
/// oracle-level import for a future printing effort. Quarantined rows are
/// import-response content, never stored. Replaced wholesale by each
/// full-replace import.
/// </summary>
public sealed class CollectionImportRow
{
    public int Id { get; set; }

    public required Guid OracleId { get; set; }

    /// <summary>1-based line of the imported file the row started on.</summary>
    public required int LineNumber { get; set; }

    /// <summary>The NFC-normalized name the row resolved by.</summary>
    public required string Name { get; set; }

    public required int Quantity { get; set; }

    /// <summary>Raw source columns, header → verbatim cell value (empty for
    /// plain-text rows, which carry nothing beyond name and quantity).</summary>
    public required JsonDocument Provenance { get; set; }
}
