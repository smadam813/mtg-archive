using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Data;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The bracket load phase, POST /bracket-classification, its reference
/// endpoints, and Game Changers drift over a real ingest: the
/// spec, snapshots, and curated lists replay from the data files (hard
/// failure on an unresolvable name), the classification carries the four
/// parts with fetchable snapshot citations and the versions echo, an
/// unknown pin is a 4xx problem, and a <c>game_changer</c> flip is
/// report-only drift that never touches the snapshot files.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BracketClassificationTests(PostgresFixture postgres) : IDisposable
{
    private static readonly string SpecFixture =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "bracket", "spec-2025-10-21.json");

    private readonly string _dataDir = Directory.CreateTempSubdirectory("mtg-archive-bracket-").FullName;

    private readonly Guid _commander = Guid.NewGuid();
    private readonly Guid _tutor = Guid.NewGuid();
    private readonly Guid _cradle = Guid.NewGuid();
    private readonly Guid _armageddon = Guid.NewGuid();
    private readonly Guid _timeWarp = Guid.NewGuid();
    private readonly Guid _forest = Guid.NewGuid();

    public void Dispose() => Directory.Delete(_dataDir, recursive: true);

    private string SpecDir => Path.Combine(_dataDir, "bracket-spec");

    private string SnapshotDir => Path.Combine(_dataDir, "game-changers");

    private string CuratedDir => Path.Combine(_dataDir, "curated");

    [Fact]
    public async Task Classification_carries_the_four_parts_the_citations_and_the_versions_echo()
    {
        WriteData();
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = Corpus();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        var response = await client.PostAsJsonAsync("/bracket-classification", new
        {
            decklist = Decklist("Test Commander", "Demonic Tutor", "Armageddon", "Forest"),
            target_bracket = 3,
            assumptions = new { intent_bracket = 4 },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["bracket_floor", "assumed_bracket", "reasons", "assumptions", "citations", "versions"],
            body.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.False(body.TryGetProperty("bracket", out _));

        var floor = body.GetProperty("bracket_floor");
        Assert.Equal(4, floor.GetProperty("bracket").GetInt32());
        Assert.Equal(3, floor.GetProperty("target_bracket").GetInt32());
        Assert.False(floor.GetProperty("within_target").GetBoolean());
        var assumed = body.GetProperty("assumed_bracket");
        Assert.Equal(4, assumed.GetProperty("bracket").GetInt32());
        Assert.Equal("declared", assumed.GetProperty("status").GetString());

        var reasons = body.GetProperty("reasons").EnumerateArray().ToList();
        Assert.Equal(["game_changers_count", "mass_land_denial"],
            reasons.Select(r => r.GetProperty("barometer").GetString()).ToArray());
        var gc = Assert.Single(reasons[0].GetProperty("cards").EnumerateArray());
        Assert.Equal("card", gc.GetProperty("type").GetString());
        Assert.Equal(_tutor.ToString(), gc.GetProperty("id").GetString());
        Assert.Equal("Demonic Tutor", gc.GetProperty("label").GetString());
        Assert.Equal("Bracket 3 allows for up to three Game Changers.", reasons[0].GetProperty("threshold").GetString());
        Assert.Equal("Armageddon", Assert.Single(reasons[1].GetProperty("cards").EnumerateArray()).GetProperty("label").GetString());

        var assumptions = body.GetProperty("assumptions").EnumerateArray().ToList();
        Assert.Equal(["intent_bracket", "chains_extra_turns"], assumptions.Select(a => a.GetProperty("name").GetString()).ToArray());
        Assert.True(assumptions[0].GetProperty("declared").GetBoolean());
        Assert.False(assumptions[1].GetProperty("declared").GetBoolean());

        var citations = body.GetProperty("citations").EnumerateArray().ToList();
        Assert.Equal(["snapshot", "snapshot"], citations.Select(c => c.GetProperty("type").GetString()).ToArray());
        Assert.Equal(["bracket-spec/2025-10-21", "game-changers/2026-02-09"],
            citations.Select(c => c.GetProperty("id").GetString()).ToArray());
        var versions = body.GetProperty("versions");
        Assert.Equal("2025-10-21", versions.GetProperty("bracket_spec_version").GetString());
        Assert.Equal("2026-02-09", versions.GetProperty("gc_snapshot").GetString());
        Assert.False(versions.TryGetProperty("banlist_version", out _));

        // Every citation id is fetchable under GET /.
        foreach (var id in citations.Select(c => c.GetProperty("id").GetString()))
        {
            var fetched = await client.GetAsync($"/{id}");
            Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        }
    }

    [Fact]
    public async Task Undeclared_assumptions_leave_the_assumed_bracket_unknown()
    {
        WriteData();
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = Corpus();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        var response = await client.PostAsJsonAsync("/bracket-classification", new
        {
            decklist = Decklist("Test Commander", "Forest"),
            target_bracket = 2,
        });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("bracket_floor").GetProperty("bracket").GetInt32());
        var assumed = body.GetProperty("assumed_bracket");
        Assert.Equal("unknown", assumed.GetProperty("status").GetString());
        Assert.False(assumed.TryGetProperty("bracket", out _));
        Assert.Empty(body.GetProperty("reasons").EnumerateArray());
        Assert.All(body.GetProperty("assumptions").EnumerateArray(), a => Assert.False(a.GetProperty("declared").GetBoolean()));
    }

    [Fact]
    public async Task A_pinned_snapshot_changes_the_count_and_the_echo()
    {
        WriteData();
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = Corpus();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        // The 2025-02-11 snapshot lists the Cradle only, so the Tutor is no signal under it.
        var response = await client.PostAsJsonAsync("/bracket-classification", new
        {
            decklist = Decklist("Test Commander", "Demonic Tutor"),
            target_bracket = 2,
            gc_snapshot = "2025-02-11",
        });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("bracket_floor").GetProperty("bracket").GetInt32());
        Assert.Equal("2025-02-11", body.GetProperty("versions").GetProperty("gc_snapshot").GetString());
        Assert.Contains(body.GetProperty("citations").EnumerateArray(),
            c => c.GetProperty("id").GetString() == "game-changers/2025-02-11");
    }

    [Theory]
    [InlineData("gc_snapshot", "2024-01-01")]
    [InlineData("bracket_spec_version", "2024-01-01")]
    public async Task An_unknown_version_pin_is_a_4xx_problem(string parameter, string value)
    {
        WriteData();
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = Corpus();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        var response = await client.PostAsJsonAsync("/bracket-classification", new Dictionary<string, object>
        {
            ["decklist"] = Decklist("Test Commander"),
            ["target_bracket"] = 2,
            [parameter] = value,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(parameter, problem.GetProperty("parameter").GetString());
        Assert.Equal(value, problem.GetProperty("value").GetString());

        var reference = await client.GetAsync($"/{parameter.Replace("gc_snapshot", "game-changers").Replace("bracket_spec_version", "bracket-spec")}/{value}");
        Assert.Equal(HttpStatusCode.NotFound, reference.StatusCode);
    }

    [Fact]
    public async Task Refusals_are_problems_not_guesses()
    {
        WriteData();
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = Corpus();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        var unknownBracket = await client.PostAsJsonAsync("/bracket-classification",
            new { decklist = Decklist("Test Commander"), target_bracket = 6 });
        Assert.Equal(HttpStatusCode.BadRequest, unknownBracket.StatusCode);
        var problem = await unknownBracket.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("target_bracket", problem.GetProperty("parameter").GetString());
        Assert.Equal([1, 2, 3, 4, 5], problem.GetProperty("known").EnumerateArray().Select(k => k.GetInt32()).ToArray());

        var missing = await client.PostAsJsonAsync("/bracket-classification", new { decklist = Decklist("Test Commander") });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("target_bracket",
            (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("parameter").GetString());

        var unresolved = await client.PostAsJsonAsync("/bracket-classification",
            new { decklist = Decklist("Test Commander", "No Such Card"), target_bracket = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, unresolved.StatusCode);
        var names = (await unresolved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unresolved_names");
        Assert.Equal("No Such Card", Assert.Single(names.EnumerateArray()).GetProperty("name").GetString());
    }

    // ADR 0012: brackets apply to Commander only. A Brawl
    // format is a 400 before any other check, so a skill error cannot get
    // a Commander bracket for a Brawl deck.
    [Theory]
    [InlineData("brawl")]
    [InlineData("competitive_brawl")]
    [InlineData("standard_brawl")]
    [InlineData("standard")]
    public async Task A_brawl_format_is_a_400_that_names_the_format(string format)
    {
        using var app = new ArchiveApp(autoMigrate: true, await postgres.CreateDatabaseAsync($"bracket_brawl_{Guid.NewGuid():N}"));
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/bracket-classification",
            new { decklist = Decklist("Test Commander"), target_bracket = 2, format });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("urn:mtg-archive:problem:commander-only", problem.GetProperty("type").GetString());
        Assert.Equal("format", problem.GetProperty("parameter").GetString());
        Assert.Equal(format, problem.GetProperty("value").GetString());
        Assert.Contains(format, problem.GetProperty("detail").GetString());
        Assert.Contains("Commander only", problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("brawl")]
    [InlineData("competitive_brawl")]
    [InlineData("standard_brawl")]
    public async Task A_brawl_format_detail_says_Arena_has_no_bracket_system(string format)
    {
        var problem = await CommanderOnlyProblemAsync(format, "bracket_brawl_detail");

        var detail = problem.GetProperty("detail").GetString();
        Assert.Contains(format, detail);
        Assert.Contains("Commander only", detail);
        Assert.Contains("Arena publishes no bracket system for Brawl.", detail);
    }

    [Fact]
    public async Task The_standard_format_detail_names_standard_and_not_Brawl()
    {
        var problem = await CommanderOnlyProblemAsync("standard", "bracket_standard_detail");

        Assert.Equal("urn:mtg-archive:problem:commander-only", problem.GetProperty("type").GetString());
        var detail = problem.GetProperty("detail").GetString();
        Assert.Contains("standard", detail);
        Assert.Contains("Commander only", detail);
        Assert.DoesNotContain("Brawl", detail);
    }

    private async Task<JsonElement> CommanderOnlyProblemAsync(string format, string databasePrefix)
    {
        using var app = new ArchiveApp(autoMigrate: true, await postgres.CreateDatabaseAsync($"{databasePrefix}_{Guid.NewGuid():N}"));
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/bracket-classification",
            new { decklist = Decklist("Test Commander"), target_bracket = 2, format });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task An_unknown_format_is_a_400_that_names_it()
    {
        using var app = new ArchiveApp(autoMigrate: true, await postgres.CreateDatabaseAsync($"bracket_fmt_{Guid.NewGuid():N}"));
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/bracket-classification",
            new { decklist = Decklist("Test Commander"), target_bracket = 2, format = "historic" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("urn:mtg-archive:problem:unknown-format", problem.GetProperty("type").GetString());
        Assert.Equal("historic", problem.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("commander")]
    public async Task No_format_or_commander_classifies_and_echoes_the_commander_format(string? format)
    {
        WriteData();
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = Corpus();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        var body = new Dictionary<string, object?>
        {
            ["decklist"] = Decklist("Test Commander", "Forest"),
            ["target_bracket"] = 2,
        };
        if (format is not null)
        {
            body["format"] = format;
        }

        var response = await client.PostAsJsonAsync("/bracket-classification", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var versions = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("versions");
        Assert.Equal("commander", versions.GetProperty("format").GetString());
        Assert.Equal("2025-10-21", versions.GetProperty("bracket_spec_version").GetString());
    }

    [Fact]
    public async Task Nothing_ingested_is_a_problem_not_a_fallback_spec()
    {
        using var app = new ArchiveApp(autoMigrate: true, await postgres.CreateDatabaseAsync($"bracket_empty_{Guid.NewGuid():N}"));
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/bracket-classification",
            new { decklist = Decklist(), target_bracket = 2 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bracket_spec_version",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("parameter").GetString());
    }

    [Fact]
    public async Task Reference_endpoints_serve_the_versions_and_the_documents()
    {
        WriteData();
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = Corpus();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        var specVersions = await client.GetFromJsonAsync<JsonElement>("/bracket-spec/versions");
        Assert.Equal("2025-10-21",
            Assert.Single(specVersions.GetProperty("versions").EnumerateArray()).GetProperty("date").GetString());
        var spec = await client.GetFromJsonAsync<JsonElement>("/bracket-spec/2025-10-21");
        Assert.Equal("bracket-spec/2025-10-21", spec.GetProperty("citation").GetProperty("id").GetString());
        Assert.Equal(3, spec.GetProperty("document").GetProperty("barometers").GetArrayLength());

        var snapshotVersions = await client.GetFromJsonAsync<JsonElement>("/game-changers/versions");
        Assert.Equal(["2025-02-11", "2026-02-09"],
            snapshotVersions.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("date").GetString()).ToArray());
        var snapshot = await client.GetFromJsonAsync<JsonElement>("/game-changers/2026-02-09");
        Assert.Equal("https://example.org/gc-2026", snapshot.GetProperty("source").GetString());
        Assert.Equal(["Demonic Tutor", "Gaea's Cradle"],
            snapshot.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("label").GetString()).ToArray());
        Assert.Equal(_cradle.ToString(), snapshot.GetProperty("cards")[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_game_changer_flip_is_report_only_drift_and_the_snapshot_file_is_untouched()
    {
        WriteData();
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        // Scryfall flags the Forest and not the Cradle; the 2026-02-09
        // snapshot says the opposite.
        source.OracleCards = Corpus(forestGameChanger: true, cradleGameChanger: false);
        var latestPath = Path.Combine(SnapshotDir, "2026-02-09.json");
        var before = File.ReadAllText(latestPath);
        var stamp = File.GetLastWriteTimeUtc(latestPath);

        var run = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        var drift = run.GetProperty("report").GetProperty("drift_detections").EnumerateArray().ToList();
        Assert.Equal(["Forest", "Gaea's Cradle"], drift.Select(d => d.GetProperty("name").GetString()).ToArray());
        Assert.All(drift, d => Assert.Equal("game_changers", d.GetProperty("kind").GetString()));
        Assert.Equal(_forest, drift[0].GetProperty("oracle_id").GetGuid());
        Assert.True(drift[0].GetProperty("scryfall_game_changer").GetBoolean());
        Assert.False(drift[0].GetProperty("snapshot_member").GetBoolean());
        Assert.False(drift[1].GetProperty("scryfall_game_changer").GetBoolean());
        Assert.True(drift[1].GetProperty("snapshot_member").GetBoolean());
        Assert.Equal("2026-02-09", drift[0].GetProperty("snapshot").GetString());
        Assert.StartsWith("report-only", drift[0].GetProperty("resolution").GetString());
        Assert.Empty(run.GetProperty("report").GetProperty("errors").EnumerateArray());

        Assert.Equal(before, File.ReadAllText(latestPath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(latestPath));
        Assert.Equal(["2025-02-11.json", "2026-02-09.json"],
            Directory.GetFiles(SnapshotDir).Select(Path.GetFileName).Order().ToArray());
        // The curated snapshot stays the truth the classifier reads.
        await using var db = PostgresFixture.CreateContext(connectionString);
        var latest = await db.GameChangersSnapshots.Include(s => s.Cards).SingleAsync(s => s.Date == new DateOnly(2026, 2, 9));
        var members = await db.Cards.Where(c => latest.Cards.Select(m => m.CardId).Contains(c.Id)).Select(c => c.OracleId).ToListAsync();
        Assert.Equal(new[] { _tutor, _cradle }.Order(), members.Order());
    }

    [Fact]
    public async Task A_second_run_replays_to_the_same_rows_and_a_dropped_file_drops_its_rows()
    {
        WriteData();
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = Corpus();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        var run2 = await TriggerAsync(client, force: true);
        await WaitForCompletionAsync(client, run2, "succeeded");

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(run2, Assert.Single(await db.BracketSpecVersions.ToListAsync()).IngestRunId);
            Assert.Equal(2, await db.GameChangersSnapshots.CountAsync());
            Assert.Equal(3, await db.Set<GameChangersSnapshotCard>().CountAsync());
            Assert.Equal(2, await db.BarometerCards.CountAsync());
        }

        // Dropping the older snapshot is a change the gate sees on its own.
        File.Delete(Path.Combine(SnapshotDir, "2025-02-11.json"));
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(new DateOnly(2026, 2, 9), Assert.Single(await db.GameChangersSnapshots.ToListAsync()).Date);
            Assert.Equal(2, await db.Set<GameChangersSnapshotCard>().CountAsync());
        }
    }

    [Fact]
    public async Task An_unresolvable_name_fails_the_run_and_keeps_the_previous_load()
    {
        WriteData();
        var (connectionString, source) = await SetUpAsync();
        using var app = NewApp(connectionString, source);
        using var client = app.CreateClient();
        source.OracleCards = Corpus();
        var run1 = await TriggerAsync(client);
        await WaitForCompletionAsync(client, run1, "succeeded");

        WriteSnapshot("2026-02-09", "https://example.org/gc-2026", "Demonic Tutor", "Gaea's Cradle", "No Such Card");
        var failed = await WaitForCompletionAsync(client, await TriggerAsync(client), "failed");

        var phase = failed.GetProperty("phases").EnumerateArray()
            .Single(p => p.GetProperty("phase").GetString() == "bracket_load");
        Assert.Equal("failed", phase.GetProperty("status").GetString());
        Assert.Contains(failed.GetProperty("report").GetProperty("errors").EnumerateArray(),
            e => e.GetString() == "game-changers/2026-02-09.json: no card named 'No Such Card' — the reference is exact Oracle name");
        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(run1, Assert.Single(await db.BracketSpecVersions.ToListAsync()).IngestRunId);
        Assert.Equal(2, await db.GameChangersSnapshots.Where(s => s.Date == new DateOnly(2026, 2, 9)).SelectMany(s => s.Cards).CountAsync());
    }

    private string Corpus(bool forestGameChanger = false, bool cradleGameChanger = true) => Lines(
        CardLine(_commander, "Test Commander", typeLine: "Legendary Creature — Human", manaCost: "{1}{G}", colorIdentity: ["G"]),
        CardLine(_tutor, "Demonic Tutor", typeLine: "Sorcery", gameChanger: true),
        CardLine(_cradle, "Gaea's Cradle", typeLine: "Legendary Land", gameChanger: cradleGameChanger),
        CardLine(_armageddon, "Armageddon", typeLine: "Sorcery"),
        CardLine(_timeWarp, "Time Warp", typeLine: "Sorcery"),
        CardLine(_forest, "Forest", typeLine: "Basic Land — Forest", gameChanger: forestGameChanger));

    private static object Decklist(params string[] names) => new
    {
        commanders = names.Take(1).Select(n => new { name = n }),
        entries = names.Skip(1).Select(n => new { card = new { name = n }, quantity = 1 }),
    };

    private void WriteData()
    {
        Directory.CreateDirectory(SpecDir);
        Directory.CreateDirectory(SnapshotDir);
        Directory.CreateDirectory(CuratedDir);
        File.Copy(SpecFixture, Path.Combine(SpecDir, "2025-10-21.json"));
        WriteSnapshot("2025-02-11", "https://example.org/gc-2025", "Gaea's Cradle");
        WriteSnapshot("2026-02-09", "https://example.org/gc-2026", "Demonic Tutor", "Gaea's Cradle");
        foreach (var (file, card) in new[] { ("mass-land-denial.json", "Armageddon"), ("chained-extra-turns.json", "Time Warp") })
        {
            File.WriteAllText(Path.Combine(CuratedDir, file),
                JsonSerializer.Serialize(new { source = "https://example.org/brackets", notes = "test", cards = new[] { card } }));
        }
    }

    private void WriteSnapshot(string date, string source, params string[] cards)
        => File.WriteAllText(Path.Combine(SnapshotDir, $"{date}.json"),
            JsonSerializer.Serialize(new { date, source, cards }));

    private async Task<(string ConnectionString, FakeBulkSource Source)> SetUpAsync()
        => (await postgres.CreateDatabaseAsync($"bracket_{Guid.NewGuid():N}"), new FakeBulkSource());

    private ArchiveApp NewApp(string connectionString, FakeBulkSource source)
        => new(
            autoMigrate: true,
            connectionString,
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(source);
                services.RemoveAll<ICrSource>();
                services.AddSingleton<ICrSource>(new FakeCrSource());
            },
            new Dictionary<string, string?>
            {
                ["Ingest:BracketSpecDir"] = SpecDir,
                ["Ingest:GameChangersDir"] = SnapshotDir,
                ["Ingest:CuratedDir"] = CuratedDir,
            });
}
