using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.Embedding;

/// <summary>
/// The embedding-state mismatch gate (GLOSSARY.md "Embedding state"): a
/// semantic query runs only when the stored record says the vectors were
/// produced under exactly the configured (model tag, dimensions, prefix-set
/// version). Otherwise it is disabled — report content for the endpoint,
/// not an error and not an automatic re-embed — until an ingest run's
/// re-embed phase rewrites the record. Vector spaces never mix.
/// </summary>
public sealed class EmbeddingStateGate(ArchiveDbContext db, EmbeddingModel model)
{
    public async Task<SemanticQueryStatus> CheckAsync(CancellationToken cancellationToken)
    {
        var stored = await db.EmbeddingStates.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var configured = model.State;
        if (stored is null)
        {
            return new(false,
                $"Nothing has been embedded yet: there is no embedding_state record (configured: {configured}). "
                + "Run ingest to embed.");
        }

        var recorded = CorpusEmbeddingState.Of(stored);
        return recorded == configured
            ? new(true, null)
            : new(false,
                $"Stored vectors were produced under {recorded}; the service is configured for {configured}. "
                + "Semantic queries stay disabled until an ingest run re-embeds.");
    }
}

/// <summary>Whether semantic queries run right now and, if not, why — the
/// report content a search endpoint returns in place of hits.</summary>
public sealed record SemanticQueryStatus(bool Enabled, string? DisabledReason);
