using Pgvector;

namespace MtgArchive.Data;

/// <summary>
/// One rules-search chunk of the current CR version: a numbered rule
/// (e.g. 601.2) together with its subrules and examples, verbatim. The
/// re-embed phase fills <see cref="Embedding"/>. A CR change fully
/// replaces every row; there is no incremental diffing.
/// </summary>
public sealed class CrRuleChunk : IEmbeddedDocument
{
    public int Id { get; set; }

    /// <summary>The chunk's rule number ("601.2") — the citation id
    /// <c>GET /rules/{number}</c> resolves.</summary>
    public required string Number { get; set; }

    /// <summary>The CR version this chunk belongs to. Stored on the row so
    /// serving text and date is one atomic read.</summary>
    public required DateOnly CrEffectiveDate { get; set; }

    /// <summary>The rule, its subrules, and its examples, verbatim.</summary>
    public required string Text { get; set; }

    public Vector? Embedding { get; set; }
}
