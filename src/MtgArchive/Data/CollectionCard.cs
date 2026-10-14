namespace MtgArchive.Data;

/// <summary>
/// One owned functional card. The single user's collection is oracle-level:
/// <c>oracle_id</c> + owned quantity, the quantity summed across every raw
/// import row that resolved to the card (the same card recurs across rows
/// per binder, finish, language, and condition). Rebuilt wholesale by each
/// full-replace import; no dated versions, no user keying.
/// </summary>
public sealed class CollectionCard
{
    public required Guid OracleId { get; set; }

    public required int Quantity { get; set; }
}
