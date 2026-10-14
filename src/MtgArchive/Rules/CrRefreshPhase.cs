using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;
using MtgArchive.Ingest;

namespace MtgArchive.Rules;

/// <summary>
/// The CR refresh phase: fetches the current Comprehensive Rules, records
/// the version found in the ingest report every run, archives each new
/// version's full text in <c>cr_document</c> (keyed by the header's
/// effective date), and on change fully replaces the rule and glossary
/// chunk rows — no diffing — while upserting the re-embed signal the M3
/// re-embed phase consumes. An unchanged version writes nothing, keeping
/// the phase a replay.
/// </summary>
public sealed class CrRefreshPhase(ICrSource source, ArchiveDbContext db) : IIngestPhase
{
    public string Name => IngestPhases.CrRefresh;

    public async Task RunAsync(IngestContext context, CancellationToken cancellationToken)
    {
        // The corpus-state gate already fetched the CR for its probe; only a
        // failed probe leaves the download to this phase.
        var fetch = context.CrFetch ?? await source.FetchCurrentAsync(cancellationToken);
        var text = CrChunker.Normalize(fetch.RawText);
        var errors = new List<string>();
        var parse = CrChunker.Parse(text, errors);
        if (errors.Count > 0)
        {
            throw new IngestValidationException(errors);
        }

        context.Report.CrVersionFound = parse.EffectiveDate.ToString("O");

        // The no-change check compares hashes so the ~1MB archived text
        // never leaves the database on the common unchanged run.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var existingHash = await db.CrDocuments
            .Where(d => d.EffectiveDate == parse.EffectiveDate)
            .Select(d => d.FullTextSha256)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingHash == hash)
        {
            return;
        }

        if (existingHash is null)
        {
            db.CrDocuments.Add(new CrDocument
            {
                EffectiveDate = parse.EffectiveDate,
                FullText = text,
                FullTextSha256 = hash,
                SourceUrl = fetch.SourceUrl,
                FetchedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            // Same effective date, different text: WotC republished the
            // file. The archive keeps the current text for the date and the
            // chunks are replaced like any other change.
            var existing = await db.CrDocuments
                .SingleAsync(d => d.EffectiveDate == parse.EffectiveDate, cancellationToken);
            existing.FullText = text;
            existing.FullTextSha256 = hash;
            existing.SourceUrl = fetch.SourceUrl;
            existing.FetchedAt = DateTimeOffset.UtcNow;
        }

        // Full replace inside one transaction with the document and signal
        // writes: ExecuteDelete runs immediately, so without the explicit
        // transaction a failure could publish a half-replaced chunk set.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.CrRuleChunks.ExecuteDeleteAsync(cancellationToken);
        await db.CrGlossaryChunks.ExecuteDeleteAsync(cancellationToken);
        db.CrRuleChunks.AddRange(parse.Rules.Select(r => new CrRuleChunk
        {
            Number = r.Number,
            Text = r.Text,
            CrEffectiveDate = parse.EffectiveDate,
        }));
        db.CrGlossaryChunks.AddRange(parse.Glossary.Select(g => new CrGlossaryChunk
        {
            Term = g.Term,
            Text = g.Text,
            CrEffectiveDate = parse.EffectiveDate,
        }));

        var signal = await db.RulesReembedSignals.SingleOrDefaultAsync(cancellationToken);
        if (signal is null)
        {
            db.RulesReembedSignals.Add(new RulesReembedSignal
            {
                CrEffectiveDate = parse.EffectiveDate,
                RecordedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            signal.CrEffectiveDate = parse.EffectiveDate;
            signal.RecordedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
