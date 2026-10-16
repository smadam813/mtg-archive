using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Data;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using Npgsql;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The legality rebuild phase end to end: full materialization
/// (every card × 3 roles × every banlist version) with reasons citing ban
/// events, idempotent re-runs through the staging/atomic-swap publish, the
/// failure semantics (a failed staging build or upstream phase leaves the
/// live table untouched), and per-role legality on card detail.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LegalityRebuildTests(PostgresFixture postgres) : IDisposable
{
    private const string Golos = "Golos, Tireless Pilgrim";
    private const string Braids = "Braids, Cabal Minion";
    private const string Lutri = "Lutri, the Spellchaser";
    private const string SolRing = "Sol Ring";
    private const string SilverBorder = "Silver-Border Jester";

    private static readonly DateOnly V1 = new(2004, 10, 4);
    private static readonly DateOnly V2 = new(2020, 4, 17);
    private static readonly DateOnly V3 = new(2024, 1, 1);

    private readonly Guid _golos = Guid.NewGuid();
    private readonly Guid _braids = Guid.NewGuid();
    private readonly Guid _lutri = Guid.NewGuid();
    private readonly Guid _solRing = Guid.NewGuid();
    private readonly Guid _silverBorder = Guid.NewGuid();
    private readonly string _eventsDir = Directory.CreateTempSubdirectory("mtg-archive-legality-").FullName;

    private string EventsPath => Path.Combine(_eventsDir, "banlist-events.json");

    public void Dispose() => Directory.Delete(_eventsDir, recursive: true);

    [Fact]
    public async Task Rebuild_materializes_every_card_role_version_and_reruns_identically()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        SeedSnapshot(source);

        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        await using var db = PostgresFixture.CreateContext(connectionString);
        // Full materialization: 5 cards × 3 roles × 3 versions in Commander,
        // and 5 cards × 3 roles in each of the four versionless formats:
        // the three Brawl formats and Standard.
        Assert.Equal(105, await db.Legalities.CountAsync());
        Assert.Equal(45, await db.Legalities.CountAsync(l => l.Format == Format.Commander));

        var status = await StatusMapAsync(db);
        var golosBan = await EventIdAsync(db, Golos, V2);
        var braidsBan = await EventIdAsync(db, Braids, V1);
        var lutriBan = await EventIdAsync(db, Lutri, V2);

        // An all-scope ban covers commander and main; the commander-eligible
        // card still fails companion eligibility (judged before the ban).
        Assert.Equal((LegalityStatus.Legal, null), status[(Golos, V1, Role.Commander)]);
        Assert.Equal((LegalityStatus.Banned, golosBan), status[(Golos, V2, Role.Commander)]);
        Assert.Equal((LegalityStatus.Banned, golosBan), status[(Golos, V3, Role.Main)]);
        Assert.Equal((LegalityStatus.NotEligible, null), status[(Golos, V3, Role.Companion)]);

        // A commander_only ban reaches no other role, and the unban lifts it.
        Assert.Equal((LegalityStatus.Banned, braidsBan), status[(Braids, V1, Role.Commander)]);
        Assert.Equal((LegalityStatus.Legal, null), status[(Braids, V1, Role.Main)]);
        Assert.Equal((LegalityStatus.Banned, braidsBan), status[(Braids, V2, Role.Commander)]);
        Assert.Equal((LegalityStatus.Legal, null), status[(Braids, V3, Role.Commander)]);

        // A companion-scope ban bans exactly the companion seat.
        Assert.Equal((LegalityStatus.Legal, null), status[(Lutri, V1, Role.Companion)]);
        Assert.Equal((LegalityStatus.Banned, lutriBan), status[(Lutri, V2, Role.Companion)]);
        Assert.Equal((LegalityStatus.Legal, null), status[(Lutri, V2, Role.Commander)]);
        Assert.Equal((LegalityStatus.Legal, null), status[(Lutri, V2, Role.Main)]);

        // Role requirements without any ban; main has none.
        Assert.Equal((LegalityStatus.NotEligible, null), status[(SolRing, V3, Role.Commander)]);
        Assert.Equal((LegalityStatus.NotEligible, null), status[(SolRing, V3, Role.Companion)]);
        Assert.Equal((LegalityStatus.Legal, null), status[(SolRing, V3, Role.Main)]);

        // Scryfall not_legal wins for every role at every version.
        Assert.All(new[] { V1, V2, V3 }, v => Assert.All(
            new[] { Role.Commander, Role.Companion, Role.Main },
            r => Assert.Equal((LegalityStatus.NotLegal, null), status[(SilverBorder, v, r)])));

        // A re-run (with one card gone stale) rebuilds identical rows —
        // stale cards keep their legality — and the rename-exchange keeps
        // canonical object names on the live table.
        source.OracleCards = SnapshotLines(includeSolRing: false);
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        await using var reread = PostgresFixture.CreateContext(connectionString);
        Assert.True((await reread.Cards.SingleAsync(c => c.OracleId == _solRing)).Stale);
        Assert.Equal(status, await StatusMapAsync(reread));
        await AssertCanonicalNamesAsync(connectionString);
    }

    [Fact]
    public async Task Card_detail_serves_per_role_legality_with_ban_event_citations()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        SeedSnapshot(source);
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        int golosBan, braidsBan;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            golosBan = await EventIdAsync(db, Golos, V2);
            braidsBan = await EventIdAsync(db, Braids, V1);
        }

        // Latest version by default, echoed in the versions block.
        var detail = await GetJsonAsync(client, $"/cards/{_golos}");
        Assert.Equal("2024-01-01", detail.GetProperty("versions").GetProperty("banlist_version").GetString());
        var rows = CommanderRows(detail);
        Assert.Equal(["commander", "companion", "main"],
            rows.Select(r => r.GetProperty("role").GetString()).ToArray());
        Assert.Equal(["banned", "not_eligible", "banned"],
            rows.Select(r => r.GetProperty("status").GetString()).ToArray());
        var citation = rows[0].GetProperty("ban_event");
        Assert.Equal("ban_event", citation.GetProperty("type").GetString());
        Assert.Equal(golosBan.ToString(), citation.GetProperty("id").GetString());
        Assert.Equal($"{Golos} banned 2020-04-17", citation.GetProperty("label").GetString());
        // A non-banned row carries no citation member at all.
        Assert.False(rows[1].TryGetProperty("ban_event", out _));

        // The citation id resolves at its reference endpoint.
        var banEvent = await client.GetAsync($"/ban-events/{citation.GetProperty("id").GetString()}");
        Assert.Equal(HttpStatusCode.OK, banEvent.StatusCode);

        // Pinning a historical version answers with that date's ban state,
        // and a scoped ban labels its seat.
        detail = await GetJsonAsync(client, $"/cards/{_braids}?banlist_version=2004-10-04");
        Assert.Equal("2004-10-04", detail.GetProperty("versions").GetProperty("banlist_version").GetString());
        rows = CommanderRows(detail);
        Assert.Equal("banned", rows[0].GetProperty("status").GetString());
        Assert.Equal($"{Braids} banned as commander 2004-10-04",
            rows[0].GetProperty("ban_event").GetProperty("label").GetString());
        Assert.Equal(braidsBan.ToString(), rows[0].GetProperty("ban_event").GetProperty("id").GetString());
        Assert.Equal("legal", rows[2].GetProperty("status").GetString());

        // The exact-name variant serves the same rows.
        detail = await GetJsonAsync(client, $"/cards?name={Uri.EscapeDataString(Lutri)}");
        rows = CommanderRows(detail);
        Assert.Equal(["legal", "banned", "legal"],
            rows.Select(r => r.GetProperty("status").GetString()).ToArray());

        // A card the rebuild has never seen has no legality rows: the member
        // stays absent while the versions block still echoes the axis.
        var unseen = Guid.NewGuid();
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            db.Cards.Add(TestCards.Minimal(unseen));
            await db.SaveChangesAsync();
        }

        detail = await GetJsonAsync(client, $"/cards/{unseen}");
        Assert.False(detail.TryGetProperty("legality", out _));
        Assert.Equal("2024-01-01", detail.GetProperty("versions").GetProperty("banlist_version").GetString());
    }

    [Fact]
    public async Task A_failed_staging_build_leaves_the_live_legality_untouched()
    {
        var (connectionString, source) = await SetUpAsync();
        SeedSnapshot(source);
        using (var app = NewApp(connectionString, source))
        using (var client = app.CreateClient())
        {
            await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        }

        Dictionary<(string, DateOnly, Role), (LegalityStatus, int?)> before;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            before = await StatusMapAsync(db);
        }

        // The sabotage phase fills the canonical rebuild slot: it builds
        // partial staging rows with the real machinery, then fails before
        // the swap.
        using (var app = NewApp(connectionString, source,
            services => services.AddScoped<IIngestPhase, FailAfterStagingBuildPhase>()))
        using (var client = app.CreateClient())
        {
            // Forced past the corpus-state gate: the sources are unchanged,
            // and the sabotage phase must actually run.
            var failed = await WaitForCompletionAsync(client, await TriggerAsync(client, force: true), "failed");
            var phase = failed.GetProperty("phases").EnumerateArray()
                .Single(p => p.GetProperty("phase").GetString() == "legality_rebuild");
            Assert.Equal("failed", phase.GetProperty("status").GetString());
        }

        await using var reader = PostgresFixture.CreateContext(connectionString);
        // The partial staging build is real — and was never published.
        Assert.Equal(1, await reader.Set<LegalityStaging>().CountAsync());
        Assert.Equal(105, await reader.Legalities.CountAsync());
        Assert.Equal(before, await StatusMapAsync(reader));
    }

    [Fact]
    public async Task The_swap_never_runs_when_an_upstream_phase_failed()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        SeedSnapshot(source);
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        Dictionary<(string, DateOnly, Role), (LegalityStatus, int?)> before;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            before = await StatusMapAsync(db);
        }

        // The events file now names a card the corpus lacks: the replay
        // phase fails, so the rebuild slot is never reached and the live
        // table keeps the previous run's rows.
        WriteEventsFile(extraEvent: """{ "card": "No Such Card", "action": "ban", "scope": "all" }""");
        var failed = await WaitForCompletionAsync(client, await TriggerAsync(client), "failed");
        var phases = failed.GetProperty("phases").EnumerateArray()
            .ToDictionary(p => p.GetProperty("phase").GetString()!, p => p.GetProperty("status").GetString());
        Assert.Equal("failed", phases["banlist_replay"]);
        Assert.Equal("skipped", phases["legality_rebuild"]);

        await using var reader = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(before, await StatusMapAsync(reader));
    }

    /// <summary>Fills the canonical legality_rebuild slot (the runner takes
    /// the last registration) and fails after a partial staging build.</summary>
    private sealed class FailAfterStagingBuildPhase(ArchiveDbContext db) : IIngestPhase
    {
        public string Name => IngestPhases.LegalityRebuild;

        public async Task RunAsync(IngestContext context, CancellationToken cancellationToken)
        {
            await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE legality_staging", cancellationToken);
            db.Set<LegalityStaging>().Add(new LegalityStaging
            {
                CardId = await db.Cards.Select(c => c.Id).FirstAsync(cancellationToken),
                BanlistVersionId = await db.BanlistVersions.Select(v => v.Id).FirstAsync(cancellationToken),
                Role = Role.Commander,
                Status = LegalityStatus.NotLegal,
            });
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("staging build failed after partial rows");
        }
    }

    // ADR 0012: each Brawl format materializes from its own
    // Scryfall key, with no version axis and no ban events. legal or banned
    // is in the pool; not_legal, or a key the record lacks, is outside it.
    // The Brawl commander rule admits a legendary planeswalker. A banned
    // row stores its citation: the key, the card's Scryfall URL, and the
    // bulk updated_at as-of date that dates every Brawl row.
    [Fact]
    public async Task Rebuild_materializes_each_brawl_format_from_its_scryfall_key()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        var walker = Guid.NewGuid();
        source.OracleCards = Lines(
            CardLine(_golos, Golos, typeLine: "Legendary Artifact Creature — Golem",
                brawl: new BrawlKeys("legal", "banned", "not_legal")),
            CardLine(_lutri, Lutri, typeLine: "Legendary Creature — Elemental Otter", keywords: ["Companion"],
                brawl: BrawlKeys.Legal),
            CardLine(walker, "Test Walker", typeLine: "Legendary Planeswalker — Test", brawl: BrawlKeys.Legal),
            CardLine(_braids, Braids, typeLine: "Legendary Creature — Human Minion"),
            CardLine(_solRing, SolRing));
        var asOf = source.UpdatedAt;

        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        await using var db = PostgresFixture.CreateContext(connectionString);
        var rows = await db.Legalities
            .Where(l => l.Format != Format.Commander && l.Format != Format.Standard)
            .Join(db.Cards, l => l.CardId, c => c.Id, (l, c) => new { l, c.Name })
            .ToListAsync();
        // 5 cards × 3 roles × 3 Brawl formats, none with a banlist version.
        Assert.Equal(45, rows.Count);
        Assert.All(rows, r => Assert.Null(r.l.BanlistVersionId));
        Assert.All(rows, r => Assert.Null(r.l.Reason));
        Assert.All(rows, r => Assert.Equal(asOf, r.l.LegalityAsOf));
        var status = rows.ToDictionary(r => (r.Name, r.l.Format, r.l.Role), r => r.l);

        // Banned only in competitivebrawl: banned there, legal in Brawl,
        // outside the Standard Brawl pool.
        Assert.Equal(LegalityStatus.Legal, status[(Golos, Format.Brawl, Role.Commander)].Status);
        Assert.Equal(LegalityStatus.Banned, status[(Golos, Format.CompetitiveBrawl, Role.Commander)].Status);
        Assert.Equal(LegalityStatus.Banned, status[(Golos, Format.CompetitiveBrawl, Role.Main)].Status);
        Assert.Equal(LegalityStatus.NotEligible, status[(Golos, Format.CompetitiveBrawl, Role.Companion)].Status);
        Assert.Equal(LegalityStatus.NotLegal, status[(Golos, Format.StandardBrawl, Role.Main)].Status);

        // Every Brawl row, banned or not, stores its legality citation
        // target: the format's key and the card's Scryfall URL.
        var ban = status[(Golos, Format.CompetitiveBrawl, Role.Main)];
        Assert.Equal("competitivebrawl", ban.ScryfallKey);
        Assert.Equal($"https://scryfall.com/card/tst/1/{_golos}", ban.ScryfallUri);
        var outside = status[(Golos, Format.StandardBrawl, Role.Main)];
        Assert.Equal("standardbrawl", outside.ScryfallKey);
        Assert.Equal($"https://scryfall.com/card/tst/1/{_golos}", outside.ScryfallUri);
        Assert.All(rows, r =>
        {
            Assert.NotNull(r.l.ScryfallKey);
            Assert.NotNull(r.l.ScryfallUri);
        });

        // A legendary planeswalker commands in every Brawl format; in
        // Commander it is not eligible.
        Assert.All(new[] { Format.Brawl, Format.CompetitiveBrawl, Format.StandardBrawl }, f =>
            Assert.Equal(LegalityStatus.Legal, status[("Test Walker", f, Role.Commander)].Status));
        Assert.Equal(LegalityStatus.NotEligible, (await db.Legalities.Join(db.Cards, l => l.CardId, c => c.Id, (l, c) => new { l, c.Name })
            .FirstAsync(x => x.Name == "Test Walker" && x.l.Format == Format.Commander && x.l.Role == Role.Commander)).l.Status);

        // The companion role is open in all three.
        Assert.All(new[] { Format.Brawl, Format.CompetitiveBrawl, Format.StandardBrawl }, f =>
            Assert.Equal(LegalityStatus.Legal, status[(Lutri, f, Role.Companion)].Status));

        // A record with no Brawl keys is outside every Brawl pool.
        Assert.All(rows.Where(r => r.Name is SolRing or Braids), r => Assert.Equal(LegalityStatus.NotLegal, r.l.Status));

        // The re-run publishes through the swap and keeps canonical names.
        await WaitForCompletionAsync(client, await TriggerAsync(client, force: true), "succeeded");
        await using var reread = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(45, await reread.Legalities.CountAsync(l => l.Format != Format.Commander && l.Format != Format.Standard));
        await AssertCanonicalNamesAsync(connectionString);
    }

    // Standard, the 60-card constructed format, materializes from the
    // Scryfall standard key: legal, banned, and not_legal map as for a
    // Brawl format, and a record that lacks the key is outside the pool.
    // Every Standard row stores its legality citation target: the key
    // standard, the card's Scryfall URL, and the as-of date. A Standard
    // deck has no commander or companion seat.
    [Fact]
    public async Task Rebuild_materializes_standard_from_the_standard_key()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = Lines(
            CardLine(_golos, Golos, typeLine: "Legendary Artifact Creature — Golem",
                brawl: new BrawlKeys("legal", "legal", "legal", Standard: "legal")),
            CardLine(_lutri, Lutri, typeLine: "Legendary Creature — Elemental Otter", keywords: ["Companion"],
                brawl: new BrawlKeys("legal", "legal", "legal", Standard: "banned")),
            CardLine(_braids, Braids, typeLine: "Legendary Creature — Human Minion",
                brawl: new BrawlKeys("legal", "legal", "legal", Standard: "not_legal")),
            CardLine(_solRing, SolRing));
        var asOf = source.UpdatedAt;

        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        await using var db = PostgresFixture.CreateContext(connectionString);
        var rows = await db.Legalities
            .Where(l => l.Format == Format.Standard)
            .Join(db.Cards, l => l.CardId, c => c.Id, (l, c) => new { l, c.Name })
            .ToListAsync();
        // 4 cards × 3 roles, none with a banlist version or ban event.
        Assert.Equal(12, rows.Count);
        Assert.All(rows, r => Assert.Null(r.l.BanlistVersionId));
        Assert.All(rows, r => Assert.Null(r.l.Reason));
        var status = rows.ToDictionary(r => (r.Name, r.l.Role), r => r.l);

        Assert.Equal(LegalityStatus.Legal, status[(Golos, Role.Main)].Status);
        Assert.Equal(LegalityStatus.Banned, status[(Lutri, Role.Main)].Status);
        Assert.Equal(LegalityStatus.NotLegal, status[(Braids, Role.Main)].Status);
        Assert.Equal(LegalityStatus.NotLegal, status[(SolRing, Role.Main)].Status);

        // No card commands or accompanies a Standard deck, not even a
        // legendary creature or a Companion-keyword card.
        Assert.All(rows.Where(r => r.l.Role != Role.Main), r => Assert.Equal(LegalityStatus.NotEligible, r.l.Status));

        var main = status[(Golos, Role.Main)];
        Assert.Equal("standard", main.ScryfallKey);
        Assert.Equal($"https://scryfall.com/card/tst/1/{_golos}", main.ScryfallUri);
        Assert.Equal(asOf, main.LegalityAsOf);
        Assert.All(rows, r =>
        {
            Assert.Equal("standard", r.l.ScryfallKey);
            Assert.NotNull(r.l.ScryfallUri);
            Assert.Equal(asOf, r.l.LegalityAsOf);
        });
    }

    // A meld result's own all_parts entry marks it. Scryfall
    // calls it legal, yet no deck can hold it; its meld part stays a
    // commander.
    [Fact]
    public async Task Rebuild_makes_a_meld_result_not_legal_and_keeps_its_meld_part()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = SnapshotLines(includeSolRing: true) + "\n" + File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "ingest", "meld-cards.jsonl"));

        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        await using var db = PostgresFixture.CreateContext(connectionString);
        var status = await StatusMapAsync(db);
        Assert.All(new[] { Role.Commander, Role.Companion, Role.Main }, r => Assert.Equal(
            (LegalityStatus.NotLegal, null), status[("Brisela, Voice of Nightmares", V3, r)]));
        Assert.Equal((LegalityStatus.Legal, null), status[("Bruna, the Fading Light", V3, Role.Commander)]);
        Assert.Equal((LegalityStatus.Legal, null), status[("Bruna, the Fading Light", V3, Role.Main)]);
    }

    // Rows ingested before the is_meld_result column carry false. A forced
    // ingest of the same snapshot rewrites them and the rebuild follows.
    [Fact]
    public async Task A_forced_ingest_flags_a_meld_result_already_in_the_corpus()
    {
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = SnapshotLines(includeSolRing: true) + "\n" + File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "ingest", "meld-cards.jsonl"));
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            await db.Cards.Where(c => c.Name == "Brisela, Voice of Nightmares")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.IsMeldResult, false)
                    .SetProperty(c => c.CanBeCommander, true));
        }

        await WaitForCompletionAsync(client, await TriggerAsync(client, force: true), "succeeded");

        await using var reader = PostgresFixture.CreateContext(connectionString);
        var brisela = await reader.Cards.SingleAsync(c => c.Name == "Brisela, Voice of Nightmares");
        Assert.True(brisela.IsMeldResult);
        Assert.False(brisela.CanBeCommander);
        var status = await StatusMapAsync(reader);
        Assert.Equal((LegalityStatus.NotLegal, null), status[("Brisela, Voice of Nightmares", V3, Role.Commander)]);
    }

    private async Task<(string ConnectionString, FakeBulkSource Source)> SetUpAsync()
    {
        WriteEventsFile();
        return (await postgres.CreateDatabaseAsync($"legality_{Guid.NewGuid():N}"), new FakeBulkSource());
    }

    private ArchiveApp NewApp(
        string connectionString, FakeBulkSource source, Action<IServiceCollection>? extraServices = null)
        => new(
            autoMigrate: true,
            connectionString,
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(source);
                services.RemoveAll<ICrSource>();
                services.AddSingleton<ICrSource>(new FakeCrSource());
                extraServices?.Invoke(services);
            },
            new Dictionary<string, string?> { ["Banlist:EventsPath"] = EventsPath });

    private void SeedSnapshot(FakeBulkSource source) => source.OracleCards = SnapshotLines(includeSolRing: true);

    private string SnapshotLines(bool includeSolRing)
    {
        var lines = new List<string>
        {
            CardLine(_golos, Golos, typeLine: "Legendary Artifact Creature — Golem"),
            CardLine(_braids, Braids, typeLine: "Legendary Creature — Human Minion"),
            CardLine(_lutri, Lutri, typeLine: "Legendary Creature — Elemental Otter", keywords: ["Companion"]),
            CardLine(_silverBorder, SilverBorder, commanderLegality: "not_legal"),
        };
        if (includeSolRing)
        {
            lines.Add(CardLine(_solRing, SolRing));
        }

        return Lines([.. lines]);
    }

    private void WriteEventsFile(string? extraEvent = null) => File.WriteAllText(EventsPath,
        $$"""
        [
          {
            "effective_date": "2004-10-04",
            "date_precision": "day",
            "announcement_url": "https://example.org/2004-banlist",
            "events": [
              { "card": "{{Braids}}", "action": "ban", "scope": "commander_only" }
            ]
          },
          {
            "effective_date": "2020-04-17",
            "date_precision": "day",
            "announcement_url": "https://example.org/2020-banlist",
            "events": [
              { "card": "{{Golos}}", "action": "ban", "scope": "all" },
              { "card": "{{Lutri}}", "action": "ban", "scope": "companion" }{{(extraEvent is null ? "" : "," + extraEvent)}}
            ]
          },
          {
            "effective_date": "2024-01-01",
            "date_precision": "day",
            "announcement_url": "https://example.org/2024-banlist",
            "events": [
              { "card": "{{Braids}}", "action": "unban", "scope": "commander_only" }
            ]
          }
        ]
        """);

    /// <summary>Every live legality row keyed (card, version date, role).</summary>
    private static async Task<Dictionary<(string Card, DateOnly Version, Role Role), (LegalityStatus Status, int? Reason)>>
        StatusMapAsync(ArchiveDbContext db)
        => (await db.Legalities
                .Join(db.Cards, l => l.CardId, c => c.Id, (l, c) => new { l, c.Name })
                .Join(db.BanlistVersions, x => x.l.BanlistVersionId, v => v.Id,
                    (x, v) => new { x.l, x.Name, v.EffectiveDate })
                .ToListAsync())
            .ToDictionary(x => (x.Name, x.EffectiveDate, x.l.Role), x => (x.l.Status, x.l.Reason));

    private static async Task<int> EventIdAsync(ArchiveDbContext db, string card, DateOnly version)
        => (await db.BanEvents
                .Join(db.BanlistVersions, e => e.BanlistVersionId, v => v.Id, (e, v) => new { e, v })
                .SingleAsync(x => x.e.Card.Name == card && x.v.EffectiveDate == version)).e.Id;

    // Card detail lists all four formats; these checks read Commander.
    private static List<JsonElement> CommanderRows(JsonElement detail)
        => [.. detail.GetProperty("legality").EnumerateArray()
            .Where(r => r.GetProperty("format").GetString() == "commander")];

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static async Task AssertCanonicalNamesAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT tablename, indexname FROM pg_indexes
            WHERE tablename LIKE 'legality%' ORDER BY indexname
            """, conn);
        var indexes = new List<(string Table, string Index)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                indexes.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        Assert.Equal(
        [
            ("legality", "ix_legality_reason"),
            ("legality_staging", "ix_legality_reason_staging"),
            ("legality", "ix_legality_version_format_role"),
            ("legality_staging", "ix_legality_version_format_role_staging"),
            ("legality", "pk_legality"),
            ("legality_staging", "pk_legality_staging"),
        ], indexes);

        await using var checks = new NpgsqlCommand(
            """
            SELECT conrelid::regclass::text, conname FROM pg_constraint
            WHERE contype = 'c' AND conrelid::regclass::text LIKE 'legality%' ORDER BY conname
            """, conn);
        var constraints = new List<(string Table, string Constraint)>();
        await using (var reader = await checks.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                constraints.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        Assert.Equal(
        [
            ("legality", "ck_legality_format_version"),
            ("legality_staging", "ck_legality_format_version_staging"),
        ], constraints);
    }
}
