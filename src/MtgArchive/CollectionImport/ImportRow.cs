using System.Collections.ObjectModel;
using System.Text;

namespace MtgArchive.CollectionImport;

/// <summary>
/// One normalized collection-import row — the shared model both the ManaBox
/// CSV parser and the plain-text decklist parser produce, and the unit the
/// name resolver classifies. <see cref="Name"/> is NFC-normalized (the
/// corpus is NFC); everything else the source carried survives verbatim in
/// <see cref="Provenance"/> so printing data outlives the oracle-level
/// import (printings stay deferred).
/// </summary>
public sealed record ImportRow
{
    /// <summary>
    /// Card name, NFC Unicode-normalized for corpus matching. The accessor
    /// normalizes, so every producer — current parsers and future ones —
    /// meets the contract without remembering to.
    /// </summary>
    public required string Name
    {
        get;
        init => field = value.Normalize(NormalizationForm.FormC);
    }

    public required int Quantity { get; init; }

    /// <summary>
    /// 1-based source line the row started on (a quoted CSV field can span
    /// lines), so a quarantine report can point back into the uploaded file
    /// the way ManaBox's own import errors do.
    /// </summary>
    public required int LineNumber { get; init; }

    /// <summary>
    /// Every raw source column, header name → verbatim cell value, including
    /// the un-normalized name. The Scryfall ID lives only here — it is the
    /// English printing-level id and never a resolution key. Empty for
    /// plain-text rows, which carry nothing beyond name and quantity.
    /// </summary>
    public IReadOnlyDictionary<string, string> Provenance { get; init; }
        = ReadOnlyDictionary<string, string>.Empty;
}
