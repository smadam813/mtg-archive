using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tests;

/// <summary>
/// POST /simulate on the XMage sidecar: the envelope names XMage and its
/// version, a card XMage lacks is a blank card, and every
/// failure of the sidecar is a problem response, never a hang or a floor.
/// The refusals run on the fake sidecar; the full request runs on the real
/// image.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SimulateXmageEndpointTests(PostgresFixture postgres)
{
    internal const string Missing = "No Such Card In Xmage";

    private const string MissingCommander = "No Such Commander In Xmage";

    private static object NeedleByThree => new { kind = "on_battlefield", card = "Pithing Needle", by_turn = 3 };

    /// <summary>Tier 3 on XMage: both arms play on the sidecar, and the envelope carries the disruption report.</summary>
    [Fact]
    public async Task A_disruption_profile_on_xmage_runs_both_arms()
    {
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = XmageApp(await SeedAsync(postgres), Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/simulate", new
        {
            decklist = Decklist(),
            goal = NeedleByThree,
            runs = 10,
            seed = 1,
            disruption_profile = new[] { new { kind = "counterspell", turn = 2 }, new { kind = "targeted_removal", turn = 3 } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("xmage", envelope.GetProperty("engine").GetProperty("name").GetString());
        var disruption = envelope.GetProperty("disruption");
        Assert.Equal(2, disruption.GetProperty("events").GetArrayLength());
        // Each arm: 10 runs and one exemplar replay (the first failure; no run succeeds).
        Assert.Equal(22, fake.Received.Count(m => (string?)m["type"] == "start_game"));
        Assert.Equal(11, fake.Received.Count(m => (string?)m["type"] == "start_game" && (bool)m["watch_casts"]!));
        Assert.All(StartGames(fake), m => Assert.True(SeatsIdle(m), m.ToJsonString()));
        Assert.Equal("abstracted", envelope.GetProperty("disruption_mode").GetString());
    }

    /// <summary>In an interactive request the baseline arm
    /// seats the idle opponent and the disrupted arm seats the scripted
    /// opponent with the disruption card table.</summary>
    [Fact]
    public async Task An_interactive_request_seats_the_scripted_opponent_in_the_disrupted_arm_only()
    {
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = XmageApp(await SeedAsync(postgres), Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/simulate", new
        {
            decklist = Decklist(),
            goal = NeedleByThree,
            runs = 10,
            seed = 1,
            disruption_profile = Array.Empty<object>(),
            disruption_mode = "interactive",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("interactive", envelope.GetProperty("disruption_mode").GetString());
        var starts = StartGames(fake);
        // Each arm: 10 runs and one exemplar replay (the first failure; no run succeeds).
        Assert.Equal(22, starts.Count);
        Assert.Equal(11, starts.Count(SeatsIdle));
        var scripted = starts.Where(m => m["opponent"] is not null).ToList();
        Assert.Equal(11, scripted.Count);
        Assert.All(scripted, m => Assert.Equal("""{"cards":["Counterspell","Atomize","Rout"]}""", m["opponent"]!.ToJsonString()));
        Assert.All(starts, m => Assert.False((bool)m["watch_casts"]!));
        // Both arms play the same seeds.
        Assert.Equal(
            starts.Where(SeatsIdle).Select(m => (int)m["seed"]!).Order(),
            scripted.Select(m => (int)m["seed"]!).Order());
    }

    /// <summary>The hold policy runs both arms, the envelope
    /// echoes it, and its limits join the interactive assumptions. A request
    /// without the member, or with the greedy policy, is the envelope of
    /// before.</summary>
    [Fact]
    public async Task The_hold_policy_is_echoed_and_declares_its_limits()
    {
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = XmageApp(await SeedAsync(postgres), Endpoint(fake.Options()));
        using var client = app.CreateClient();

        async Task<JsonElement> PostAsync(string? policy)
        {
            var body = new System.Text.Json.Nodes.JsonObject
            {
                ["decklist"] = System.Text.Json.JsonSerializer.SerializeToNode(Decklist()),
                ["goal"] = System.Text.Json.JsonSerializer.SerializeToNode(NeedleByThree),
                ["runs"] = 5,
                ["seed"] = 1,
                ["disruption_profile"] = new System.Text.Json.Nodes.JsonArray(),
                ["disruption_mode"] = "interactive",
            };
            if (policy is not null)
            {
                body["play_policy"] = policy;
            }

            var response = await client.PostAsJsonAsync("/simulate", body);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        var none = await PostAsync(null);
        var greedy = await PostAsync("goal-aware-greedy");
        var hold = await PostAsync("goal-aware-hold");

        Assert.Equal("goal-aware-greedy", none.GetProperty("policies").GetProperty("play").GetString());
        Assert.Equal(none.GetProperty("disruption").GetRawText(), greedy.GetProperty("disruption").GetRawText());
        Assert.Equal("goal-aware-hold", hold.GetProperty("policies").GetProperty("play").GetString());
        Assert.Equal("goal-aware", hold.GetProperty("policies").GetProperty("mulligan").GetString());
        Assert.StartsWith(">= ", hold.GetProperty("probability").GetProperty("label").GetString());
        Assert.EndsWith("%, greedy policy", hold.GetProperty("probability").GetProperty("label").GetString());

        var assumptions = hold.GetProperty("disruption").GetProperty("assumptions").EnumerateArray().Select(e => e.GetString()!).ToList();
        var defaults = none.GetProperty("disruption").GetProperty("assumptions").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(defaults.Count + 3, assumptions.Count);
        Assert.Contains(assumptions, a => a.StartsWith("Hold policy:", StringComparison.Ordinal));
        Assert.Contains(assumptions, a => a.StartsWith("Hold reserve:", StringComparison.Ordinal));
        Assert.Contains(assumptions, a => a.StartsWith("Hold limits:", StringComparison.Ordinal) && a.Contains("counterspell path", StringComparison.Ordinal) && a.Contains("all_of", StringComparison.Ordinal) && a.Contains("colors", StringComparison.Ordinal));
        Assert.DoesNotContain(assumptions, a => a.Contains("may tap out", StringComparison.Ordinal));
        Assert.Contains(defaults, a => a.Contains("may tap out", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tier_2_request_seats_the_idle_opponent_and_echoes_no_mode()
    {
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = XmageApp(await SeedAsync(postgres), Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/simulate", new { decklist = Decklist(), goal = NeedleByThree, runs = 5, seed = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(envelope.TryGetProperty("disruption_mode", out _));
        Assert.NotEmpty(StartGames(fake));
        Assert.All(StartGames(fake), m => Assert.True(SeatsIdle(m), m.ToJsonString()));
    }

    /// <summary>A stale image speaks protocol 1, and the
    /// request fails at hello with a 503 that names both versions.</summary>
    [Fact]
    public async Task A_sidecar_on_an_older_protocol_is_a_503_problem_that_names_both_versions()
    {
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3), protocol: 1);
        using var app = XmageApp(await SeedAsync(postgres), Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var problem = await ProblemAsync(client, HttpStatusCode.ServiceUnavailable, new { decklist = Decklist(), goal = NeedleByThree, runs = 5 });

        Assert.Equal("urn:mtg-archive:problem:engine-unavailable", problem.GetProperty("type").GetString());
        var detail = problem.GetProperty("detail").GetString();
        Assert.Contains("protocol 1", detail);
        Assert.Contains($"protocol {XmageWire.Protocol}", detail);
        Assert.Empty(StartGames(fake));
    }

    private static List<System.Text.Json.Nodes.JsonObject> StartGames(FakeSidecar fake)
        => [.. fake.Received.Where(m => (string?)m["type"] == "start_game")];

    /// <summary>The start_game names the opponent member, and it is null: the idle opponent.</summary>
    private static bool SeatsIdle(System.Text.Json.Nodes.JsonObject start)
        => start.ContainsKey("opponent") && start["opponent"] is null;

    [Fact]
    public async Task An_unreachable_sidecar_is_a_503_problem()
    {
        using var app = XmageApp(await SeedAsync(postgres), ClosedEndpoint());
        using var client = app.CreateClient();

        var problem = await ProblemAsync(client, HttpStatusCode.ServiceUnavailable, new { decklist = Decklist(), goal = NeedleByThree, runs = 10 });

        Assert.Equal("urn:mtg-archive:problem:engine-unavailable", problem.GetProperty("type").GetString());
    }

    /// <summary>Phase 4, Review Focus 2: the service starts and answers /healthz with no sidecar; only /simulate needs one.</summary>
    [Fact]
    public async Task Healthz_does_not_need_the_sidecar()
    {
        using var app = XmageApp(await SeedAsync(postgres), ClosedEndpoint());
        using var client = app.CreateClient();

        var health = await client.GetAsync("/healthz");
        var problem = await ProblemAsync(client, HttpStatusCode.ServiceUnavailable, new { decklist = Decklist(), goal = NeedleByThree, runs = 5 });

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("urn:mtg-archive:problem:engine-unavailable", problem.GetProperty("type").GetString());
    }

    /// <summary>The sidecar exits partway through a batch. The
    /// request ends in the 503 problem well inside the read timeout; no game
    /// waits for the full read timeout, and the caller never sees a dropped
    /// connection.</summary>
    [Fact]
    public async Task A_sidecar_that_exits_partway_through_a_batch_is_a_503_problem_in_bounded_time()
    {
        var games = 0;
        FakeSidecar? fake = null;
        await using var sidecar = new FakeSidecar(game =>
        {
            if (Interlocked.Increment(ref games) == 5)
            {
                fake!.Exit();
                return Task.CompletedTask;
            }

            return game.OverAsync("horizon", 3);
        });
        fake = sidecar;
        using var app = XmageApp(await SeedAsync(postgres), Endpoint(sidecar.Options(readTimeoutSeconds: 30)));
        using var client = app.CreateClient();

        var clock = Stopwatch.StartNew();
        var problem = await ProblemAsync(client, HttpStatusCode.ServiceUnavailable, new { decklist = Decklist(), goal = NeedleByThree, runs = 100, seed = 1 });

        Assert.Equal("urn:mtg-archive:problem:engine-unavailable", problem.GetProperty("type").GetString());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"The problem took {clock.Elapsed}; the read timeout is 30 s.");
        var played = Volatile.Read(ref games);
        Assert.True(played >= 5, $"The sidecar played {played} games before it exited.");
    }

    [Fact]
    public async Task More_than_one_percent_engine_errors_is_a_502_problem()
    {
        await using var fake = new FakeSidecar(game => game.OverAsync("engine_error", 0, error: "boom"));
        using var app = XmageApp(await SeedAsync(postgres), Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var problem = await ProblemAsync(client, HttpStatusCode.BadGateway, new { decklist = Decklist(), goal = NeedleByThree, runs = 10, seed = 1 });

        Assert.Equal("urn:mtg-archive:problem:engine-errors", problem.GetProperty("type").GetString());
        Assert.Equal(10, problem.GetProperty("engine_errors").GetInt32());
        Assert.Equal(10, problem.GetProperty("games").GetInt32());
    }

    [Fact]
    public async Task A_goal_only_a_blank_card_answers_is_an_invalid_goal()
    {
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3), unimplemented: [Missing]);
        using var app = XmageApp(await SeedAsync(postgres), Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var problem = await ProblemAsync(client, HttpStatusCode.BadRequest, new
        {
            decklist = Decklist(),
            goal = new { kind = "cast", card = Missing, by_turn = 3 },
        });

        Assert.Equal("urn:mtg-archive:problem:invalid-goal", problem.GetProperty("type").GetString());
        Assert.Contains("blank card", Assert.Single(problem.GetProperty("errors").EnumerateArray()).GetString());
        Assert.DoesNotContain(fake.Received, m => (string?)m["type"] == "start_game");
    }

    [Fact]
    public async Task A_commander_xmage_lacks_is_a_422_problem()
    {
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3), unimplemented: [MissingCommander]);
        using var app = XmageApp(await SeedAsync(postgres), Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var problem = await ProblemAsync(client, HttpStatusCode.UnprocessableEntity, new { decklist = Decklist(MissingCommander), goal = NeedleByThree });

        Assert.Equal("urn:mtg-archive:problem:engine-cannot-run", problem.GetProperty("type").GetString());
        Assert.Equal([MissingCommander], problem.GetProperty("cards").EnumerateArray().Select(e => e.GetString()));
    }

    /// <summary>Phase 2, Review Focus 1 and 4 through the real image: the envelope
    /// names XMage, lists the blank and the unprofiled commander, and the
    /// same request gives the same envelope.</summary>
    [Fact]
    public async Task A_request_on_xmage_names_the_engine_and_its_coverage_and_replays_the_same()
    {
        using var app = XmageApp(await SeedAsync(postgres), Endpoint(await XmageSidecarContainer.OptionsAsync()));
        using var client = app.CreateClient();
        var request = new { decklist = Decklist(), goal = NeedleByThree, runs = 40, seed = 7, turn_horizon = 3 };

        var first = await client.PostAsJsonAsync("/simulate", request);
        var second = await client.PostAsJsonAsync("/simulate", request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var text = await first.Content.ReadAsStringAsync();
        Assert.Equal(text, await second.Content.ReadAsStringAsync());
        var envelope = JsonDocument.Parse(text).RootElement;
        Assert.Equal("xmage", envelope.GetProperty("engine").GetProperty("name").GetString());
        Assert.Matches(@"^xmage-1\.4\.62V1\+[0-9a-f]{8}$", envelope.GetProperty("engine").GetProperty("version").GetString()!);
        Assert.Equal([Missing], envelope.GetProperty("engine_coverage").GetProperty("unimplemented").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["Sheoldred, the Apocalypse"], envelope.GetProperty("engine_coverage").GetProperty("unprofiled").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(0, envelope.GetProperty("engine_errors").GetInt32());
        Assert.Equal(XmageEngine.SimplificationsFor(GameRules.Commander), envelope.GetProperty("simplifications").EnumerateArray().Select(e => e.GetString()!));
        Assert.True(envelope.GetProperty("successes").GetInt32() > 0);
        Assert.All(envelope.GetProperty("exemplars").EnumerateArray(), e => Assert.NotEmpty(e.GetProperty("log").EnumerateArray()));
    }

    internal static ArchiveApp XmageApp(string connectionString, string endpoint)
        => new(autoMigrate: true, connectionString, settings: new Dictionary<string, string?>
        {
            [XmageOptions.EndpointSetting] = endpoint,
            [XmageOptions.ConnectTimeoutSetting] = "2",
        });

    internal static string Endpoint(XmageOptions options) => $"{options.Host}:{options.Port}";

    /// <summary>A port nothing listens on.</summary>
    private static string ClosedEndpoint()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return $"127.0.0.1:{port}";
    }

    internal static async Task<JsonElement> ProblemAsync(HttpClient client, HttpStatusCode status, object request)
    {
        var response = await client.PostAsJsonAsync("/simulate", request);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal static object Decklist(string commander = "Sheoldred, the Apocalypse") => new
    {
        commanders = new[] { new { name = commander } },
        entries = new[]
        {
            new { card = new { name = "Swamp" }, quantity = 40 },
            new { card = new { name = "Pithing Needle" }, quantity = 10 },
            new { card = new { name = "Dark Ritual" }, quantity = 4 },
            new { card = new { name = Missing }, quantity = 5 },
        },
    };

    /// <summary>
    /// A fresh database of real card names XMage knows, a card it lacks,
    /// and a commander it lacks. Every card has a card feature profile row
    /// but Sheoldred, so engine coverage lists Sheoldred as unprofiled.
    /// </summary>
    internal static async Task<string> SeedAsync(PostgresFixture postgres)
    {
        var connectionString = await postgres.CreateDatabaseAsync($"simulate_xmage_{Guid.NewGuid():N}");
        await using var db = PostgresFixture.CreateContext(connectionString);
        await db.Database.MigrateAsync();

        var swamp = Card("Swamp", "Basic Land — Swamp", null, 0);
        swamp.Supertypes = ["Basic"];
        swamp.CardTypes = ["Land"];
        swamp.Subtypes = ["Swamp"];
        var needle = Card("Pithing Needle", "Artifact", "{1}", 1);
        var ritual = Card("Dark Ritual", "Instant", "{B}", 1);
        ritual.CardTypes = ["Instant"];
        var blank = Card(Missing, "Sorcery", "{2}", 2);
        blank.CardTypes = ["Sorcery"];
        var sheoldred = Commander("Sheoldred, the Apocalypse", "Legendary Creature — Phyrexian Praetor", "{2}{B}{B}", 4);
        var missingCommander = Commander(MissingCommander, "Legendary Creature — Human", "{1}{B}", 2);
        db.Cards.AddRange(swamp, needle, ritual, blank, sheoldred, missingCommander);
        await db.SaveChangesAsync();

        db.SourceProfiles.Add(new SourceProfile { CardId = swamp.Id, ProducedColors = ["B"] });
        db.CardFeatureProfileRows.AddRange(
            new CardFeatureProfileRow { CardId = swamp.Id, FaceIndex = 0, Develops = true, TapMana = ["{B}"], HasActivatedAbility = true },
            new CardFeatureProfileRow { CardId = needle.Id, FaceIndex = 0 },
            new CardFeatureProfileRow { CardId = ritual.Id, FaceIndex = 0, Develops = true, SpellMana = "{B}{B}{B}" },
            new CardFeatureProfileRow { CardId = blank.Id, FaceIndex = 0 },
            new CardFeatureProfileRow { CardId = missingCommander.Id, FaceIndex = 0 });
        await db.SaveChangesAsync();
        return connectionString;
    }

    private static Card Card(string name, string typeLine, string? manaCost, int manaValue)
    {
        var card = TestCards.Minimal(Guid.NewGuid(), "B");
        card.Name = name;
        card.TypeLine = typeLine;
        card.ManaCost = manaCost;
        card.ManaValue = manaValue;
        return card;
    }

    private static Card Commander(string name, string typeLine, string manaCost, int manaValue)
    {
        var card = Card(name, typeLine, manaCost, manaValue);
        card.Supertypes = ["Legendary"];
        card.CardTypes = ["Creature"];
        card.CanBeCommander = true;
        return card;
    }
}
