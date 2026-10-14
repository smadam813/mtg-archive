using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.Embedding;

/// <summary>The two semantic retrieval modes (GLOSSARY.md "Search surface"),
/// each with its own fixed query instruction.</summary>
public enum SearchSurface
{
    CardSearch,
    RulesSearch,
}

/// <summary>
/// One provisionable embedding model and its prefix set: the query and
/// document templates the service prepends itself, because Ollama's embed
/// API passes input through verbatim. <see cref="PrefixSetVersion"/> is a
/// content hash of the templates and both instruction strings, so any
/// change to either bumps it by construction, as does a non-default
/// <see cref="Composition"/>. It rides with the tag in the embedding-state
/// record, and a stored record that disagrees closes the semantic-query
/// gate (<see cref="EmbeddingStateGate"/>).
/// </summary>
public sealed record EmbeddingModel(string Tag, int Dimensions, string QueryTemplate, string DocumentTemplate)
{
    /// <summary>How card documents render their oracle text line; the
    /// frozen default is verbatim.</summary>
    public CardDocumentComposition Composition { get; init; } = CardDocumentComposition.Verbatim;

    public const string CardSearchInstruction =
        "Given a description of what a Magic: The Gathering card does, retrieve cards whose rules text does that";

    public const string RulesSearchInstruction =
        "Given a question about the rules of Magic: The Gathering, retrieve the Comprehensive Rules entries "
        + "and glossary terms that answer it";

    /// <summary>Primary. Instruction-aware: a query carries its surface's
    /// instruction; documents are unprefixed.</summary>
    public static readonly EmbeddingModel Qwen3 =
        new("qwen3-embedding:0.6b", 1024, "Instruct: {task}\nQuery: {query}", "{document}");

    /// <summary>Fallback. A task prefix on each side; the instructions do
    /// not render. Its 768-dim output is zero-padded to the storage width
    /// (<see cref="OllamaEmbedder"/>).</summary>
    public static readonly EmbeddingModel Nomic =
        new("nomic-embed-text", 768, "search_query: {query}", "search_document: {document}");

    /// <summary>The provisionable tags; <see cref="Resolve"/> says why the
    /// larger qwen3 variants are excluded.</summary>
    public static readonly ImmutableArray<EmbeddingModel> Known = [Qwen3, Nomic];

    /// <summary>The configured model and composition. An unknown tag, one
    /// whose width the vector columns cannot hold, or an unknown
    /// composition name fails startup. An absent composition is the frozen
    /// verbatim default.</summary>
    public static EmbeddingModel Resolve(string? tag, string? composition = null)
    {
        var model = Known.FirstOrDefault(m => m.Tag == tag)
            ?? throw new InvalidOperationException(
                $"Unknown embedding model tag '{tag}'. Embedding:ModelTag must be one of: "
                + $"{string.Join(", ", Known.Select(m => m.Tag))}. qwen3-embedding:4b and :8b are excluded: "
                + "2560/4096 dims exceed pgvector's 2000-dim HNSW index limit.");
        return model.Dimensions <= ArchiveDbContext.EmbeddingDimensions
            ? model with { Composition = CardDocumentCompositions.Resolve(composition) }
            : throw new InvalidOperationException(
                $"Embedding:ModelTag '{tag}' produces {model.Dimensions}-dim vectors, which exceed the "
                + $"vector({ArchiveDbContext.EmbeddingDimensions}) columns.");
    }

    /// <summary>Hash of the two templates and the two instruction strings.
    /// The instructions count for every model, rendered or not — an
    /// instruction edit under nomic re-embeds needlessly, the price of one
    /// rule with no exceptions. A non-default composition joins the hash.
    /// The verbatim default stays out, so ADR 0007's frozen version holds.</summary>
    public string PrefixSetVersion => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(
        '\0', Hashed))))[..16];

    private IEnumerable<string> Hashed
    {
        get
        {
            yield return QueryTemplate;
            yield return DocumentTemplate;
            yield return CardSearchInstruction;
            yield return RulesSearchInstruction;
            if (Composition != CardDocumentComposition.Verbatim)
            {
                yield return CardDocumentCompositions.NameOf(Composition);
            }
        }
    }

    /// <summary>What vectors embedded under this model are recorded as.</summary>
    public CorpusEmbeddingState State => new(Tag, Dimensions, PrefixSetVersion);

    public string QueryText(SearchSurface surface, string query)
        => QueryTemplate.Replace("{task}", Instruction(surface)).Replace("{query}", query);

    public string DocumentText(string document) => DocumentTemplate.Replace("{document}", document);

    private static string Instruction(SearchSurface surface) => surface switch
    {
        SearchSurface.CardSearch => CardSearchInstruction,
        SearchSurface.RulesSearch => RulesSearchInstruction,
        _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null),
    };
}
