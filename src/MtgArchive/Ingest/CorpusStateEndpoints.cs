using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.Ingest;

/// <summary>
/// GET /corpus-state — the versions block at rest: the latest of every
/// versioned axis the corpus serves, the last successful ingest run per
/// phase, and the source freshness stamps. Snapshot citation ids resolve
/// here. The GC snapshot axis (in <c>versions</c>) is defined in the shape
/// now and stays null until its issue lands ("Bracket classifier, endpoint,
/// and Game Changers drift wiring"); <c>embedding_state</c> is absent until
/// a re-embed records what the stored vectors were produced under.
/// </summary>
public sealed class CorpusStateEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/corpus-state", async Task<Ok<CorpusStateResponse>> (
            ArchiveDbContext db, CancellationToken cancellationToken) =>
        {
            var state = await db.CorpusStates.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            var embedding = await db.EmbeddingStates.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            var banlist = await db.BanlistVersions
                .OrderByDescending(v => v.EffectiveDate)
                .Select(v => (DateOnly?)v.EffectiveDate)
                .FirstOrDefaultAsync(cancellationToken);
            // The live chunk set's date, not the newest archived cr_document:
            // a republished older CR wins back the chunks (CrRefreshPhase),
            // and the corpus at rest is what the chunks serve.
            var cr = await db.CrRuleChunks
                .Select(c => (DateOnly?)c.CrEffectiveDate)
                .FirstOrDefaultAsync(cancellationToken);

            var hashes = CorpusStateGate.Hashes(state?.CuratedFileHashes);
            return TypedResults.Ok(new CorpusStateResponse
            {
                PopularityExports = [.. hashes
                    .Select(pair => PopularityExports.ParseKey(pair.Key, pair.Value))
                    .OfType<PopularityExportStamp>()
                    .OrderBy(e => e.Format)],
                Versions = new VersionsBlock
                {
                    BanlistVersion = banlist?.ToString("O", CultureInfo.InvariantCulture),
                    CrEffectiveDate = cr?.ToString("O", CultureInfo.InvariantCulture),
                },
                EmbeddingState = embedding is null ? null : CorpusEmbeddingState.Of(embedding),
                LastPhaseRuns = CorpusStateGate.PhaseRuns(state?.LastPhaseRuns),
                Freshness = new SourceStamps(
                    state?.ScryfallUpdatedAt,
                    state?.CrEffectiveDate,
                    state?.CrTextSha256,
                    hashes),
            });
        });
    }
}

public sealed record CorpusStateResponse
{
    public required VersionsBlock Versions { get; init; }

    /// <summary>What the stored vectors were produced under; absent until a
    /// re-embed records it.</summary>
    public required CorpusEmbeddingState? EmbeddingState { get; init; }

    /// <summary>Canonical phase name → last successful run; a phase that
    /// never succeeded is absent.</summary>
    public required IReadOnlyDictionary<string, CorpusPhaseRun> LastPhaseRuns { get; init; }

    public required SourceStamps Freshness { get; init; }

    /// <summary>The popularity export of each format that has one, as the
    /// last successful run hashed it: window start, window end, and file
    /// hash. Empty until a run with an export succeeds.</summary>
    public required IReadOnlyList<PopularityExportStamp> PopularityExports { get; init; }
}
