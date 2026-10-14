using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;

namespace MtgArchive.Ingest;

/// <summary>
/// The single-flight, in-service background job behind POST /ingest — no
/// container, no scheduler. A trigger while a run is active returns the
/// running run's id (never an error, never a queue). Each canonical phase
/// runs in its own DI scope and commits as it lands; a slot with no
/// registered implementation completes as skipped. Before the first phase
/// the corpus-state gate compares source freshness: unchanged sources after
/// a successful previous run make the run a recorded no-op unless the
/// trigger forced. The report persists after every phase, so a failed run
/// keeps the counts it measured; the only recovery path is triggering
/// ingest again. The runner writes the final status under
/// <c>_singleFlight</c>, so a trigger after the row reads final starts a
/// new run.
/// </summary>
public sealed class IngestRunner(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<IngestRunner> logger)
{
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private int? _activeRunId;

    /// <summary>Starts a run, or returns the active run's id unchanged.</summary>
    public async Task<int> TriggerAsync(bool force, CancellationToken cancellationToken)
    {
        await _singleFlight.WaitAsync(cancellationToken);
        try
        {
            if (_activeRunId is { } active)
            {
                return active;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
            await FailInterruptedRunsAsync(db, cancellationToken);

            var run = new IngestRun
            {
                Status = IngestRunStatus.Running,
                Force = force,
                StartedAt = DateTimeOffset.UtcNow,
                Phases = [.. IngestPhases.CanonicalOrder.Select((phase, position) => new IngestRunPhase
                {
                    Position = position,
                    Phase = phase,
                    Status = IngestPhaseStatus.Pending,
                })],
            };
            db.IngestRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);

            _activeRunId = run.Id;
            _ = Task.Run(() => ExecuteAsync(run.Id, force), CancellationToken.None);
            return run.Id;
        }
        finally
        {
            _singleFlight.Release();
        }
    }

    /// <summary>
    /// A run row still "running" with no in-process task is a leftover from
    /// a crashed or restarted service (single instance — nothing else can be
    /// running it). Recovery is rerun, so it fails here, at the next trigger.
    /// </summary>
    private static async Task FailInterruptedRunsAsync(ArchiveDbContext db, CancellationToken cancellationToken)
    {
        var interrupted = await db.IngestRuns
            .Include(r => r.Phases)
            .Where(r => r.Status == IngestRunStatus.Running)
            .ToListAsync(cancellationToken);
        foreach (var run in interrupted)
        {
            run.Status = IngestRunStatus.Failed;
            run.FinishedAt = DateTimeOffset.UtcNow;
            foreach (var phase in run.Phases)
            {
                if (phase.Status == IngestPhaseStatus.Running)
                {
                    phase.Status = IngestPhaseStatus.Failed;
                    phase.FinishedAt = DateTimeOffset.UtcNow;
                    phase.Error = "interrupted: the service restarted mid-run";
                }
                else if (phase.Status == IngestPhaseStatus.Pending)
                {
                    phase.Status = IngestPhaseStatus.Skipped;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ExecuteAsync(int runId, bool force)
    {
        var context = new IngestContext(runId, force);
        IngestRunStatus status;
        var noOp = false;
        try
        {
            (status, noOp) = await RunPhasesAsync(context, lifetime.ApplicationStopping);
        }
        catch (Exception ex)
        {
            // Failure of the framework itself (a status write, shutdown
            // cancellation): the run fails with the error recorded.
            logger.LogError(ex, "Ingest run {RunId} aborted outside a phase", runId);
            context.Report.Errors.Add(ex.Message);
            status = IngestRunStatus.Failed;
        }

        await FinishAsync(context, status, noOp);
    }

    /// <summary>Runs the corpus-state gate and the phases. Returns the
    /// final status without writing it.</summary>
    private async Task<(IngestRunStatus Status, bool NoOp)> RunPhasesAsync(
        IngestContext context, CancellationToken cancellationToken)
    {
        var runId = context.RunId;

        // The corpus-state gate: unchanged sources short-circuit to a
        // recorded no-op run (all phases skipped, never silently
        // dropped); force bypasses the comparison but the stamps are
        // still gathered for the post-run write.
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var gate = scope.ServiceProvider.GetRequiredService<CorpusStateGate>();
            await gate.ReadCurrentAsync(context, cancellationToken);
            if (!context.Force && await gate.IsUnchangedAsync(context, cancellationToken))
            {
                // The gate parsed the current CR to compare it, so the
                // report's "CR version found" stays recorded every run.
                context.Report.CrVersionFound = context.SourceStamps?.CrEffectiveDate?.ToString("O");
                return (IngestRunStatus.Succeeded, true);
            }
        }

        foreach (var phaseName in IngestPhases.CanonicalOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopeFactory.CreateAsyncScope();
            var phase = scope.ServiceProvider.GetServices<IIngestPhase>()
                .LastOrDefault(p => p.Name == phaseName);
            if (phase is null)
            {
                await UpdatePhaseAsync(runId, phaseName, p => p.Status = IngestPhaseStatus.Skipped);
                continue;
            }

            await UpdatePhaseAsync(runId, phaseName, p =>
            {
                p.Status = IngestPhaseStatus.Running;
                p.StartedAt = DateTimeOffset.UtcNow;
            });
            try
            {
                await phase.RunAsync(context, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ingest run {RunId} failed in phase {Phase}", runId, phaseName);
                context.Report.Errors.AddRange(Describe(ex));
                await UpdatePhaseAsync(runId, phaseName, p =>
                {
                    p.Status = IngestPhaseStatus.Failed;
                    p.FinishedAt = DateTimeOffset.UtcNow;
                    p.Error = ex.Message;
                });
                return (IngestRunStatus.Failed, false);
            }

            await UpdatePhaseAsync(runId, phaseName, p =>
            {
                p.Status = IngestPhaseStatus.Succeeded;
                p.FinishedAt = DateTimeOffset.UtcNow;
            }, recordLastSuccess: true);
            await PersistReportAsync(context);
        }

        // Cleanup precedes the status flip so "succeeded" always means
        // the temp artifacts are gone; a failed run leaves them for the
        // rerun.
        DeleteTempArtifacts(context);
        return (IngestRunStatus.Succeeded, false);
    }

    /// <summary>
    /// Writes the final status and goes idle under <c>_singleFlight</c>.
    /// A trigger that arrives after the row reads final waits for the idle
    /// transition and starts a new run. A trigger that arrives before it
    /// gets this run's id. The wait is one status write, and the request's
    /// cancellation token can cut it short. If the write fails, a second
    /// write marks the run failed and records the error. If that fails too,
    /// the row stays running until the next trigger fails it as interrupted.
    /// </summary>
    private async Task FinishAsync(IngestContext context, IngestRunStatus status, bool noOp)
    {
        await _singleFlight.WaitAsync(CancellationToken.None);
        try
        {
            await CompleteAsync(context, status, noOp);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ingest run {RunId} could not be marked {Status}", context.RunId, status);
            try
            {
                context.Report.Errors.Add(ex.Message);
                await CompleteAsync(context, IngestRunStatus.Failed, noOp: false);
            }
            catch (Exception completion)
            {
                logger.LogError(completion, "Ingest run {RunId} could not be marked failed", context.RunId);
            }
        }
        finally
        {
            _activeRunId = null;
            _singleFlight.Release();
        }
    }

    /// <summary>Itemized validation errors all reach the report, capped loudly.</summary>
    private static IEnumerable<string> Describe(Exception exception)
    {
        if (exception is not IngestValidationException validation)
        {
            return [exception.Message];
        }

        const int cap = 100;
        return validation.Errors.Count <= cap
            ? validation.Errors
            : validation.Errors.Take(cap).Append($"… and {validation.Errors.Count - cap} more error(s)");
    }

    private async Task UpdatePhaseAsync(
        int runId, string phaseName, Action<IngestRunPhase> update, bool recordLastSuccess = false)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
        var phase = await db.Set<IngestRunPhase>()
            .SingleAsync(p => p.IngestRunId == runId && p.Phase == phaseName);
        update(phase);
        if (recordLastSuccess)
        {
            // Per-phase success commits with the status flip: a later-failing
            // run still credits the phases whose work landed.
            var state = await CorpusStateGate.SingletonAsync(db, CancellationToken.None);
            state.LastPhaseRuns = CorpusStateGate.RecordPhase(
                state.LastPhaseRuns, phaseName, runId, phase.FinishedAt!.Value);
        }

        await db.SaveChangesAsync();
    }

    private async Task PersistReportAsync(IngestContext context)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
        var run = await db.IngestRuns.SingleAsync(r => r.Id == context.RunId);
        run.Report = context.Report.Build().ToDocument();
        await db.SaveChangesAsync();
    }

    private async Task CompleteAsync(IngestContext context, IngestRunStatus status, bool noOp)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
        var run = await db.IngestRuns
            .Include(r => r.Phases)
            .SingleAsync(r => r.Id == context.RunId);
        run.Status = status;
        run.NoOp = noOp;
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.Report = context.Report.Build().ToDocument();
        if (status == IngestRunStatus.Succeeded && !noOp && context.SourceStamps is { } stamps)
        {
            // The stamps read at run start describe the sources this run
            // ingested; a source updating mid-run then reads as changed next
            // time, which errs toward a full run.
            CorpusStateGate.SaveStamps(await CorpusStateGate.SingletonAsync(db, CancellationToken.None), stamps);
        }

        foreach (var phase in run.Phases)
        {
            if (phase.Status == IngestPhaseStatus.Pending)
            {
                phase.Status = IngestPhaseStatus.Skipped;
            }
            else if (phase.Status == IngestPhaseStatus.Running)
            {
                // A phase can be caught mid-flight when the framework itself
                // fails (e.g. a status write threw after RunAsync returned);
                // a failed run must not leave it reading as running forever.
                phase.Status = IngestPhaseStatus.Failed;
                phase.FinishedAt = DateTimeOffset.UtcNow;
                phase.Error ??= "the run aborted before this phase's status was recorded";
            }
        }

        await db.SaveChangesAsync();
    }

    private void DeleteTempArtifacts(IngestContext context)
    {
        foreach (var path in context.TempArtifacts)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover temp file must never fail an otherwise
                // successful run.
                logger.LogWarning(ex, "Could not delete ingest temp artifact {Path}", path);
            }
        }
    }
}
