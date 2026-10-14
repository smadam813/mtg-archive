using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Banlist;
using MtgArchive.Data;
using Npgsql;
using NpgsqlTypes;

namespace MtgArchive.Ingest;

/// <summary>
/// Regenerates the whole materialized <c>legality</c> table. Commander
/// replays the ban-event log the banlist replay phase just rebuilt: every
/// card × 3 roles × every banlist version, each status from
/// <see cref="LegalityDerivation.Derive"/> with the standing ban at that
/// version from <see cref="BanState.Replay"/>. Each Brawl format reads its
/// own Scryfall key (ADR 0012): every card × 3 roles, one current state with
/// no banlist version and no ban events, each status from
/// <see cref="LegalityDerivation.DeriveBrawl"/>, dated by the Oracle Cards
/// bulk <c>updated_at</c>. Every Brawl row stores its <c>legality</c> citation
/// target: the key and the card's Scryfall URL. The phase rebuilds into the staging
/// twin (streamed over one binary COPY — ~9M narrow rows) and publishes
/// through <see cref="DerivedStateSwap"/>, so the live table only flips
/// between complete states, and only when every upstream phase succeeded — a
/// failed phase aborts the run before this slot executes. Stale cards keep
/// their rows: card detail still serves them, and downstream candidate
/// queries exclude staleness themselves.
/// </summary>
public sealed class LegalityRebuildPhase(ArchiveDbContext db) : IIngestPhase
{
    private static readonly DerivedStateSwap.SwappedTable[] SwappedTables =
    [
        new("legality",
            Constraints:
            [
                "pk_legality", "fk_legality_card", "fk_legality_banlist_version", "fk_legality_reason",
                "ck_legality_format_version",
            ],
            Indexes: ["ix_legality_version_format_role", "ix_legality_reason"]),
    ];

    private static readonly Role[] Roles = [Role.Commander, Role.Companion, Role.Main];

    /// <summary>Each Brawl format and the Scryfall legality key it reads.</summary>
    public static readonly IReadOnlyList<(Format Format, string Key)> BrawlKeys =
    [
        (Format.Brawl, "brawl"),
        (Format.CompetitiveBrawl, "competitivebrawl"),
        (Format.StandardBrawl, "standardbrawl"),
    ];

    public string Name => IngestPhases.LegalityRebuild;

    public async Task RunAsync(IngestContext context, CancellationToken cancellationToken)
    {
        var asOf = context.OracleCardsUpdatedAt
            ?? throw new InvalidOperationException("The fetch phase recorded no Oracle Cards updated_at to date Brawl legality.");
        var versions = await db.BanlistVersions
            .Include(v => v.Events)
            .AsNoTracking()
            .OrderBy(v => v.EffectiveDate)
            .ToListAsync(cancellationToken);
        var cards = await db.Cards
            .Select(c => new
            {
                c.Id,
                c.OracleId,
                c.Name,
                c.CanBeCommander,
                c.CanBeBrawlCommander,
                c.IsMeldResult,
                HasCompanionKeyword = c.Keywords.Contains("Companion"),
                ScryfallCommander = c.Raw.RootElement.GetProperty("legalities").GetProperty("commander").GetString(),
                Brawl = c.Raw.RootElement.GetProperty("legalities").GetProperty("brawl").GetString(),
                CompetitiveBrawl = c.Raw.RootElement.GetProperty("legalities").GetProperty("competitivebrawl").GetString(),
                StandardBrawl = c.Raw.RootElement.GetProperty("legalities").GetProperty("standardbrawl").GetString(),
                ScryfallUri = c.Raw.RootElement.GetProperty("scryfall_uri").GetString(),
            })
            .ToListAsync(cancellationToken);

        // The live table still holds the prior ingest's Brawl rows: the
        // status changes diff against them before the swap replaces them.
        var unversioned = FormatParameter.Materialized.Where(f => !FormatParameter.IsVersioned(f)).ToList();
        var prior = await db.Legalities
            .Where(l => unversioned.Contains(l.Format) && l.Role == Role.Main)
            .Select(l => new { l.CardId, l.Format, l.Status })
            .ToDictionaryAsync(l => (l.CardId, l.Format), l => l.Status, cancellationToken);

        await DerivedStateSwap.TruncateStagingAsync(db, SwappedTables, cancellationToken);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var importer = await connection.BeginBinaryImportAsync(
                "COPY legality_staging (card_id, format, banlist_version_id, role, status, reason, "
                + "legality_as_of, scryfall_key, scryfall_uri) FROM STDIN (FORMAT BINARY)",
                cancellationToken);
            for (var i = 0; i < versions.Count; i++)
            {
                var version = versions[i];
                var standing = BanState.Replay(versions.Take(i + 1)).ToDictionary(e => e.CardId);
                foreach (var card in cards)
                {
                    var ban = standing.GetValueOrDefault(card.Id);
                    foreach (var role in Roles)
                    {
                        var status = LegalityDerivation.Derive(
                            role, card.CanBeCommander, card.HasCompanionKeyword, card.ScryfallCommander, ban?.Scope,
                            card.IsMeldResult);
                        await WriteRowAsync(importer, new(
                            card.Id, Format.Commander, version.Id, role, status,
                            status == LegalityStatus.Banned ? ban!.Id : null,
                            AsOf: null, Key: null, Uri: null), cancellationToken);
                    }
                }
            }

            var tallies = BrawlKeys.ToDictionary(k => k.Format, k => new BrawlTally(k.Format));
            foreach (var card in cards)
            {
                foreach (var (format, key) in BrawlKeys)
                {
                    var scryfallStatus = format switch
                    {
                        Format.Brawl => card.Brawl,
                        Format.CompetitiveBrawl => card.CompetitiveBrawl,
                        _ => card.StandardBrawl,
                    };
                    foreach (var role in Roles)
                    {
                        var status = LegalityDerivation.DeriveBrawl(
                            role, card.CanBeBrawlCommander, card.HasCompanionKeyword, scryfallStatus, card.IsMeldResult);
                        await WriteRowAsync(importer, new(
                            card.Id, format, VersionId: null, role, status, Reason: null,
                            asOf, key, card.ScryfallUri), cancellationToken);
                        if (role == Role.Main)
                        {
                            tallies[format].Count(status);
                            if (prior.TryGetValue((card.Id, format), out var was) && was != status)
                            {
                                tallies[format].Changes.Add(new(card.OracleId, card.Name, was, status));
                            }
                        }
                    }
                }
            }

            await importer.CompleteAsync(cancellationToken);
            context.Report.BrawlLegality.Clear();
            context.Report.BrawlLegality.AddRange(BrawlKeys.Select(k => tallies[k.Format].ToReport(asOf)));
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        await DerivedStateSwap.PublishAsync(db, SwappedTables, cancellationToken);
    }

    /// <summary>One Brawl format's main-role counts and status changes,
    /// gathered as the rows stream.</summary>
    private sealed class BrawlTally(Format format)
    {
        private int _legal;
        private int _banned;
        private int _notLegal;

        public List<BrawlStatusChange> Changes { get; } = [];

        public void Count(LegalityStatus status)
        {
            switch (status)
            {
                case LegalityStatus.Legal: _legal++; break;
                case LegalityStatus.Banned: _banned++; break;
                case LegalityStatus.NotLegal: _notLegal++; break;
            }
        }

        public BrawlLegalityReport ToReport(DateTimeOffset asOf)
            => new(format, asOf, _legal, _banned, _notLegal,
                [.. Changes.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.OracleId)]);
    }

    private readonly record struct Row(
        int CardId,
        Format Format,
        int? VersionId,
        Role Role,
        LegalityStatus Status,
        int? Reason,
        DateTimeOffset? AsOf,
        string? Key,
        string? Uri);

    private static async Task WriteRowAsync(NpgsqlBinaryImporter importer, Row row, CancellationToken cancellationToken)
    {
        await importer.StartRowAsync(cancellationToken);
        await importer.WriteAsync(row.CardId, NpgsqlDbType.Integer, cancellationToken);
        await importer.WriteAsync(ArchiveDbContext.WireName(row.Format), NpgsqlDbType.Text, cancellationToken);
        await WriteNullableAsync(importer, row.VersionId, NpgsqlDbType.Integer, cancellationToken);
        await importer.WriteAsync(ArchiveDbContext.WireName(row.Role), NpgsqlDbType.Text, cancellationToken);
        await importer.WriteAsync(ArchiveDbContext.WireName(row.Status), NpgsqlDbType.Text, cancellationToken);
        await WriteNullableAsync(importer, row.Reason, NpgsqlDbType.Integer, cancellationToken);
        await WriteNullableAsync(importer, row.AsOf, NpgsqlDbType.TimestampTz, cancellationToken);
        await WriteNullableAsync(importer, row.Key, NpgsqlDbType.Text, cancellationToken);
        await WriteNullableAsync(importer, row.Uri, NpgsqlDbType.Text, cancellationToken);
    }

    private static Task WriteNullableAsync<T>(
        NpgsqlBinaryImporter importer, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        => value is null
            ? importer.WriteNullAsync(cancellationToken)
            : importer.WriteAsync(value, type, cancellationToken);
}
