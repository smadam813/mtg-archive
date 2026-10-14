namespace MtgArchive.Ingest;

/// <summary>
/// Downloads the three Scryfall bulk files (Oracle Cards, Rulings, Oracle
/// Tags) and fully validates them before the first DB write. A validation
/// error fails the run with every problem itemized in the report; on
/// success the phase hands the parsed snapshot to card upsert in memory.
/// The downloaded files are temp artifacts: the runner deletes them after
/// a successful run, and a failed run leaves them for the rerun.
/// </summary>
public sealed class FetchValidatePhase(IScryfallBulkSource source, IConfiguration configuration) : IIngestPhase
{
    public string Name => IngestPhases.FetchValidate;

    public async Task RunAsync(IngestContext context, CancellationToken cancellationToken)
    {
        var workDir = configuration["Ingest:WorkDir"]
            ?? Path.Combine(Path.GetTempPath(), "mtg-archive-ingest");
        Directory.CreateDirectory(workDir);

        // The three downloads are independent; a failed batch may leave
        // files behind, which a failed run is allowed to do.
        var paths = await Task.WhenAll(
            source.DownloadAsync(ScryfallBulkType.OracleCards, workDir, cancellationToken),
            source.DownloadAsync(ScryfallBulkType.Rulings, workDir, cancellationToken),
            source.DownloadAsync(ScryfallBulkType.OracleTags, workDir, cancellationToken));
        context.TempArtifacts.AddRange(paths);
        var (cardsPath, rulingsPath, tagsPath) = (paths[0], paths[1], paths[2]);

        var errors = new List<string>();
        var cards = ScryfallSnapshotReader.ReadCards(cardsPath, errors);
        var rulings = ScryfallSnapshotReader.ReadRulings(rulingsPath, errors);
        var tags = ScryfallSnapshotReader.ReadOracleTags(tagsPath, errors, context.Report.Errors);
        if (errors.Count > 0)
        {
            throw new IngestValidationException(errors);
        }

        context.Snapshot = new OracleSnapshot(cards, rulings, tags);
        // Truncated to whole microseconds, the timestamptz precision, so the
        // stored as-of date reads back equal.
        var updatedAt = (await source.UpdatedAtAsync(ScryfallBulkType.OracleCards, cancellationToken)).ToUniversalTime();
        context.OracleCardsUpdatedAt = new DateTimeOffset(updatedAt.Ticks - updatedAt.Ticks % 10, TimeSpan.Zero);
    }
}
