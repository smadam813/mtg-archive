using MtgArchive.Data;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Exceptions;
using Pgvector;

namespace MtgArchive.Embedding;

/// <summary>
/// The one place embedding happens: every query and document vector in the
/// service comes through here, prefixed per <see cref="EmbeddingModel"/>
/// and sent to Ollama's <c>POST /api/embed</c> through OllamaSharp
/// (Microsoft's own Ollama package is deprecated in its favor). Ollama
/// truncates over-long input silently by default (<c>truncate: true</c>);
/// every request here sends <c>truncate: false</c>, so an over-long input is
/// a refused batch surfaced as <see cref="EmbeddingTruncatedException"/>,
/// never a vector of half a document.
/// </summary>
public sealed class OllamaEmbedder(HttpClient http, EmbeddingModel model)
{
    private readonly OllamaApiClient _ollama = new(http);

    // A narrower model's vectors are zero-padded to the storage width.
    // Zero dimensions change neither dot products nor norms, so cosine
    // distance is exactly the native-width distance; queries come
    // through here too, so both sides pad identically. A wider model is
    // refused here as well as in EmbeddingModel.Resolve, so a directly
    // constructed embedder fails with the same story, not a negative
    // array size.
    private readonly float[] _padding = model.Dimensions <= ArchiveDbContext.EmbeddingDimensions
        ? new float[ArchiveDbContext.EmbeddingDimensions - model.Dimensions]
        : throw new ArgumentOutOfRangeException(nameof(model), model.Dimensions,
            $"{model.Tag} produces vectors wider than the vector({ArchiveDbContext.EmbeddingDimensions}) columns.");

    public EmbeddingModel Model => model;

    public static void Configure(IServiceProvider services, HttpClient client)
    {
        client.BaseAddress = services.GetRequiredService<IConfiguration>().GetValue<Uri>("Embedding:OllamaUrl");
        // A bulk re-embed batch on CPU outlasts the 100s default.
        client.Timeout = TimeSpan.FromMinutes(10);
    }

    /// <summary>
    /// Startup provisioning: pulls the configured tag when Ollama lacks it,
    /// so it is safe on every startup. A present tag is never re-pulled — a
    /// registry republish under the same tag would swap the weights under
    /// stored vectors that the embedding-state record could not tell apart.
    /// Returns whether a pull happened.
    /// </summary>
    public async Task<bool> EnsureModelAsync(CancellationToken cancellationToken)
    {
        // Ollama lists an untagged name as name:latest.
        var listed = model.Tag.Contains(':') ? model.Tag : $"{model.Tag}:latest";
        var local = await _ollama.ListLocalModelsAsync(cancellationToken);
        if (local.Any(m => m.Name == listed))
        {
            return false;
        }

        await foreach (var _ in _ollama.PullModelAsync(new PullModelRequest { Model = model.Tag }, cancellationToken))
        {
        }

        return true;
    }

    /// <summary>Trims the query and unifies CRLF to LF before the template
    /// applies. A stray trailing newline moves a distance by 0.015.
    /// Other interior whitespace stays.</summary>
    public async Task<Vector> EmbedQueryAsync(SearchSurface surface, string query, CancellationToken cancellationToken)
        => (await EmbedAsync([model.QueryText(surface, query.Replace("\r\n", "\n").Trim())], cancellationToken))[0];

    /// <summary>One request per call: Ollama's input array is the batch, and
    /// the caller sizes it.</summary>
    public Task<IReadOnlyList<Vector>> EmbedDocumentsAsync(IReadOnlyList<string> documents, CancellationToken cancellationToken)
        => EmbedAsync([.. documents.Select(model.DocumentText)], cancellationToken);

    private async Task<IReadOnlyList<Vector>> EmbedAsync(List<string> input, CancellationToken cancellationToken)
    {
        EmbedResponse response;
        try
        {
            response = await _ollama.EmbedAsync(
                new EmbedRequest { Model = model.Tag, Input = input, Truncate = false }, cancellationToken);
        }
        catch (OllamaException ex) when (ex.Message.Contains("context length", StringComparison.OrdinalIgnoreCase))
        {
            throw new EmbeddingTruncatedException(model.Tag, input.Count, ex);
        }

        if (response.Embeddings.Count != input.Count)
        {
            throw new InvalidOperationException(
                $"Ollama returned {response.Embeddings.Count} embedding(s) for {input.Count} input(s).");
        }

        return [.. response.Embeddings.Select(e => e.Length == model.Dimensions
            ? new Vector((float[])[.. e, .. _padding])
            : throw new InvalidOperationException(
                $"{model.Tag} returned a {e.Length}-dim embedding; the model is declared at {model.Dimensions} dims."))];
    }
}

/// <summary>
/// At least one input in the batch exceeds the model's context length.
/// Ollama would have truncated it silently; the service refuses the batch
/// instead, and the caller reports it (the ingest report's errors).
/// </summary>
public sealed class EmbeddingTruncatedException(string modelTag, int inputCount, Exception inner)
    : Exception(
        $"Refused to embed a batch of {inputCount} input(s): at least one exceeds {modelTag}'s context length, "
        + "and Ollama would truncate it silently.",
        inner);
