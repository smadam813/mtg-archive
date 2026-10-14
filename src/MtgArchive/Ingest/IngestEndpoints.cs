using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.Ingest;

/// <summary>
/// The ingest surface — the only asynchronous surface in the API. POST
/// /ingest triggers a run (202 + run id; a trigger during an active run
/// returns that run's id); the GET routes report per-phase progress while
/// running, then the final ingest report.
/// </summary>
public sealed class IngestEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/ingest", async Task<Accepted<IngestTriggerResponse>> (
            IngestTriggerRequest? request, IngestRunner runner, CancellationToken cancellationToken) =>
        {
            var runId = await runner.TriggerAsync(request?.Force ?? false, cancellationToken);
            return TypedResults.Accepted($"/ingest/runs/{runId}", new IngestTriggerResponse(runId));
        });

        app.MapGet("/ingest/runs/latest", async Task<Results<Ok<IngestRunResponse>, ProblemHttpResult>> (
            ArchiveDbContext db, CancellationToken cancellationToken) =>
        {
            var run = await Runs(db).OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
            return run is null
                ? TypedResults.Problem(Problems.UnknownIngestRun("latest"))
                : TypedResults.Ok(ToResponse(run));
        });

        app.MapGet("/ingest/runs/{id:int}", async Task<Results<Ok<IngestRunResponse>, ProblemHttpResult>> (
            int id, ArchiveDbContext db, CancellationToken cancellationToken) =>
        {
            var run = await Runs(db).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
            return run is null
                ? TypedResults.Problem(Problems.UnknownIngestRun(id.ToString()))
                : TypedResults.Ok(ToResponse(run));
        });
    }

    private static IQueryable<IngestRun> Runs(ArchiveDbContext db)
        => db.IngestRuns.Include(r => r.Phases).AsNoTracking();

    private static IngestRunResponse ToResponse(IngestRun run) => new(
        run.Id,
        run.Status,
        run.Force,
        run.NoOp,
        run.StartedAt,
        run.FinishedAt,
        [.. run.Phases
            .OrderBy(p => p.Position)
            .Select(p => new IngestRunPhaseProgress(p.Phase, p.Status, p.StartedAt, p.FinishedAt, p.Error))],
        // The report persists per phase (so a crash keeps measured counts)
        // but is served only once the run is done: progress while running,
        // then the report.
        run.Status == IngestRunStatus.Running ? null : run.Report);
}

public sealed record IngestTriggerRequest(bool? Force);

public sealed record IngestTriggerResponse(int RunId);

public sealed record IngestRunPhaseProgress(
    string Phase,
    IngestPhaseStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error);

public sealed record IngestRunResponse(
    int RunId,
    IngestRunStatus Status,
    bool Force,
    bool NoOp,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<IngestRunPhaseProgress> Phases,
    JsonDocument? Report);
