using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Data;
using Npgsql;
using NpgsqlTypes;

namespace MtgArchive.Ingest;

/// <summary>
/// Derives each format's popularity order (ADR 0012).
/// The order is total and 1-based over the format's pool: every live
/// card whose main-role legality is <c>legal</c> or <c>banned</c> (Commander
/// at the latest banlist version, a Brawl format in its one current state).
///
/// A format with a popularity export (Brawl, Competitive Brawl) first loads
/// the export's matched rows into <c>card_popularity</c>. A row matches by
/// name: the oracle name or the front-face name, with <c>" /// "</c> read as
/// <c>" // "</c>, within the format's pool, and never by the Untapped id. A
/// row that matches no card or more than one, or a card whose seat an
/// earlier row filled, does not load; the ingest report lists it. The card
/// order reads the deck row's share, ties by its total games; the commander
/// order reads the commander row's share, then the deck row's share. Both
/// then fall to the EDHREC tiers: <c>edhrec_rank</c>, cards with no rank
/// after every ranked card (an explicit nulls-last, not the Postgres
/// default), then name, then oracle id. A format with no export has no
/// <c>card_popularity</c> rows, so both its orders are the EDHREC proxy.
/// The database sorts, so the name order is the database collation, as on
/// every candidate route.
///
/// Runs after <c>legality_rebuild</c>, which publishes the pools, and
/// rebuilds all three tables in their staging twins before one
/// <see cref="DerivedStateSwap"/>.
/// </summary>
public sealed class PopularityLoadPhase(
    ArchiveDbContext db, IConfiguration configuration, IHostEnvironment environment) : IIngestPhase
{
    private static readonly DerivedStateSwap.SwappedTable[] SwappedTables =
    [
        new("popularity_rank", Constraints: ["pk_popularity_rank", "fk_popularity_rank_card"],
            Indexes: ["ix_popularity_rank_place"]),
        new("popularity_source", Constraints: ["pk_popularity_source"], Indexes: []),
        new("card_popularity", Constraints: ["pk_card_popularity", "fk_card_popularity_card"], Indexes: []),
    ];

    private static readonly Format[] Formats = Enum.GetValues<Format>();

    public string Name => IngestPhases.PopularityLoad;

    public async Task RunAsync(IngestContext context, CancellationToken cancellationToken)
    {
        var asOf = context.OracleCardsUpdatedAt
            ?? throw new InvalidOperationException("The fetch phase recorded no Oracle Cards updated_at to date the popularity order.");
        var errors = new List<string>();
        var exports = PopularityExports.Read(PopularityExports.ResolveDir(configuration, environment), errors);
        if (errors.Count > 0)
        {
            throw new IngestValidationException(errors);
        }

        var latest = await db.BanlistVersions
            .OrderByDescending(v => v.EffectiveDate)
            .Select(v => (int?)v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        await DerivedStateSwap.TruncateStagingAsync(db, SwappedTables, cancellationToken);
        context.Report.SkippedPopularityRows.Clear();
        foreach (var export in exports)
        {
            await StageExportAsync(export, latest, context.Report.SkippedPopularityRows, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        var orders = new List<(Format Format, PopularityOrder Order, List<int> CardIds)>();
        foreach (var format in Formats)
        {
            orders.Add((format, PopularityOrder.Card, await CardOrderAsync(format, latest, cancellationToken)));
            orders.Add((format, PopularityOrder.Commander, await CommanderOrderAsync(format, latest, cancellationToken)));
        }

        await CopyRanksAsync(orders, cancellationToken);
        foreach (var format in Formats)
        {
            var export = exports.FirstOrDefault(e => e.Format == format);
            db.Set<PopularitySourceStaging>().Add(new PopularitySourceStaging
            {
                Format = format,
                Source = export is null ? PopularitySourceKind.Edhrec : PopularitySourceKind.Untapped,
                // The window end is a date; the phase stores it as midnight UTC.
                AsOf = export is null
                    ? asOf
                    : new DateTimeOffset(export.WindowEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await DerivedStateSwap.PublishAsync(db, SwappedTables, cancellationToken);
    }

    /// <summary>Matches the export's rows to the format's pool and adds the
    /// matched rows to the staging table.</summary>
    private async Task StageExportAsync(
        PopularityExport export, int? latest, List<SkippedPopularityRow> skipped, CancellationToken cancellationToken)
    {
        var pool = await Pool(export.Format, latest)
            .Select(c => new
            {
                c.Id,
                c.Name,
                Front = c.Faces.Where(f => f.FaceIndex == 0).Select(f => f.Name).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        var byName = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var card in pool)
        {
            foreach (var name in new[] { card.Name, card.Front })
            {
                if (name is null)
                {
                    continue;
                }

                if (!byName.TryGetValue(name, out var ids))
                {
                    byName[name] = ids = [];
                }

                ids.Add(card.Id);
            }
        }

        var filled = new HashSet<(int CardId, Role Seat)>();
        foreach (var row in export.Rows)
        {
            var matches = byName.GetValueOrDefault(row.Name.Replace(" /// ", " // ", StringComparison.Ordinal));
            SkippedPopularityReason? reason = matches?.Count switch
            {
                null or 0 => SkippedPopularityReason.NoMatch,
                > 1 => SkippedPopularityReason.Ambiguous,
                _ => filled.Add((matches.Single(), row.Seat)) ? null : SkippedPopularityReason.Duplicate,
            };
            if (reason is { } why)
            {
                skipped.Add(new SkippedPopularityRow(export.Format, row.Name, row.Seat, why));
                continue;
            }

            db.Set<CardPopularityStaging>().Add(new CardPopularityStaging
            {
                CardId = matches!.Single(),
                Format = export.Format,
                Seat = row.Seat,
                Share = row.Share,
                TotalGames = row.TotalGames,
            });
        }
    }

    /// <summary>Tier 1: the deck row's share, ties by its total games. Then
    /// the EDHREC tiers.</summary>
    private Task<List<int>> CardOrderAsync(Format format, int? latest, CancellationToken cancellationToken)
        => Ranked(format, latest)
            .OrderBy(c => c.DeckShare == null)
            .ThenByDescending(c => c.DeckShare)
            .ThenBy(c => c.DeckGames == null)
            .ThenByDescending(c => c.DeckGames)
            .ThenBy(c => c.EdhrecRank == null)
            .ThenBy(c => c.EdhrecRank)
            .ThenBy(c => c.Name)
            .ThenBy(c => c.OracleId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

    /// <summary>Tier 1a: the commander row's share. Tier 1b: the deck row's
    /// share. Then the EDHREC tiers.</summary>
    private Task<List<int>> CommanderOrderAsync(Format format, int? latest, CancellationToken cancellationToken)
        => Ranked(format, latest)
            .OrderBy(c => c.CommanderShare == null)
            .ThenByDescending(c => c.CommanderShare)
            .ThenBy(c => c.DeckShare == null)
            .ThenByDescending(c => c.DeckShare)
            .ThenBy(c => c.EdhrecRank == null)
            .ThenBy(c => c.EdhrecRank)
            .ThenBy(c => c.Name)
            .ThenBy(c => c.OracleId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

    /// <summary>The pool with each card's staged export values (null when
    /// the card has no row in that seat).</summary>
    private IQueryable<RankedCard> Ranked(Format format, int? latest)
    {
        var staged = db.Set<CardPopularityStaging>().Where(p => p.Format == format);
        return Pool(format, latest).Select(c => new RankedCard
        {
            Id = c.Id,
            OracleId = c.OracleId,
            Name = c.Name,
            EdhrecRank = c.EdhrecRank,
            DeckShare = staged.Where(p => p.CardId == c.Id && p.Seat == Role.Main).Select(p => p.Share).FirstOrDefault(),
            DeckGames = staged.Where(p => p.CardId == c.Id && p.Seat == Role.Main).Select(p => p.TotalGames).FirstOrDefault(),
            CommanderShare = staged.Where(p => p.CardId == c.Id && p.Seat == Role.Commander).Select(p => p.Share).FirstOrDefault(),
        });
    }

    private sealed class RankedCard
    {
        public int Id { get; init; }

        public Guid OracleId { get; init; }

        public required string Name { get; init; }

        public int? EdhrecRank { get; init; }

        public decimal? DeckShare { get; init; }

        public int? DeckGames { get; init; }

        public decimal? CommanderShare { get; init; }
    }

    /// <summary>The live cards in the format's pool. A card outside it can
    /// never be a candidate in any role or at any banlist version, because
    /// <c>not_legal</c> does not depend on the role or the version.</summary>
    private IQueryable<Card> Pool(Format format, int? latestVersion)
    {
        var versioned = FormatParameter.IsVersioned(format);
        return db.Cards.Where(c => !c.Stale && db.Legalities.Any(l =>
            l.CardId == c.Id
            && l.Format == format
            && (versioned ? l.BanlistVersionId == latestVersion : l.BanlistVersionId == null)
            && l.Role == Role.Main
            && (l.Status == LegalityStatus.Legal || l.Status == LegalityStatus.Banned)));
    }

    private async Task CopyRanksAsync(
        List<(Format Format, PopularityOrder Order, List<int> CardIds)> orders, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var importer = await connection.BeginBinaryImportAsync(
                "COPY popularity_rank_staging (card_id, format, \"order\", rank) FROM STDIN (FORMAT BINARY)",
                cancellationToken);
            foreach (var (format, order, cardIds) in orders)
            {
                for (var i = 0; i < cardIds.Count; i++)
                {
                    await importer.StartRowAsync(cancellationToken);
                    await importer.WriteAsync(cardIds[i], NpgsqlDbType.Integer, cancellationToken);
                    await importer.WriteAsync(ArchiveDbContext.WireName(format), NpgsqlDbType.Text, cancellationToken);
                    await importer.WriteAsync(ArchiveDbContext.WireName(order), NpgsqlDbType.Text, cancellationToken);
                    await importer.WriteAsync(i + 1, NpgsqlDbType.Integer, cancellationToken);
                }
            }

            await importer.CompleteAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
