using MtgArchive.Data;

namespace MtgArchive.CollectionImport;

/// <summary>
/// PUT /collection body. Both members are required — System.Text.Json
/// rejects a body missing either, so an absent format can never silently
/// bind to the first enum value.
/// </summary>
public sealed record CollectionImportRequest
{
    public required CollectionImportFormat Format { get; init; }

    public required string Content { get; init; }
}

/// <summary>
/// The import outcome: counts plus the quarantine report, inside the 2xx —
/// resolvable rows import even when others quarantine. Only an import that
/// resolves nothing is refused (<see cref="Api.Problems.EmptyCollectionImport"/>),
/// and even then the quarantine rides along.
/// </summary>
public sealed record CollectionImportResult(
    int TotalRows,
    int ImportedRows,
    int QuarantinedRows,
    int DistinctCards,
    int TotalQuantity,
    IReadOnlyList<QuarantineEntry> Quarantine);

/// <summary>
/// One row that failed to resolve to a corpus card, pointing back into the
/// uploaded file by line number the way ManaBox's own import errors do.
/// </summary>
public sealed record QuarantineEntry(
    int LineNumber,
    string Name,
    QuarantineReason Reason,
    IReadOnlyList<Guid>? CandidateOracleIds = null);

public enum QuarantineReason
{
    /// <summary>No corpus card carries the name (misscans, name drift).</summary>
    NoMatch,

    /// <summary>The name belongs to several oracle identities (mostly token
    /// names); the candidates are listed, never picked from.</summary>
    Ambiguous,
}

/// <summary>GET /collection: every resolved row plus the last import's
/// metadata (omitted until a first import lands).</summary>
public sealed record CollectionResponse(
    IReadOnlyList<CollectionCardRow> Cards,
    CollectionImportMeta? Import);

public sealed record CollectionCardRow(Guid OracleId, string Name, int Quantity)
{
    /// <summary>The owned rows among <paramref name="cards"/>, name-ordered.
    /// Ordered before the record projection: EF cannot sort on a
    /// constructor-projected member.</summary>
    public static IQueryable<CollectionCardRow> Query(ArchiveDbContext db, IQueryable<Card> cards) => db.CollectionCards
        .Join(cards, owned => owned.OracleId, card => card.OracleId,
            (owned, card) => new { card.OracleId, card.Name, owned.Quantity })
        .OrderBy(row => row.Name).ThenBy(row => row.OracleId)
        .Select(row => new CollectionCardRow(row.OracleId, row.Name, row.Quantity));
}

public sealed record CollectionImportMeta(
    CollectionImportFormat Format,
    DateTimeOffset ImportedAt,
    int TotalRows,
    int ImportedRows,
    int QuarantinedRows);
