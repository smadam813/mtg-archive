using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;
using MtgArchive.Ingest;
using Pgvector;

namespace MtgArchive.Embedding;

/// <summary>
/// The re-embed phase: the last canonical ingest slot. Embeds every row
/// still lacking a vector — the card documents the upsert phase composed
/// or changed, plus the rule and glossary chunks a CR change fully
/// replaced — through the one embedding client, and records the embedding
/// state on completion. A stored state that disagrees with the configured
/// model nulls every vector first, so a model or prefix change is a full
/// re-embed and vector spaces never mix. A replay like every other phase:
/// "lacks a vector" is the work list, so a run that fails here is
/// recovered by running ingest again. An over-long input is reported with
/// the run, never truncated.
/// </summary>
public sealed class ReEmbedPhase(ArchiveDbContext db, OllamaEmbedder embedder, EmbeddingStateGate gate) : IIngestPhase
{
    /// <summary>Documents per Ollama request.</summary>
    private const int BatchSize = 64;

    public string Name => IngestPhases.ReEmbed;

    public async Task RunAsync(IngestContext context, CancellationToken cancellationToken)
    {
        var stored = await db.EmbeddingStates.SingleOrDefaultAsync(cancellationToken);
        if (stored is not null && !(await gate.CheckAsync(cancellationToken)).Enabled)
        {
            // The record goes first: if this run dies mid-way the gate reads
            // "nothing embedded", never the old state over half-new vectors.
            db.EmbeddingStates.Remove(stored);
            await db.SaveChangesAsync(cancellationToken);
            await ClearVectorsAsync(db.CardFaceDocuments, cancellationToken);
            await ClearVectorsAsync(db.CrRuleChunks, cancellationToken);
            await ClearVectorsAsync(db.CrGlossaryChunks, cancellationToken);
            stored = null;
        }

        await EmbedPendingAsync(db.CardFaceDocuments, d => $"card document '{d.Text[..d.Text.IndexOf('\n')]}'",
            context, cancellationToken);
        await EmbedPendingAsync(db.CrRuleChunks, c => $"rule {c.Number}", context, cancellationToken);
        await EmbedPendingAsync(db.CrGlossaryChunks, g => $"glossary entry '{g.Term}'", context, cancellationToken);

        // The CR refresh phase's signal is consumed: its chunks were embedded above.
        await db.RulesReembedSignals.ExecuteDeleteAsync(cancellationToken);
        if (stored is null)
        {
            db.EmbeddingStates.Add(embedder.Model.State.ToStored());
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static Task<int> ClearVectorsAsync<T>(DbSet<T> documents, CancellationToken cancellationToken)
        where T : class, IEmbeddedDocument
        => documents.ExecuteUpdateAsync(
            s => s.SetProperty(d => EF.Property<Vector?>(d, nameof(IEmbeddedDocument.Embedding)), (Vector?)null),
            cancellationToken);

    /// <summary>
    /// Embeds every row of <paramref name="documents"/> without a vector,
    /// a batch per request, committing each batch. Ollama refuses a whole
    /// batch when one input exceeds the context length, so a refused batch
    /// is retried one input at a time to name the offender in the report;
    /// the offender stays vector-less and is skipped for the rest of the run.
    /// </summary>
    private async Task EmbedPendingAsync<T>(
        DbSet<T> documents, Func<T, string> describe, IngestContext context, CancellationToken cancellationToken)
        where T : class, IEmbeddedDocument
    {
        var refused = new List<int>();
        while (true)
        {
            var batch = await documents
                .Where(d => EF.Property<Vector?>(d, nameof(IEmbeddedDocument.Embedding)) == null
                    && !refused.Contains(EF.Property<int>(d, nameof(IEmbeddedDocument.Id))))
                .OrderBy(d => EF.Property<int>(d, nameof(IEmbeddedDocument.Id)))
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return;
            }

            await EmbedAsync(batch, describe, refused, context, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
    }

    private async Task EmbedAsync<T>(
        List<T> batch, Func<T, string> describe, List<int> refused, IngestContext context, CancellationToken cancellationToken)
        where T : class, IEmbeddedDocument
    {
        try
        {
            var vectors = await embedder.EmbedDocumentsAsync([.. batch.Select(d => d.Text)], cancellationToken);
            foreach (var (document, vector) in batch.Zip(vectors))
            {
                document.Embedding = vector;
            }
        }
        catch (EmbeddingTruncatedException) when (batch.Count > 1)
        {
            foreach (var document in batch)
            {
                await EmbedAsync([document], describe, refused, context, cancellationToken);
            }
        }
        catch (EmbeddingTruncatedException)
        {
            refused.Add(batch[0].Id);
            context.Report.Errors.Add(
                $"re_embed: {describe(batch[0])} exceeds {embedder.Model.Tag}'s context length; not embedded");
        }
    }
}
