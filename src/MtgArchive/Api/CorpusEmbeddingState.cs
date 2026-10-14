using MtgArchive.Data;

namespace MtgArchive.Api;

/// <summary>
/// The embedding-state record (GLOSSARY.md "Embedding state"): what a set of
/// vectors was produced under. The gate compares the stored one against the
/// configured model's; GET /corpus-state serves the stored one.
/// </summary>
public sealed record CorpusEmbeddingState(string ModelTag, int Dimensions, string PrefixSetVersion)
{
    public static CorpusEmbeddingState Of(EmbeddingState stored)
        => new(stored.ModelTag, stored.Dimensions, stored.PrefixSetVersion);

    /// <summary>The singleton row recording this state (the inverse of <see cref="Of"/>).</summary>
    public EmbeddingState ToStored() => new()
    {
        ModelTag = ModelTag,
        Dimensions = Dimensions,
        PrefixSetVersion = PrefixSetVersion,
    };

    public override string ToString() => $"{ModelTag} ({Dimensions} dims, prefix set {PrefixSetVersion})";
}
