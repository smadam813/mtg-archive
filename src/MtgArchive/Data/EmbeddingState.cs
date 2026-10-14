namespace MtgArchive.Data;

/// <summary>
/// The embedding-state singleton (GLOSSARY.md "Embedding state"): the (model
/// tag, dimensions, prefix-set version) the stored vectors were produced
/// under. The re-embed phase writes it on completion; the mismatch gate
/// (<see cref="Embedding.EmbeddingStateGate"/>) reads it, and GET
/// /corpus-state serves it. Row absent = nothing embedded yet.
/// </summary>
public sealed class EmbeddingState
{
    public int Id { get; set; } = 1;

    public required string ModelTag { get; set; }

    public required int Dimensions { get; set; }

    public required string PrefixSetVersion { get; set; }
}
