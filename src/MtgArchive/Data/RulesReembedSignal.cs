namespace MtgArchive.Data;

/// <summary>
/// The recorded CR-change signal (at most one row, id = 1). The CR refresh
/// phase upserts it whenever it replaces the rule and glossary chunks; the
/// re-embed phase (<see cref="Embedding.ReEmbedPhase"/>) consumes and
/// deletes it, re-embedding the rules chunks. Row present = rules chunks
/// await re-embedding.
/// </summary>
public sealed class RulesReembedSignal
{
    public int Id { get; set; } = 1;

    /// <summary>The CR version whose chunks await embedding.</summary>
    public required DateOnly CrEffectiveDate { get; set; }

    public required DateTimeOffset RecordedAt { get; set; }
}
