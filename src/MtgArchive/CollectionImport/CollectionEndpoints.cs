using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.CollectionImport;

/// <summary>
/// The collection endpoints. PUT /collection is the full-replace import —
/// parse (ManaBox CSV or plain-text list) → resolve → aggregate → atomic
/// replace — synchronous, on-demand, idempotent, the same delete-then-insert
/// transaction the CR refresh phase uses for rule chunks: recovery from any
/// failure is running it again. GET /collection serves the resolved
/// oracle-level rows plus the last import's metadata.
/// </summary>
public sealed class CollectionEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/collection", async Task<Results<Ok<CollectionImportResult>, ProblemHttpResult>> (
            CollectionImportRequest request,
            ArchiveDbContext db,
            CancellationToken cancellationToken) =>
        {
            // System.Text.Json's required check is presence-only — an
            // explicit null still binds — so empty content is refused here
            // rather than crashing the parser.
            if (string.IsNullOrEmpty(request.Content))
            {
                return TypedResults.Problem(Problems.EmptyCollectionImport([]));
            }

            IReadOnlyList<ImportRow> rows;
            try
            {
                using var reader = new StringReader(request.Content);
                rows = request.Format == CollectionImportFormat.ManaboxCsv
                    ? ManaBoxCsvParser.Parse(reader)
                    : DecklistParser.Parse(reader);
            }
            catch (FormatException ex)
            {
                return TypedResults.Problem(Problems.InvalidImportContent(ex.Message));
            }

            // Bare face names resolve to their full-name card only for
            // plain-text rows — ManaBox names are always the full
            // Front // Back (the resolver's pinned product decision).
            var resolutions = await new CardNameResolver(db).ResolveAsync(
                [.. rows.Select(r => r.Name)],
                faceNameFallback: request.Format == CollectionImportFormat.List,
                cancellationToken);

            var imported = new List<(ImportRow Row, Guid OracleId)>();
            var quarantine = new List<QuarantineEntry>();
            foreach (var row in rows)
            {
                switch (resolutions[row.Name])
                {
                    case { Outcome: NameResolutionOutcome.Resolved, OracleId: { } oracleId }:
                        imported.Add((row, oracleId));
                        break;
                    case { Outcome: NameResolutionOutcome.Ambiguous, CandidateOracleIds: var candidates }:
                        quarantine.Add(new QuarantineEntry(
                            row.LineNumber, row.Name, QuarantineReason.Ambiguous, candidates));
                        break;
                    default:
                        quarantine.Add(new QuarantineEntry(row.LineNumber, row.Name, QuarantineReason.NoMatch));
                        break;
                }
            }

            if (imported.Count == 0)
            {
                return TypedResults.Problem(Problems.EmptyCollectionImport(quarantine));
            }

            var owned = imported
                .GroupBy(i => i.OracleId)
                .Select(g => new CollectionCard { OracleId = g.Key, Quantity = g.Sum(i => i.Row.Quantity) })
                .ToList();

            // The replace is one transaction (ExecuteDelete runs outside
            // SaveChanges), so any failure leaves the prior collection whole.
            // The advisory lock (released with the transaction) queues
            // concurrent imports, so two replaces never interleave into a
            // merged collection or collide on the singleton metadata row.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtext('collection'))", cancellationToken);
            await db.CollectionCards.ExecuteDeleteAsync(cancellationToken);
            await db.CollectionImportRows.ExecuteDeleteAsync(cancellationToken);
            await db.CollectionImportStates.ExecuteDeleteAsync(cancellationToken);
            db.CollectionCards.AddRange(owned);
            db.CollectionImportRows.AddRange(imported.Select(i => new CollectionImportRow
            {
                OracleId = i.OracleId,
                LineNumber = i.Row.LineNumber,
                Name = i.Row.Name,
                Quantity = i.Row.Quantity,
                // Deliberately not WireJson: provenance keeps the source's
                // header names verbatim ("Scryfall ID"), never snake_cased.
                Provenance = JsonSerializer.SerializeToDocument(i.Row.Provenance),
            }));
            db.CollectionImportStates.Add(new CollectionImportState
            {
                Format = request.Format,
                ImportedAt = DateTimeOffset.UtcNow,
                TotalRows = rows.Count,
                ImportedRows = imported.Count,
                QuarantinedRows = quarantine.Count,
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return TypedResults.Ok(new CollectionImportResult(
                TotalRows: rows.Count,
                ImportedRows: imported.Count,
                QuarantinedRows: quarantine.Count,
                DistinctCards: owned.Count,
                TotalQuantity: owned.Sum(c => c.Quantity),
                Quarantine: quarantine));
        });

        app.MapGet("/collection", async Task<Ok<CollectionResponse>> (
            ArchiveDbContext db,
            CancellationToken cancellationToken) =>
        {
            var cards = await CollectionCardRow.Query(db, db.Cards).ToListAsync(cancellationToken);
            var state = await db.CollectionImportStates.SingleOrDefaultAsync(cancellationToken);
            return TypedResults.Ok(new CollectionResponse(
                cards,
                state is null
                    ? null
                    : new CollectionImportMeta(
                        state.Format, state.ImportedAt, state.TotalRows, state.ImportedRows, state.QuarantinedRows)));
        });
    }
}
