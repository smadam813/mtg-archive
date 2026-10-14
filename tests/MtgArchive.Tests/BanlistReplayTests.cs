using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Banlist;
using MtgArchive.Data;
using MtgArchive.Ingest;

namespace MtgArchive.Tests;

/// <summary>
/// The banlist replay phase: replaying the fixture events file
/// into derived <c>banlist_version</c> + <c>ban_event</c> rows, idempotent
/// re-runs that keep ids (ban-event citations stay stable), full rebuild
/// when the file changes, and the hard failure on an unresolvable Oracle
/// name — never a warning (ADR 0004: these rows are dated citation).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BanlistReplayTests(PostgresFixture postgres)
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "banlist", "banlist-events.json");

    private static readonly string[] FixtureCards =
        ["Ancestral Recall", "Braids, Cabal Minion", "Sundering Titan", "Lutri, the Spellchaser"];

    [Fact]
    public async Task Replay_builds_version_and_event_rows_and_reruns_identically()
    {
        var connectionString = await NewCorpusAsync(FixtureCards);

        await ReplayAsync(connectionString, FixturePath);
        var first = await SnapshotAsync(connectionString);

        var versions = first.Versions;
        Assert.Equal(3, versions.Count);
        Assert.Equal(new DateOnly(2004, 10, 4), versions[0].EffectiveDate);
        Assert.Equal(DatePrecision.Day, versions[0].DatePrecision);
        // Month-precision announcements land on the 1st with the precision
        // recording the caveat.
        Assert.Equal(new DateOnly(2007, 6, 1), versions[1].EffectiveDate);
        Assert.Equal(DatePrecision.Month, versions[1].DatePrecision);
        Assert.Equal("https://example.org/2026-02-announcement", versions[2].AnnouncementUrl);

        Assert.Equal(
            [("Ancestral Recall", BanAction.Ban, BanScope.All),
             ("Braids, Cabal Minion", BanAction.Ban, BanScope.CommanderOnly)],
            first.Events.Where(e => e.VersionId == versions[0].Id).Select(e => (e.Card, e.Action, e.Scope)));
        Assert.Equal(
            [("Braids, Cabal Minion", BanAction.Unban, BanScope.CommanderOnly),
             ("Sundering Titan", BanAction.Ban, BanScope.All)],
            first.Events.Where(e => e.VersionId == versions[1].Id).Select(e => (e.Card, e.Action, e.Scope)));
        Assert.Equal(
            [("Lutri, the Spellchaser", BanAction.Ban, BanScope.Companion)],
            first.Events.Where(e => e.VersionId == versions[2].Id).Select(e => (e.Card, e.Action, e.Scope)));

        // The columns store the wire literals verbatim, commander_only included.
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(["all", "commander_only", "companion"],
                await DistinctAsync(db, """SELECT DISTINCT scope AS "Value" FROM ban_event"""));
            Assert.Equal(["ban", "unban"],
                await DistinctAsync(db, """SELECT DISTINCT action AS "Value" FROM ban_event"""));
            Assert.Equal(["day", "month"],
                await DistinctAsync(db, """SELECT DISTINCT date_precision AS "Value" FROM banlist_version"""));
        }

        // Idempotent: a re-run rebuilds identical rows — ids included, so
        // ban-event citations never dangle across runs.
        await ReplayAsync(connectionString, FixturePath);
        Assert.Equal(first, await SnapshotAsync(connectionString));
    }

    [Fact]
    public async Task Replay_rebuilds_from_the_file_dropping_versions_it_no_longer_contains()
    {
        var connectionString = await NewCorpusAsync(FixtureCards);
        await ReplayAsync(connectionString, FixturePath);
        var first = await SnapshotAsync(connectionString);

        // The edited file keeps 2004 verbatim, amends 2007's URL, drops 2026.
        var edited = Path.Combine(Path.GetTempPath(), "mtg-archive-tests", $"banlist-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(edited,
            """
            [
              {
                "effective_date": "2004-10-04",
                "date_precision": "day",
                "announcement_url": "https://web.archive.org/web/20041012000000/https://example.org/2004-banlist",
                "events": [
                  { "card": "Ancestral Recall", "action": "ban", "scope": "all" },
                  { "card": "Braids, Cabal Minion", "action": "ban", "scope": "commander_only" }
                ]
              },
              {
                "effective_date": "2007-06",
                "date_precision": "month",
                "announcement_url": "https://example.org/2007-corrected",
                "events": [
                  { "card": "Braids, Cabal Minion", "action": "unban", "scope": "commander_only" },
                  { "card": "Sundering Titan", "action": "ban", "scope": "all" }
                ]
              }
            ]
            """);
        await ReplayAsync(connectionString, edited);

        var second = await SnapshotAsync(connectionString);
        Assert.Equal(2, second.Versions.Count);
        Assert.Equal("https://example.org/2007-corrected", second.Versions[1].AnnouncementUrl);
        // Unchanged history keeps its rows: same version and event ids.
        Assert.Equal(first.Versions.Take(2).Select(v => v.Id), second.Versions.Select(v => v.Id));
        Assert.Equal(
            first.Events.Where(e => e.VersionId != first.Versions[2].Id).Select(e => e.Id),
            second.Events.Select(e => e.Id));
    }

    [Fact]
    public async Task A_transcription_correction_updates_the_event_in_place_keeping_its_id()
    {
        var connectionString = await NewCorpusAsync(FixtureCards);
        await ReplayAsync(connectionString, FixturePath);
        var first = await SnapshotAsync(connectionString);
        var braids2004 = first.Events.Single(e =>
            e.VersionId == first.Versions[0].Id && e.Card == "Braids, Cabal Minion");

        // The corrected file re-scopes Braids' 2004 ban from commander_only
        // to all; the event's identity (announcement + card) is unchanged,
        // so its citation id must survive.
        var corrected = Path.Combine(Path.GetTempPath(), "mtg-archive-tests", $"banlist-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(corrected,
            (await File.ReadAllTextAsync(FixturePath)).Replace(
                """{ "card": "Braids, Cabal Minion", "action": "ban", "scope": "commander_only" }""",
                """{ "card": "Braids, Cabal Minion", "action": "ban", "scope": "all" }"""));
        await ReplayAsync(connectionString, corrected);

        var second = await SnapshotAsync(connectionString);
        var rescoped = second.Events.Single(e =>
            e.VersionId == first.Versions[0].Id && e.Card == "Braids, Cabal Minion");
        Assert.Equal(braids2004.Id, rescoped.Id);
        Assert.Equal(BanScope.All, rescoped.Scope);
        Assert.Equal(first.Events.Select(e => e.Id), second.Events.Select(e => e.Id));
    }

    [Fact]
    public async Task An_unresolvable_oracle_name_fails_with_every_name_itemized()
    {
        // Only two of the four fixture names exist in this corpus.
        var connectionString = await NewCorpusAsync("Ancestral Recall", "Braids, Cabal Minion");

        var failure = await Assert.ThrowsAsync<IngestValidationException>(
            () => ReplayAsync(connectionString, FixturePath));

        Assert.Equal(2, failure.Errors.Count);
        Assert.Contains(failure.Errors, e => e.Contains("no card named 'Sundering Titan'"));
        Assert.Contains(failure.Errors, e => e.Contains("no card named 'Lutri, the Spellchaser'"));

        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(0, await db.BanlistVersions.CountAsync());
        Assert.Equal(0, await db.BanEvents.CountAsync());
    }

    [Fact]
    public async Task A_stale_namesake_loses_to_the_live_card_and_a_lone_stale_row_still_resolves()
    {
        var connectionString = await NewCorpusAsync("Sundering Titan", "Lutri, the Spellchaser");
        Guid liveAncestral;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var stale = Named(Guid.NewGuid(), "Ancestral Recall");
            stale.Stale = true;
            var live = Named(Guid.NewGuid(), "Ancestral Recall");
            liveAncestral = live.OracleId;
            // Braids exists only as a stale row — history still resolves.
            var braids = Named(Guid.NewGuid(), "Braids, Cabal Minion");
            braids.Stale = true;
            db.Cards.AddRange(stale, live, braids);
            await db.SaveChangesAsync();
        }

        await ReplayAsync(connectionString, FixturePath);

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var banned = await db.BanEvents.Include(e => e.Card)
                .Where(e => e.Card.Name == "Ancestral Recall")
                .Select(e => e.Card.OracleId)
                .SingleAsync();
            Assert.Equal(liveAncestral, banned);
            Assert.Equal(2, await db.BanEvents.CountAsync(e => e.Card.Name == "Braids, Cabal Minion"));
        }
    }

    [Fact]
    public async Task A_token_namesake_loses_to_the_banned_card()
    {
        var connectionString = await NewCorpusAsync("Braids, Cabal Minion", "Sundering Titan", "Lutri, the Spellchaser");
        Guid ancestral;
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var real = Named(Guid.NewGuid(), "Ancestral Recall");
            real.Layout = "normal";
            ancestral = real.OracleId;
            var token = Named(Guid.NewGuid(), "Ancestral Recall");
            token.Layout = "token";
            db.Cards.AddRange(real, token);
            await db.SaveChangesAsync();
        }

        await ReplayAsync(connectionString, FixturePath);

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var banned = await db.BanEvents
                .Where(e => e.Card.Name == "Ancestral Recall")
                .Select(e => e.Card.OracleId)
                .SingleAsync();
            Assert.Equal(ancestral, banned);
        }
    }

    [Fact]
    public async Task An_unresolvable_name_fails_the_ingest_run()
    {
        var connectionString = await postgres.CreateDatabaseAsync($"banlist_{Guid.NewGuid():N}");
        using var app = new ArchiveApp(
            autoMigrate: true,
            connectionString,
            // Only the banlist slot is filled, so the run reaches it with an
            // empty corpus and every fixture name unresolvable.
            services =>
            {
                services.RemoveAll<IIngestPhase>();
                services.AddScoped<IIngestPhase, BanlistReplayPhase>();
            },
            new Dictionary<string, string?> { ["Banlist:EventsPath"] = FixturePath });
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/ingest", new { });
        int runId;
        using (var trigger = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            runId = trigger.RootElement.GetProperty("run_id").GetInt32();
        }

        for (var attempt = 0; ; attempt++)
        {
            using var body = JsonDocument.Parse(await client.GetStringAsync($"/ingest/runs/{runId}"));
            var run = body.RootElement;
            if (run.GetProperty("status").GetString() != "running")
            {
                Assert.Equal("failed", run.GetProperty("status").GetString());
                var replay = run.GetProperty("phases").EnumerateArray()
                    .Single(p => p.GetProperty("phase").GetString() == "banlist_replay");
                Assert.Equal("failed", replay.GetProperty("status").GetString());
                Assert.Contains(run.GetProperty("report").GetProperty("errors").EnumerateArray(),
                    e => e.GetString()!.Contains("no card named 'Ancestral Recall'"));
                return;
            }

            Assert.True(attempt < 400, $"Ingest run {runId} did not complete.");
            await Task.Delay(25);
        }
    }

    /// <summary>A fresh migrated database seeded with live cards by name.</summary>
    private async Task<string> NewCorpusAsync(params string[] cardNames)
    {
        var connectionString = await postgres.CreateDatabaseAsync($"banlist_{Guid.NewGuid():N}");
        await using var db = PostgresFixture.CreateContext(connectionString);
        await db.Database.MigrateAsync();
        db.Cards.AddRange(cardNames.Select(name => Named(Guid.NewGuid(), name)));
        await db.SaveChangesAsync();
        return connectionString;
    }

    internal static Card Named(Guid oracleId, string name)
    {
        var card = TestCards.Minimal(oracleId);
        card.Name = name;
        return card;
    }

    /// <summary>Runs the replay phase; with no categories file, an empty one.</summary>
    internal static async Task ReplayAsync(string connectionString, string eventsPath, string? categoriesPath = null)
    {
        if (categoriesPath is null)
        {
            categoriesPath = TempFile();
            await File.WriteAllTextAsync(categoriesPath, "[]");
        }

        await using var db = PostgresFixture.CreateContext(connectionString);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Banlist:EventsPath"] = eventsPath,
                ["Banlist:CategoriesPath"] = categoriesPath,
            })
            .Build();
        await new BanlistReplayPhase(db, configuration, new BanlistVersionAxis())
            .RunAsync(new IngestContext(runId: 0, force: false), CancellationToken.None);
    }

    private sealed record VersionRow(int Id, DateOnly EffectiveDate, DatePrecision DatePrecision, string AnnouncementUrl);

    private sealed record EventRow(int Id, int VersionId, string Card, BanAction Action, BanScope Scope, string? Category);

    private sealed record Snapshot(IReadOnlyList<VersionRow> Versions, IReadOnlyList<EventRow> Events)
    {
        public bool Equals(Snapshot? other)
            => other is not null
                && Versions.SequenceEqual(other.Versions)
                && Events.SequenceEqual(other.Events);

        public override int GetHashCode() => Versions.Count ^ Events.Count;
    }

    private static async Task<Snapshot> SnapshotAsync(string connectionString)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        return new Snapshot(
            await db.BanlistVersions.OrderBy(v => v.EffectiveDate)
                .Select(v => new VersionRow(v.Id, v.EffectiveDate, v.DatePrecision, v.AnnouncementUrl))
                .ToListAsync(),
            await db.BanEvents.OrderBy(e => e.BanlistVersionId).ThenBy(e => e.Card.Name)
                .Select(e => new EventRow(e.Id, e.BanlistVersionId, e.Card.Name, e.Action, e.Scope, e.Category))
                .ToListAsync());
    }

    [Fact]
    public async Task Categories_expand_into_dated_ban_events_and_rerun_identically()
    {
        var connectionString = await NewCorpusAsync([.. FixtureCards, "Jihad"]);
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var favor = Named(Guid.NewGuid(), "Brago's Favor");
            favor.CardTypes = ["Conspiracy"];
            var bird = Named(Guid.NewGuid(), "Jeweled Bird");
            bird.OracleText = "Remove Jeweled Bird from your deck before playing if you're not playing for Ante.";
            // The word sits on a face, not on the card.
            var faced = Named(Guid.NewGuid(), "Wager // Win");
            faced.Faces = [new CardFace { FaceIndex = 0, Name = "Wager", TypeLine = "Sorcery", OracleText = "Put a card into the ante." }];
            // "Anteater" holds the letters but not the whole word.
            var anteater = Named(Guid.NewGuid(), "Anteater Herd");
            anteater.OracleText = "Anteaters are antelopes' friends.";
            db.Cards.AddRange(favor, bird, faced, anteater);
            await db.SaveChangesAsync();
        }

        var categories = TempFile();
        await File.WriteAllTextAsync(categories,
            """
            [
              {
                "category": "ante",
                "list_wording": "Cards involving ante",
                "effective_date": "2004-10-04",
                "date_precision": "day",
                "announcement_url": "https://web.archive.org/web/20041012000000/https://example.org/2004-banlist",
                "members": { "oracle_word": "ante" }
              },
              {
                "category": "conspiracy",
                "list_wording": "Conspiracy cards",
                "effective_date": "2020-05-18",
                "date_precision": "day",
                "announcement_url": "https://example.org/2020-05-categories",
                "members": { "card_type": "Conspiracy" }
              },
              {
                "category": "racist_depictions",
                "list_wording": "Cards removed for racist depictions",
                "effective_date": "2020-06-10",
                "date_precision": "day",
                "announcement_url": "https://example.org/2020-06-removals",
                "members": { "cards": ["Jihad"] }
              }
            ]
            """);

        await ReplayAsync(connectionString, FixturePath, categories);
        var first = await SnapshotAsync(connectionString);

        // A category on a date the events file holds joins that version;
        // the others open their own.
        Assert.Equal(
            [new DateOnly(2004, 10, 4), new DateOnly(2007, 6, 1), new DateOnly(2020, 5, 18),
             new DateOnly(2020, 6, 10), new DateOnly(2026, 2, 2)],
            first.Versions.Select(v => v.EffectiveDate));
        Assert.Equal("https://example.org/2020-05-categories", first.Versions[2].AnnouncementUrl);
        Assert.Equal(
            [("Ancestral Recall", BanAction.Ban, BanScope.All, (string?)null),
             ("Braids, Cabal Minion", BanAction.Ban, BanScope.CommanderOnly, null),
             ("Jeweled Bird", BanAction.Ban, BanScope.All, "ante"),
             ("Wager // Win", BanAction.Ban, BanScope.All, "ante")],
            first.Events.Where(e => e.VersionId == first.Versions[0].Id).Select(e => (e.Card, e.Action, e.Scope, e.Category)));
        Assert.Equal(
            [("Brago's Favor", "conspiracy")],
            first.Events.Where(e => e.VersionId == first.Versions[2].Id).Select(e => (e.Card, e.Category)));
        Assert.Equal(
            [("Jihad", "racist_depictions")],
            first.Events.Where(e => e.VersionId == first.Versions[3].Id).Select(e => (e.Card, e.Category)));
        Assert.DoesNotContain(first.Events, e => e.Card == "Anteater Herd");

        // Idempotent, ids included.
        await ReplayAsync(connectionString, FixturePath, categories);
        Assert.Equal(first, await SnapshotAsync(connectionString));
    }

    [Fact]
    public async Task A_category_that_collides_or_matches_nothing_fails_with_every_problem_itemized()
    {
        var connectionString = await NewCorpusAsync(FixtureCards);
        var categories = TempFile();
        await File.WriteAllTextAsync(categories,
            """
            [
              {
                "category": "other_announcement",
                "list_wording": "A category dated on another announcement's day",
                "effective_date": "2007-06",
                "date_precision": "month",
                "announcement_url": "https://example.org/not-the-2007-list",
                "members": { "cards": ["Sundering Titan"] }
              },
              {
                "category": "named_twice",
                "list_wording": "A category naming a card its date already changes",
                "effective_date": "2004-10-04",
                "date_precision": "day",
                "announcement_url": "https://web.archive.org/web/20041012000000/https://example.org/2004-banlist",
                "members": { "cards": ["Ancestral Recall"] }
              },
              {
                "category": "schemes",
                "list_wording": "Scheme cards",
                "effective_date": "2021-01-01",
                "date_precision": "day",
                "announcement_url": "https://example.org/2021",
                "members": { "card_type": "Scheme" }
              }
            ]
            """);

        var failure = await Assert.ThrowsAsync<IngestValidationException>(
            () => ReplayAsync(connectionString, FixturePath, categories));

        Assert.Equal(3, failure.Errors.Count);
        Assert.Contains(failure.Errors, e => e.Contains("'other_announcement'") && e.Contains("one announcement per date"));
        Assert.Contains(failure.Errors, e => e.Contains("'named_twice'") && e.Contains("'Ancestral Recall' already changes"));
        Assert.Contains(failure.Errors, e => e.Contains("'schemes'") && e.Contains("matches no card"));

        // A listed name resolves like an events-file name: exactly, or the run fails.
        await File.WriteAllTextAsync(categories,
            """
            [
              {
                "category": "racist_depictions",
                "list_wording": "Cards removed for racist depictions",
                "effective_date": "2020-06-10",
                "date_precision": "day",
                "announcement_url": "https://example.org/2020-06-removals",
                "members": { "cards": ["Jihad"] }
              }
            ]
            """);
        failure = await Assert.ThrowsAsync<IngestValidationException>(
            () => ReplayAsync(connectionString, FixturePath, categories));
        Assert.Equal(["banlist-categories: no card named 'Jihad' — the reference is exact Oracle name"], failure.Errors);

        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(0, await db.BanlistVersions.CountAsync());
    }

    private static string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "mtg-archive-tests", $"banlist-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static async Task<string[]> DistinctAsync(ArchiveDbContext db, string sql)
        => [.. (await db.Database.SqlQueryRaw<string>(sql).ToListAsync()).Order(StringComparer.Ordinal)];
}
