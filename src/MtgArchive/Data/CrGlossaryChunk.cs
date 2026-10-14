using Pgvector;

namespace MtgArchive.Data;

/// <summary>
/// One glossary entry of the current CR version, chunked one per term. The
/// re-embed phase fills <see cref="Embedding"/>. A CR change fully
/// replaces every row.
/// </summary>
public sealed class CrGlossaryChunk : IEmbeddedDocument
{
    public int Id { get; set; }

    /// <summary>The term exactly as the glossary lists it — the citation id
    /// <c>GET /rules/{number}</c> resolves.</summary>
    public required string Term { get; set; }

    /// <summary>The CR version this entry belongs to. Stored on the row so
    /// serving text and date is one atomic read.</summary>
    public required DateOnly CrEffectiveDate { get; set; }

    /// <summary>The entry verbatim: the term line plus its definition.</summary>
    public required string Text { get; set; }

    public Vector? Embedding { get; set; }
}
