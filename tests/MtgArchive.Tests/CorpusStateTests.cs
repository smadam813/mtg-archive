using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// Corpus state, the no-op short-circuit, and banlist drift detection
/// through the ingest surface: unchanged stamps record a no-op
/// run, force and any source change (Scryfall republish, curated-file edit)
/// produce a full run, unexplained <c>legalities.commander</c> flips land in
/// the report's drift detections without touching derived legality, and
/// GET /corpus-state serves the versions block at rest.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CorpusStateTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Unchanged_sources_short_circuit_to_a_recorded_no_op_run()
    {
        var (connectionString, source, eventsPath) = await SetUpAsync();
        using var app = NewApp(connectionString, source, eventsPath);
        using var client = app.CreateClient();
        var relic = Guid.NewGuid();
        source.OracleCards = Lines(CardLine(relic, "Quiet Relic"));

        var run1 = await TriggerAsync(client);
        var first = await WaitForCompletionAsync(client, run1, "succeeded");
        Assert.False(first.GetProperty("no_op").GetBoolean());

        // Unchanged stamps: the run is recorded — its own id, every phase
        // skipped, a served report — never silently skipped.
        var run2 = await TriggerAsync(client);
        Assert.NotEqual(run1, run2);
        var noOp = await WaitForCompletionAsync(client, run2, "succeeded");
        Assert.True(noOp.GetProperty("no_op").GetBoolean());
        Assert.All(noOp.GetProperty("phases").EnumerateArray(),
            p => Assert.Equal("skipped", p.GetProperty("status").GetString()));
        Assert.Equal(0, noOp.GetProperty("report").GetProperty("new_cards").GetInt32());
        // The gate parsed the current CR to compare it, so "CR version
        // found" stays recorded on the no-op run.
        Assert.Equal("2026-08-07", noOp.GetProperty("report").GetProperty("cr_version_found").GetString());

        // force overrides the short-circuit with the same unchanged stamps.
        var run3 = await TriggerAsync(client, force: true);
        var forced = await WaitForCompletionAsync(client, run3, "succeeded");
        Assert.False(forced.GetProperty("no_op").GetBoolean());
        Assert.Equal("succeeded", Phase(forced, "card_upsert").GetProperty("status").GetString());

        // A Scryfall republish (the fixture setter bumps updated_at) is a
        // changed source: full run, no force needed.
        source.OracleCards = Lines(CardLine(relic, "Quiet Relic"));
        var run4 = await TriggerAsync(client);
        var republished = await WaitForCompletionAsync(client, run4, "succeeded");
        Assert.False(republished.GetProperty("no_op").GetBoolean());
    }

    [Fact]
    public async Task An_edited_curated_file_is_a_changed_source_needing_no_force()
    {
        var (connectionString, source, eventsPath) = await SetUpAsync();
        using var app = NewApp(connectionString, source, eventsPath);
        using var client = app.CreateClient();
        source.OracleCards = Lines(CardLine(Guid.NewGuid(), "Sudden Menace"));

        var run1 = await TriggerAsync(client);
        await WaitForCompletionAsync(client, run1, "succeeded");

        // The drift resolution loop: append the dated event and re-ingest —
        // the hash change alone makes it a full run.
        File.WriteAllText(eventsPath,
            """
            [
              {
                "effective_date": "2026-02-02",
                "date_precision": "day",
                "announcement_url": "https://example.org/2026-02-announcement",
                "events": [ { "card": "Sudden Menace", "action": "ban", "scope": "all" } ]
              }
            ]
            """);
        var run2 = await TriggerAsync(client);
        var second = await WaitForCompletionAsync(client, run2, "succeeded");

        Assert.False(second.GetProperty("no_op").GetBoolean());
        Assert.Equal("succeeded", Phase(second, "banlist_replay").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/banlist/2026-02-02")).StatusCode);
    }

    [Fact]
    public async Task Unexplained_commander_legality_flips_report_drift_and_change_no_legality()
    {
        var (connectionString, source, eventsPath) = await SetUpAsync();
        var categoriesPath = Path.ChangeExtension(eventsPath, ".categories.json");
        File.WriteAllText(categoriesPath,
            """
            [
              {
                "category": "conspiracy",
                "list_wording": "Conspiracy cards",
                "effective_date": "2025-06-01",
                "date_precision": "day",
                "announcement_url": "https://example.org/2025-categories",
                "members": { "card_type": "Conspiracy" }
              }
            ]
            """);
        File.WriteAllText(eventsPath,
            """
            [
              {
                "effective_date": "2026-01-15",
                "date_precision": "day",
                "announcement_url": "https://example.org/2026-01-announcement",
                "events": [
                  { "card": "Old Offender", "action": "ban", "scope": "all" },
                  { "card": "Otter Companion", "action": "ban", "scope": "companion" },
                  { "card": "Braids Analog", "action": "ban", "scope": "commander_only" },
                  { "card": "Freed Titan", "action": "ban", "scope": "all" }
                ]
              }
            ]
            """);
        using var app = NewApp(connectionString, source, eventsPath, categoriesPath: categoriesPath);
        using var client = app.CreateClient();
        var outlaw = Guid.NewGuid();
        var titan = Guid.NewGuid();
        source.OracleCards = Lines(
            // Scryfall says banned; the events file has no explaining entry.
            CardLine(outlaw, "Sudden Outlaw", commanderLegality: "banned"),
            // Flips the files explain — a named full ban and a category ban.
            CardLine(Guid.NewGuid(), "Old Offender", commanderLegality: "banned"),
            CardLine(Guid.NewGuid(), "Secret Plot", typeLine: "Conspiracy", commanderLegality: "banned"),
            // A category member Scryfall calls not_legal: the ban is moot, not drift.
            CardLine(Guid.NewGuid(), "Playtest Plot", typeLine: "Conspiracy", commanderLegality: "not_legal"),
            // Banned as commander or as companion only: still legal in the
            // deck, so Scryfall's "legal" is not drift.
            CardLine(Guid.NewGuid(), "Otter Companion", commanderLegality: "legal"),
            CardLine(Guid.NewGuid(), "Braids Analog", commanderLegality: "legal"),
            // The file says banned; Scryfall no longer does.
            CardLine(titan, "Freed Titan", commanderLegality: "legal"),
            // Legal on Scryfall and absent from the file: nothing to report.
            CardLine(Guid.NewGuid(), "Plain Bear"));

        var runId = await TriggerAsync(client);
        var run = await WaitForCompletionAsync(client, runId, "succeeded");

        // Report-only: the run completed, and exactly the two unexplained
        // flips landed, in name order.
        var drift = run.GetProperty("report").GetProperty("drift_detections").EnumerateArray().ToList();
        Assert.Equal(2, drift.Count);
        Assert.Equal("banlist", drift[0].GetProperty("kind").GetString());
        Assert.Equal(titan, drift[0].GetProperty("oracle_id").GetGuid());
        Assert.Equal("Freed Titan", drift[0].GetProperty("name").GetString());
        Assert.Equal("legal", drift[0].GetProperty("scryfall_commander_legality").GetString());
        Assert.True(drift[0].GetProperty("curated_banned").GetBoolean());
        // The resolution names the configured events file, not a fixed path.
        Assert.Contains(eventsPath, drift[0].GetProperty("resolution").GetString());
        Assert.Equal(outlaw, drift[1].GetProperty("oracle_id").GetGuid());
        Assert.Equal("banned", drift[1].GetProperty("scryfall_commander_legality").GetString());
        Assert.False(drift[1].GetProperty("curated_banned").GetBoolean());

        // Legality stays derived from the curated files: the detector wrote
        // no events and no versions beyond the two the files date.
        using var versions = JsonDocument.Parse(await client.GetStringAsync("/banlist/versions"));
        Assert.Equal(2, versions.RootElement.GetProperty("versions").GetArrayLength());
        using var banlist = JsonDocument.Parse(await client.GetStringAsync("/banlist/2026-01-15"));
        var banned = banlist.RootElement.GetProperty("banned").EnumerateArray().ToList();
        Assert.Equal(
            ["Braids Analog", "Freed Titan", "Old Offender", "Otter Companion", "Playtest Plot", "Secret Plot"],
            banned.Select(b => b.GetProperty("card").GetProperty("label").GetString()!).ToArray());
        // The category member carries its category; a named ban carries none.
        Assert.Equal("conspiracy", banned[5].GetProperty("category").GetString());
        Assert.False(banned[2].TryGetProperty("category", out _));
        var plotEvent = banned[5].GetProperty("ban_event").GetProperty("id").GetString();
        using var resolved = JsonDocument.Parse(await client.GetStringAsync($"/ban-events/{plotEvent}"));
        Assert.Equal("conspiracy", resolved.RootElement.GetProperty("category").GetString());
        Assert.Equal("https://example.org/2025-categories",
            resolved.RootElement.GetProperty("announcement").GetProperty("announcement_url").GetString());
    }

    [Fact]
    public async Task Corpus_state_serves_the_versions_block_at_rest_with_stamps_and_phase_runs()
    {
        var (connectionString, source, eventsPath) = await SetUpAsync();
        using var app = NewApp(connectionString, source, eventsPath);
        using var client = app.CreateClient();

        // At rest before any run: the shape holds with empty axes.
        var empty = await GetRunAsync(client, "/corpus-state");
        Assert.False(empty.GetProperty("versions").TryGetProperty("banlist_version", out _));
        Assert.Empty(empty.GetProperty("last_phase_runs").EnumerateObject());
        Assert.Empty(empty.GetProperty("freshness").GetProperty("curated_file_hashes").EnumerateObject());

        File.WriteAllText(eventsPath,
            """
            [
              {
                "effective_date": "2026-03-01",
                "date_precision": "day",
                "announcement_url": "https://example.org/2026-03-announcement",
                "events": [ { "card": "Banned Relic", "action": "ban", "scope": "all" } ]
              }
            ]
            """);
        source.OracleCards = Lines(CardLine(Guid.NewGuid(), "Banned Relic", edhrecRank: 10));
        var run1 = await TriggerAsync(client);
        await WaitForCompletionAsync(client, run1, "succeeded");
        // A no-op run must not disturb what the full run recorded.
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        var state = await GetRunAsync(client, "/corpus-state");

        var versions = state.GetProperty("versions");
        Assert.Equal("2026-03-01", versions.GetProperty("banlist_version").GetString());
        Assert.Equal("2026-08-07", versions.GetProperty("cr_effective_date").GetString());
        Assert.False(versions.TryGetProperty("staple_cut_version", out _));
        // Defined in the shape; gc_snapshot is absent until its issue lands.
        // The re-embed phase recorded the embedding state.
        Assert.False(versions.TryGetProperty("gc_snapshot", out _));
        Assert.Equal("qwen3-embedding:0.6b", state.GetProperty("embedding_state").GetProperty("model_tag").GetString());

        var phaseRuns = state.GetProperty("last_phase_runs");
        Assert.Equal(
            ["banlist_replay", "bracket_load", "card_feature_derive", "card_upsert", "cr_refresh", "fetch_validate", "legality_rebuild", "popularity_load", "re_embed", "source_profile_derive"],
            phaseRuns.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.All(phaseRuns.EnumerateObject(), p =>
        {
            Assert.Equal(run1, p.Value.GetProperty("run_id").GetInt32());
            Assert.NotEqual(JsonValueKind.Undefined, p.Value.GetProperty("finished_at").ValueKind);
        });

        var freshness = state.GetProperty("freshness");
        Assert.Equal(source.UpdatedAt, freshness.GetProperty("scryfall_updated_at").GetDateTimeOffset());
        Assert.Equal("2026-08-07", freshness.GetProperty("cr_effective_date").GetString());
        Assert.Matches("^[0-9A-F]{64}$", freshness.GetProperty("cr_text_sha256").GetString());
        var hashes = freshness.GetProperty("curated_file_hashes");
        Assert.Equal(
            ["banlist-categories.json", "banlist-events.json", "curated/chained-extra-turns.json", "curated/mass-land-denial.json"],
            hashes.EnumerateObject().Select(h => h.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.All(hashes.EnumerateObject(), h => Assert.Matches("^[0-9A-F]{64}$", h.Value.GetString()));
    }

    /// <summary>Phase 4, Review Focus 3: an archive ingested before cutover
    /// still stores the retired phases in corpus_state; /corpus-state renders
    /// only the canonical ones.</summary>
    [Fact]
    public void Corpus_state_drops_the_phase_runs_of_retired_phases()
    {
        using var stored = JsonDocument.Parse("""
            {"re_embed":{"run_id":3,"finished_at":"2026-09-30T00:00:00+00:00"},
             "staple_cut_recompute":{"run_id":3,"finished_at":"2026-09-30T00:00:00+00:00"},
             "staple_load":{"run_id":3,"finished_at":"2026-09-30T00:00:00+00:00"}}
            """);

        var runs = CorpusStateGate.PhaseRuns(stored);

        Assert.Equal(["re_embed"], runs.Keys);
    }

    /// <summary>Review finding: recording a phase also drops the retired
    /// phases from the stored row, so one ingest cleans it.</summary>
    [Fact]
    public void Recording_a_phase_drops_the_retired_phases_from_the_stored_row()
    {
        using var stored = JsonDocument.Parse("""
            {"re_embed":{"run_id":3,"finished_at":"2026-09-30T00:00:00+00:00"},
             "staple_load":{"run_id":3,"finished_at":"2026-09-30T00:00:00+00:00"}}
            """);

        using var recorded = CorpusStateGate.RecordPhase(stored, "bracket_load", 4, DateTimeOffset.UnixEpoch);

        Assert.Equal(["bracket_load", "re_embed"],
            recorded.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_failed_run_makes_the_next_unforced_run_a_full_rerun()
    {
        var (connectionString, source, eventsPath) = await SetUpAsync();
        // The sabotage phase takes the bracket-load slot: the re-embed slot
        // behind it must stay real, since its embedding-state write is part
        // of what "unchanged" means to the gate.
        var bracketLoad = new SwitchablePhase(IngestPhases.BracketLoad);
        using var app = NewApp(connectionString, source, eventsPath,
            services => services.AddSingleton<IIngestPhase>(bracketLoad));
        using var client = app.CreateClient();
        source.OracleCards = Lines(CardLine(Guid.NewGuid(), "Steady Relic"));
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        // A forced run over unchanged sources fails in a late phase.
        bracketLoad.Fail = true;
        await WaitForCompletionAsync(client, await TriggerAsync(client, force: true), "failed");

        // Recovery is triggering ingest again — no force — and the run
        // after a failure is never a no-op.
        bracketLoad.Fail = false;
        var rerun = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        Assert.False(rerun.GetProperty("no_op").GetBoolean());
        Assert.Equal("succeeded", Phase(rerun, "bracket_load").GetProperty("status").GetString());

        // With the rerun landed, unchanged sources are a no-op again.
        var settled = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        Assert.True(settled.GetProperty("no_op").GetBoolean());
    }

    [Fact]
    public async Task A_same_date_cr_republish_is_a_changed_source()
    {
        var (connectionString, source, eventsPath) = await SetUpAsync();
        var cr = new FakeCrSource();
        using var app = NewApp(connectionString, source, eventsPath, services =>
        {
            services.RemoveAll<ICrSource>();
            services.AddSingleton<ICrSource>(cr);
        });
        using var client = app.CreateClient();
        source.OracleCards = Lines(CardLine(Guid.NewGuid(), "Quiet Relic"));
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        var before = (await GetRunAsync(client, "/corpus-state"))
            .GetProperty("freshness").GetProperty("cr_text_sha256").GetString();

        // WotC republishes a wording correction under the same effective
        // date: the date alone would miss it; the text hash does not.
        cr.RawText = CrFixtures.RawExcerpt().Replace(
            "Synthetic fixture credits line.", "Synthetic fixture credits line, corrected.");
        var run = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        Assert.False(run.GetProperty("no_op").GetBoolean());
        Assert.Equal("succeeded", Phase(run, "cr_refresh").GetProperty("status").GetString());
        var after = (await GetRunAsync(client, "/corpus-state"))
            .GetProperty("freshness").GetProperty("cr_text_sha256").GetString();
        Assert.NotEqual(before, after);
    }

    private static JsonElement Phase(JsonElement run, string phase)
        => run.GetProperty("phases").EnumerateArray()
            .Single(p => p.GetProperty("phase").GetString() == phase);

    private async Task<(string ConnectionString, FakeBulkSource Source, string EventsPath)> SetUpAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync($"corpus_state_{Guid.NewGuid():N}");
        var eventsPath = Path.Combine(
            Path.GetTempPath(), "mtg-archive-tests", $"corpus-state-events-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(eventsPath)!);
        File.WriteAllText(eventsPath, "[]");
        return (connectionString, new FakeBulkSource(), eventsPath);
    }

    /// <summary>The events file lives at a test-owned path so tests can edit
    /// it; the bulk source is the test's own instance. The CR source stays
    /// <see cref="ArchiveApp"/>'s fixture default unless
    /// <paramref name="configureServices"/> swaps it.</summary>
    private static ArchiveApp NewApp(
        string connectionString,
        FakeBulkSource source,
        string eventsPath,
        Action<IServiceCollection>? configureServices = null,
        string? categoriesPath = null)
        => new(
            autoMigrate: true,
            connectionString,
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(source);
                configureServices?.Invoke(services);
            },
            categoriesPath is null
                ? new Dictionary<string, string?> { ["Banlist:EventsPath"] = eventsPath }
                : new Dictionary<string, string?> { ["Banlist:EventsPath"] = eventsPath, ["Banlist:CategoriesPath"] = categoriesPath });
}
