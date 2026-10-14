using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MtgArchive.Data;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tests;

/// <summary>
/// POST /simulate: the tier-2 envelope over a seeded corpus.
/// The tests check the floor label and the confidence interval as fields,
/// the seed replay, and the policy names. They check that the coverage
/// names the uncovered cards and queues them as demand. They check that an
/// illegal decklist runs with its verdict attached and that the versions
/// block echoes both pins. Every refusal is problem+json. The validation
/// and legality tests run on the fake sidecar over test cards; the play
/// tests run on the real sidecar image over real cards.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SimulateEndpointTests(PostgresFixture postgres)
{
    private const string BanlistVersion = "2025-01-01";

    private static readonly Regex FloorLabel = new(@"^>= \d+(\.\d)?%, greedy policy$");

    private static object NeedleByThree => new { kind = "on_battlefield", card = "Pithing Needle", by_turn = 3 };

    [Fact]
    public async Task Envelope_carries_the_floor_the_interval_the_policies_the_curve_and_the_exemplars()
    {
        using var app = await RealAppAsync();
        using var client = app.CreateClient();

        var envelope = await SimulateAsync(client, new
        {
            decklist = SimulateXmageEndpointTests.Decklist(),
            goal = NeedleByThree,
            runs = 40,
            seed = 7,
            turn_horizon = 3,
        });

        var probability = envelope.GetProperty("probability");
        var floor = probability.GetProperty("floor").GetDouble();
        Assert.Matches(FloorLabel, probability.GetProperty("label").GetString()!);
        var interval = probability.GetProperty("confidence_interval");
        Assert.Equal(0.95, interval.GetProperty("level").GetDouble());
        Assert.InRange(interval.GetProperty("lower").GetDouble(), 0, floor);
        Assert.InRange(interval.GetProperty("upper").GetDouble(), floor, 1);

        Assert.Equal(40, envelope.GetProperty("runs").GetInt32());
        Assert.Equal(7, envelope.GetProperty("base_seed").GetInt64());
        Assert.Equal(3, envelope.GetProperty("turn_horizon").GetInt32());
        Assert.True(envelope.GetProperty("successes").GetInt32() > 0);
        Assert.Equal(floor, envelope.GetProperty("successes").GetInt32() / 40.0);
        Assert.Equal("goal-aware-greedy", envelope.GetProperty("policies").GetProperty("play").GetString());
        Assert.Equal("goal-aware", envelope.GetProperty("policies").GetProperty("mulligan").GetString());

        var curve = envelope.GetProperty("curve").EnumerateArray().Select(p => p.GetProperty("cumulative").GetDouble()).ToList();
        Assert.Equal(3, curve.Count);
        Assert.Equal(curve, curve.Order());
        Assert.True(curve[^1] >= floor);
        Assert.Equal([1, 2, 3], envelope.GetProperty("curve").EnumerateArray().Select(p => p.GetProperty("turn").GetInt32()));

        var exemplars = envelope.GetProperty("exemplars").EnumerateArray().ToList();
        Assert.InRange(exemplars.Count, 2, 3);
        Assert.Contains(exemplars, e => e.GetProperty("kind").GetString() == "fastest" && e.GetProperty("success").GetBoolean());
        Assert.Contains(exemplars, e => e.GetProperty("kind").GetString() == "typical" && e.GetProperty("success").GetBoolean());
        foreach (var exemplar in exemplars)
        {
            Assert.NotEmpty(exemplar.GetProperty("log").EnumerateArray());
            Assert.True(exemplar.GetProperty("run_index").GetInt32() < 40);
            Assert.Equal(exemplar.GetProperty("seed").GetInt32(), RunSeedOf(envelope, exemplar));
        }

        var goal = envelope.GetProperty("goal");
        Assert.Equal("on_battlefield", goal.GetProperty("kind").GetString());
        Assert.Equal("Pithing Needle", goal.GetProperty("card").GetString());
        Assert.Equal(3, goal.GetProperty("by_turn").GetInt32());
        Assert.Equal(59, envelope.GetProperty("input").GetProperty("deck_size").GetInt32());
        Assert.Equal("Sheoldred, the Apocalypse",
            Assert.Single(envelope.GetProperty("input").GetProperty("commanders").EnumerateArray()).GetProperty("label").GetString());
        Assert.NotEmpty(envelope.GetProperty("simplifications").EnumerateArray());
    }

    [Fact]
    public async Task Envelope_names_the_engine_its_coverage_and_its_engine_errors()
    {
        using var app = await RealAppAsync();
        using var client = app.CreateClient();

        var envelope = await SimulateAsync(client, new { decklist = SimulateXmageEndpointTests.Decklist(), goal = NeedleByThree, runs = 5, seed = 1, turn_horizon = 3 });

        var engine = envelope.GetProperty("engine");
        Assert.Equal("xmage", engine.GetProperty("name").GetString());
        Assert.Matches(@"^xmage-1\.4\.62V1\+[0-9a-f]{8}$", engine.GetProperty("version").GetString()!);
        var coverage = envelope.GetProperty("engine_coverage");
        Assert.Equal([SimulateXmageEndpointTests.Missing], coverage.GetProperty("unimplemented").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(0, envelope.GetProperty("engine_errors").GetInt32());
        Assert.Equal(XmageEngine.SimplificationsFor(GameRules.Commander), envelope.GetProperty("simplifications").EnumerateArray().Select(e => e.GetString()!));
    }

    [Fact]
    public async Task Illegal_decklist_runs_and_carries_its_deck_verdict_and_the_banlist_version()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var envelope = await SimulateAsync(client, new
        {
            decklist = Deck(),
            goal = NeedleByThree,
            runs = 20,
            seed = 1,
            banlist_version = BanlistVersion,
        });

        Assert.Matches(FloorLabel, envelope.GetProperty("probability").GetProperty("label").GetString()!);
        Assert.Equal(20, envelope.GetProperty("runs").GetInt32());
        Assert.False(envelope.TryGetProperty("legality", out _));
        var verdict = envelope.GetProperty("deck_verdict");
        Assert.Equal("illegal", verdict.GetProperty("status").GetString());
        var finding = Assert.Single(verdict.GetProperty("card_findings").EnumerateArray());
        Assert.Equal("Test Commander", finding.GetProperty("card").GetProperty("label").GetString());
        Assert.Equal("commander", finding.GetProperty("role").GetString());
        Assert.Equal("banned", finding.GetProperty("status").GetString());
        Assert.Equal("ban_event", finding.GetProperty("ban_event").GetProperty("type").GetString());
        Assert.False(verdict.TryGetProperty("versions", out _));

        var versions = envelope.GetProperty("versions");
        Assert.Equal(BanlistVersion, versions.GetProperty("banlist_version").GetString());
        Assert.False(versions.TryGetProperty("staple_cut_version", out _));
    }

    [Fact]
    public async Task A_legal_100_card_deck_has_a_legal_deck_verdict()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var envelope = await SimulateAsync(client, new { decklist = LegalDeck(), goal = NeedleByThree, runs = 20, seed = 1 });

        var verdict = envelope.GetProperty("deck_verdict");
        Assert.Equal("legal", verdict.GetProperty("status").GetString());
        Assert.Empty(verdict.GetProperty("card_findings").EnumerateArray());
        Assert.Empty(verdict.GetProperty("shape_findings").EnumerateArray());
        Assert.Equal(100, verdict.GetProperty("deck_size").GetInt32());
    }

    [Fact]
    public async Task A_card_the_legality_rebuild_missed_makes_the_verdict_unknown()
    {
        var connectionString = await SeedCorpusAsync();
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var bear = await db.Cards.SingleAsync(c => c.Name == "Test Bear");
            db.Legalities.RemoveRange(db.Legalities.Where(l => l.CardId == bear.Id));
            await db.SaveChangesAsync();
        }

        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var envelope = await SimulateAsync(client, new { decklist = LegalDeck(), goal = NeedleByThree, runs = 20, seed = 1 });

        Assert.Equal("unknown", envelope.GetProperty("deck_verdict").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Without_a_banlist_replay_the_verdict_is_unknown_and_the_banlist_version_is_absent()
    {
        var connectionString = await SeedCorpusAsync(banlist: false);
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var envelope = await SimulateAsync(client, new { decklist = LegalDeck(), goal = NeedleByThree, runs = 20, seed = 1 });

        Assert.Equal("unknown", envelope.GetProperty("deck_verdict").GetProperty("status").GetString());
        Assert.False(envelope.GetProperty("versions").TryGetProperty("banlist_version", out _));
        Assert.Equal(["format"], envelope.GetProperty("versions").EnumerateObject().Select(p => p.Name));
    }

    /// <summary>The envelope's verdict is the deck verdict route's verdict
    /// for the same list and format, less the route's own versions block
    /// (ADR 0012). The illegal list breaks card legality and deck shape,
    /// so every part of the verdict takes part in the comparison.</summary>
    [Theory]
    [InlineData("commander", true)]
    [InlineData("commander", false)]
    [InlineData("brawl", true)]
    [InlineData("brawl", false)]
    [InlineData("standard_brawl", true)]
    public async Task The_simulate_verdict_equals_the_deck_verdict_route_verdict(string format, bool legal)
    {
        var connectionString = await SeedCorpusAsync();
        await SeedBrawlRowsAsync(connectionString, new DateTimeOffset(2026, 10, 4, 9, 5, 0, TimeSpan.Zero));
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();
        var decklist = legal ? LegalDeck() : Deck();

        var envelope = await SimulateAsync(client, new { decklist, goal = NeedleByThree, runs = 2, seed = 1, format });
        var response = await client.PostAsJsonAsync("/deck-verdict", new { decklist, format });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);

        var route = JsonNode.Parse(text)!.AsObject();
        Assert.True(route.Remove("versions"));
        var embedded = JsonNode.Parse(envelope.GetProperty("deck_verdict").GetRawText());
        Assert.True(JsonNode.DeepEquals(route, embedded), $"route {route.ToJsonString()}\nsimulate {embedded!.ToJsonString()}");
    }

    [Fact]
    public async Task The_same_seed_reproduces_the_result_and_an_omitted_seed_is_random_and_reported()
    {
        using var app = await RealAppAsync();
        using var client = app.CreateClient();
        var request = new { decklist = SimulateXmageEndpointTests.Decklist(), goal = NeedleByThree, runs = 20, seed = 7, turn_horizon = 3 };

        var first = await SimulateAsync(client, request);
        var second = await SimulateAsync(client, request);
        Assert.Equal(first.GetRawText(), second.GetRawText());

        var seeds = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var envelope = await SimulateAsync(client, new { decklist = SimulateXmageEndpointTests.Decklist(), goal = NeedleByThree, runs = 2, turn_horizon = 3 });
            seeds.Add(envelope.GetProperty("base_seed").GetInt64());
        }

        Assert.True(seeds.Distinct().Count() > 1);
    }

    [Fact]
    public async Task Entry_order_does_not_change_the_envelope()
    {
        using var app = await RealAppAsync();
        using var client = app.CreateClient();

        object[] entries =
        [
            new { card = new { name = "Swamp" }, quantity = 40 },
            new { card = new { name = "Pithing Needle" }, quantity = 10 },
            new { card = new { name = "Dark Ritual" }, quantity = 4 },
            new { card = new { name = SimulateXmageEndpointTests.Missing }, quantity = 5 },
        ];
        object[] split =
        [
            new { card = new { name = "Dark Ritual" }, quantity = 1 },
            new { card = new { name = "Swamp" }, quantity = 25 },
            new { card = new { name = SimulateXmageEndpointTests.Missing }, quantity = 5 },
            new { card = new { name = "Pithing Needle" }, quantity = 10 },
            new { card = new { name = "Dark Ritual" }, quantity = 3 },
            new { card = new { name = "Swamp" }, quantity = 15 },
        ];
        var random = new Random(175);
        object[][] orders =
        [
            [.. entries.Reverse()],
            [entries[1], entries[2], entries[3], entries[0]],
            [.. entries.OrderBy(_ => random.Next())],
            split,
        ];

        var expected = await SimulateAsync(client, Request(entries));
        foreach (var order in orders)
        {
            var actual = await SimulateAsync(client, Request(order));
            Assert.Equal(expected.GetRawText(), actual.GetRawText());
        }

        static object Request(object[] entries)
            => new
            {
                decklist = new { commanders = new[] { new { name = "Sheoldred, the Apocalypse" } }, entries },
                goal = NeedleByThree,
                runs = 20,
                seed = 175,
                turn_horizon = 3,
            };
    }

    /// <summary>On the fake sidecar: commander order changes
    /// neither the envelope nor the deck the sidecar loads. The real-card
    /// seed has one commander XMage knows, so the real-image test cannot
    /// cover this.</summary>
    [Fact]
    public async Task Commander_order_does_not_change_the_envelope_or_the_loaded_deck()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var forward = await SimulateAsync(client, Request("Test Commander", "Test Partner"));
        var reverse = await SimulateAsync(client, Request("Test Partner", "Test Commander"));

        Assert.Equal(2, forward.GetProperty("input").GetProperty("commanders").GetArrayLength());
        Assert.Equal(forward.GetRawText(), reverse.GetRawText());
        var decks = fake.Received.Where(m => (string?)m["type"] == "load_deck").Select(m => m.ToJsonString()).ToList();
        Assert.Equal(2, decks.Count);
        Assert.Equal(decks[0], decks[1]);

        static object Request(params string[] commanders)
            => new
            {
                decklist = new
                {
                    commanders = commanders.Select(name => new { name }),
                    entries = new[]
                    {
                        new { card = new { name = "Test Swamp" }, quantity = 30 },
                        new { card = new { name = "Pithing Needle" }, quantity = 4 },
                        new { card = new { name = "Test Bear" }, quantity = 5 },
                    },
                },
                goal = NeedleByThree,
                runs = 20,
                seed = 175,
            };
    }

    [Fact]
    public async Task Runs_default_to_a_thousand_and_the_horizon_to_ten()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var defaultRuns = await SimulateAsync(client, new { decklist = Deck(), goal = NeedleByThree, seed = 3, turn_horizon = 3 });
        var defaultHorizon = await SimulateAsync(client, new { decklist = Deck(), goal = NeedleByThree, runs = 5, seed = 3 });

        Assert.Equal(1000, defaultRuns.GetProperty("runs").GetInt32());
        Assert.Equal(10, defaultHorizon.GetProperty("turn_horizon").GetInt32());
    }

    [Fact]
    public async Task An_all_of_goal_over_two_cards_on_different_turns_runs()
    {
        using var app = await RealAppAsync();
        using var client = app.CreateClient();

        var envelope = await SimulateAsync(client, new
        {
            decklist = SimulateXmageEndpointTests.Decklist(),
            goal = new
            {
                kind = "all_of",
                goals = new object[]
                {
                    new { kind = "on_battlefield", card = "Pithing Needle", by_turn = 2 },
                    new { kind = "cast", card = "Dark Ritual", by_turn = 3 },
                },
            },
            runs = 20,
            seed = 11,
            turn_horizon = 3,
        });

        var goal = envelope.GetProperty("goal");
        Assert.Equal("all_of", goal.GetProperty("kind").GetString());
        Assert.Equal(2, goal.GetProperty("goals").GetArrayLength());
        Assert.Matches(FloorLabel, envelope.GetProperty("probability").GetProperty("label").GetString()!);
    }

    [Fact]
    public async Task Runs_and_horizon_outside_their_bounds_are_problems()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var above = await client.PostAsJsonAsync("/simulate", new { decklist = Deck(), goal = NeedleByThree, runs = 10_001 });
        Assert.Equal(HttpStatusCode.BadRequest, above.StatusCode);
        Assert.Equal("application/problem+json", above.Content.Headers.ContentType?.MediaType);
        var problem = await above.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("urn:mtg-archive:problem:parameter-above-maximum", problem.GetProperty("type").GetString());
        Assert.Equal("runs", problem.GetProperty("parameter").GetString());
        Assert.Equal(10_000, problem.GetProperty("maximum").GetInt32());

        var below = await client.PostAsJsonAsync("/simulate", new { decklist = Deck(), goal = NeedleByThree, runs = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, below.StatusCode);
        Assert.Equal("runs", (await below.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("parameter").GetString());

        var horizon = await client.PostAsJsonAsync("/simulate", new { decklist = Deck(), goal = NeedleByThree, turn_horizon = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, horizon.StatusCode);
        Assert.Equal("turn_horizon", (await horizon.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("parameter").GetString());

        var farHorizon = await ProblemAsync(client, new { decklist = Deck(), goal = NeedleByThree, turn_horizon = 31 });
        Assert.Equal("urn:mtg-archive:problem:parameter-above-maximum", farHorizon.GetProperty("type").GetString());
        Assert.Equal("turn_horizon", farHorizon.GetProperty("parameter").GetString());
        Assert.Equal(30, farHorizon.GetProperty("maximum").GetInt32());
    }

    [Fact]
    public async Task Bad_goals_are_problems_naming_each_error()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var unknownKind = await ProblemAsync(client, new { decklist = Deck(), goal = new { kind = "in_hand", card = "Pithing Needle", by_turn = 2 } });
        Assert.Equal("urn:mtg-archive:problem:invalid-goal", unknownKind.GetProperty("type").GetString());
        Assert.Contains("on_battlefield", unknownKind.GetProperty("detail").GetString());
        Assert.NotEmpty(unknownKind.GetProperty("errors").EnumerateArray());

        var outsideDeck = await ProblemAsync(client, new { decklist = Deck(), goal = new { kind = "cast", card = "Sol Ring", by_turn = 2 } });
        Assert.Equal("urn:mtg-archive:problem:invalid-goal", outsideDeck.GetProperty("type").GetString());
        Assert.Contains("Sol Ring", Assert.Single(outsideDeck.GetProperty("errors").EnumerateArray()).GetString());

        var afterHorizon = await ProblemAsync(client, new { decklist = Deck(), goal = NeedleByThree, turn_horizon = 2 });
        Assert.Equal("urn:mtg-archive:problem:invalid-goal", afterHorizon.GetProperty("type").GetString());
        Assert.Contains("horizon", Assert.Single(afterHorizon.GetProperty("errors").EnumerateArray()).GetString());

        var missing = await client.PostAsJsonAsync("/simulate", new { decklist = Deck() });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_goal_names_a_playable_face_and_any_other_face_is_a_problem()
    {
        var connectionString = await SeedCorpusAsync(banlist: false);
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            db.Cards.AddRange(
                MultiFace("modal_dfc", ("Esika, God of the Tree", "Legendary Creature — God", "{1}{G}{G}"), ("The Prismatic Bridge", "Legendary Enchantment", "{W}{U}{B}{R}{G}")),
                MultiFace("transform", ("Delver of Secrets", "Creature — Human Wizard", "{U}"), ("Insectile Aberration", "Creature — Human Insect", null)),
                MultiFace("split", ("Fire", "Instant", "{1}{R}"), ("Ice", "Instant", "{1}{U}")),
                MultiFace("adventure", ("Bonecrusher Giant", "Creature — Giant", "{2}{R}"), ("Stomp", "Instant — Adventure", "{1}{R}")));
            await db.SaveChangesAsync();
        }

        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();
        var deck = new
        {
            commanders = Array.Empty<object>(),
            entries = new[] { "Test Swamp", "Esika, God of the Tree // The Prismatic Bridge", "Delver of Secrets // Insectile Aberration", "Fire // Ice", "Bonecrusher Giant // Stomp" }
                .Select(name => new { card = new { name }, quantity = name == "Test Swamp" ? 30 : 1 }),
        };

        foreach (var face in new[] { "The Prismatic Bridge", "Esika, God of the Tree", "Fire", "Esika, God of the Tree // The Prismatic Bridge" })
        {
            var envelope = await SimulateAsync(client, new { decklist = deck, goal = new { kind = "on_battlefield", card = face, by_turn = 3 }, runs = 5, seed = 1 });
            Assert.Equal(face, envelope.GetProperty("goal").GetProperty("card").GetString());
        }

        foreach (var (face, layout) in new[] { ("Insectile Aberration", "transform"), ("Ice", "split"), ("Stomp", "adventure") })
        {
            var problem = await ProblemAsync(client, new { decklist = deck, goal = new { kind = "cast", card = face, by_turn = 3 } });
            Assert.Equal("urn:mtg-archive:problem:invalid-goal", problem.GetProperty("type").GetString());
            var error = Assert.Single(problem.GetProperty("errors").EnumerateArray()).GetString();
            Assert.Contains($"\"{face}\"", error);
            Assert.Contains(layout, error);
        }
    }

    [Fact]
    public async Task Unknown_version_pins_are_problems_never_a_fallback()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var banlist = await ProblemAsync(client, new { decklist = Deck(), goal = NeedleByThree, banlist_version = "1999-01-01" });
        Assert.Equal("urn:mtg-archive:problem:unknown-version", banlist.GetProperty("type").GetString());
        Assert.Equal("banlist_version", banlist.GetProperty("parameter").GetString());
    }

    [Fact]
    public async Task A_request_that_still_sends_staple_cut_version_is_accepted_and_the_versions_block_has_no_staple_axis()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var envelope = await SimulateAsync(client, new { decklist = Deck(), goal = NeedleByThree, runs = 5, seed = 1, staple_cut_version = "2025-01-01.1" });

        Assert.False(envelope.GetProperty("versions").TryGetProperty("staple_cut_version", out _));
        Assert.False(envelope.TryGetProperty("dsl_coverage", out _));
    }

    [Fact]
    public async Task A_disruption_profile_switches_to_paired_runs_and_the_tier_3_envelope_extends_the_tier_2_one()
    {
        using var app = await RealAppAsync();
        using var client = app.CreateClient();

        var profile = new object[] { new { kind = "counterspell", turn = 2 }, new { kind = "targeted_removal", turn = 3 } };
        var tier2 = await SimulateAsync(client, new { decklist = SimulateXmageEndpointTests.Decklist(), goal = NeedleByThree, runs = 40, seed = 63, turn_horizon = 3 });
        var tier3 = await SimulateAsync(client, new { decklist = SimulateXmageEndpointTests.Decklist(), goal = NeedleByThree, runs = 40, seed = 63, turn_horizon = 3, disruption_profile = profile });

        Assert.False(tier2.TryGetProperty("disruption_profile", out _));
        Assert.False(tier2.TryGetProperty("disruption", out _));
        var echoed = tier3.GetProperty("disruption_profile").EnumerateArray().ToList();
        Assert.Equal(["counterspell", "targeted_removal"], echoed.Select(e => e.GetProperty("kind").GetString()));
        Assert.Equal([2, 3], echoed.Select(e => e.GetProperty("turn").GetInt32()));

        // The tier-2 fields describe the baseline arm.
        foreach (var member in new[] { "goal", "probability", "runs", "successes", "base_seed", "turn_horizon", "policies", "curve", "exemplars", "input", "simplifications", "versions" })
        {
            Assert.Equal(tier2.GetProperty(member).GetRawText(), tier3.GetProperty(member).GetRawText());
        }

        var disruption = tier3.GetProperty("disruption");
        var baseline = disruption.GetProperty("baseline");
        var disrupted = disruption.GetProperty("disrupted");
        Assert.Equal(tier2.GetProperty("probability").GetRawText(), baseline.GetProperty("probability").GetRawText());
        Assert.Equal(tier2.GetProperty("curve").GetRawText(), baseline.GetProperty("curve").GetRawText());
        foreach (var arm in new[] { baseline, disrupted })
        {
            var probability = arm.GetProperty("probability");
            Assert.Matches(FloorLabel, probability.GetProperty("label").GetString()!);
            Assert.Equal(0.95, probability.GetProperty("confidence_interval").GetProperty("level").GetDouble());
            Assert.Equal(3, arm.GetProperty("curve").GetArrayLength());
            Assert.Equal(arm.GetProperty("successes").GetInt32() / 40.0, probability.GetProperty("floor").GetDouble());
        }

        var baselineFloor = baseline.GetProperty("probability").GetProperty("floor").GetDouble();
        var disruptedFloor = disrupted.GetProperty("probability").GetProperty("floor").GetDouble();
        var delta = disruption.GetProperty("resilience_delta").GetDouble();
        Assert.True(delta >= 0);
        Assert.Equal(baselineFloor - disruptedFloor, delta);

        // A Needle cast on turn 1 or 2 is countered.
        var events = disruption.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(["counterspell", "targeted_removal"], events.Select(e => e.GetProperty("kind").GetString()));
        Assert.True(events[0].GetProperty("fired_rate").GetDouble() > 0);
        Assert.False(events[0].GetProperty("unused").GetBoolean());

        Assert.Collection(disruption.GetProperty("assumptions").EnumerateArray().Select(a => a.GetString()!),
            a => Assert.StartsWith("No protection", a),
            a => Assert.StartsWith("Blind pilot", a),
            a => Assert.StartsWith("Adversarial targeting", a),
            a => Assert.StartsWith("No indestructible", a));

        var disruptedExemplars = disrupted.GetProperty("exemplars").EnumerateArray().ToList();
        Assert.InRange(disruptedExemplars.Count, 1, 3);
        foreach (var exemplar in disruptedExemplars)
        {
            Assert.Equal(exemplar.GetProperty("seed").GetInt32(), RunSeedOf(tier3, exemplar));
            Assert.True(exemplar.TryGetProperty("events", out _));
        }

        foreach (var exemplar in tier3.GetProperty("exemplars").EnumerateArray())
        {
            Assert.False(exemplar.TryGetProperty("events", out _));
        }

        var empty = await SimulateAsync(client, new { decklist = SimulateXmageEndpointTests.Decklist(), goal = NeedleByThree, runs = 10, seed = 63, turn_horizon = 3, disruption_profile = Array.Empty<object>() });
        Assert.Empty(empty.GetProperty("disruption_profile").EnumerateArray());
        Assert.Equal(0, empty.GetProperty("disruption").GetProperty("resilience_delta").GetDouble());
        Assert.Empty(empty.GetProperty("disruption").GetProperty("events").EnumerateArray());
        Assert.Equal(
            empty.GetProperty("disruption").GetProperty("baseline").GetProperty("curve").GetRawText(),
            empty.GetProperty("disruption").GetProperty("disrupted").GetProperty("curve").GetRawText());
    }

    /// <summary>The seed the envelope's base seed and the exemplar's run index derive to.</summary>
    private static int RunSeedOf(JsonElement envelope, JsonElement exemplar)
        => RunSeeds.Derive(envelope.GetProperty("base_seed").GetInt64(), exemplar.GetProperty("run_index").GetInt32());

    [Fact]
    public async Task A_bad_disruption_profile_is_a_request_error_never_a_warning()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var unknownKind = await ProblemAsync(client, new
        {
            decklist = Deck(),
            goal = NeedleByThree,
            disruption_profile = new[] { new { kind = "discard", turn = 2 } },
        });
        Assert.Equal("urn:mtg-archive:problem:invalid-disruption-profile", unknownKind.GetProperty("type").GetString());
        Assert.Contains("counterspell, targeted_removal, board_wipe", unknownKind.GetProperty("detail").GetString());
        Assert.Contains("discard", Assert.Single(unknownKind.GetProperty("errors").EnumerateArray()).GetString());

        var badTurn = await ProblemAsync(client, new
        {
            decklist = Deck(),
            goal = NeedleByThree,
            disruption_profile = new[] { new { kind = "board_wipe", turn = 0 } },
        });
        Assert.Equal("urn:mtg-archive:problem:invalid-disruption-profile", badTurn.GetProperty("type").GetString());

        var tooMany = await ProblemAsync(client, new
        {
            decklist = Deck(),
            goal = NeedleByThree,
            disruption_profile = Enumerable.Repeat(new { kind = "counterspell", turn = 1 }, 101).ToArray(),
        });
        Assert.Equal("urn:mtg-archive:problem:parameter-above-maximum", tooMany.GetProperty("type").GetString());
        Assert.Equal("disruption_profile", tooMany.GetProperty("parameter").GetString());
        Assert.Equal(100, tooMany.GetProperty("maximum").GetInt32());
    }

    /// <summary>A mode needs a profile, and the modes are a
    /// closed list.</summary>
    [Fact]
    public async Task A_bad_disruption_mode_is_a_request_error()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var alone = await ProblemAsync(client, new { decklist = Deck(), goal = NeedleByThree, disruption_mode = "interactive" });
        Assert.Equal("urn:mtg-archive:problem:invalid-disruption-mode", alone.GetProperty("type").GetString());
        Assert.Contains("disruption_profile", alone.GetProperty("detail").GetString());
        Assert.False(alone.TryGetProperty("allowed", out _));

        var abstractedAlone = await ProblemAsync(client, new { decklist = Deck(), goal = NeedleByThree, disruption_mode = "abstracted" });
        Assert.Equal("urn:mtg-archive:problem:invalid-disruption-mode", abstractedAlone.GetProperty("type").GetString());

        var unknown = await ProblemAsync(client, new
        {
            decklist = Deck(),
            goal = NeedleByThree,
            disruption_profile = Array.Empty<object>(),
            disruption_mode = "scripted",
        });
        Assert.Equal("urn:mtg-archive:problem:invalid-disruption-mode", unknown.GetProperty("type").GetString());
        Assert.Equal(["abstracted", "interactive"], unknown.GetProperty("allowed").EnumerateArray().Select(e => e.GetString()));

        Assert.DoesNotContain(fake.Received, m => (string?)m["type"] == "start_game");
    }

    /// <summary>The hold policy needs the interactive mode, and
    /// the policies are a closed list.</summary>
    [Fact]
    public async Task A_bad_play_policy_is_a_request_error()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var tier2 = await ProblemAsync(client, new { decklist = Deck(), goal = NeedleByThree, play_policy = "goal-aware-hold" });
        Assert.Equal("urn:mtg-archive:problem:invalid-play-policy", tier2.GetProperty("type").GetString());
        Assert.Contains("interactive", tier2.GetProperty("detail").GetString());

        var abstracted = await ProblemAsync(client, new
        {
            decklist = Deck(),
            goal = NeedleByThree,
            disruption_profile = Array.Empty<object>(),
            play_policy = "goal-aware-hold",
        });
        Assert.Equal("urn:mtg-archive:problem:invalid-play-policy", abstracted.GetProperty("type").GetString());

        var unknown = await ProblemAsync(client, new
        {
            decklist = Deck(),
            goal = NeedleByThree,
            disruption_profile = Array.Empty<object>(),
            disruption_mode = "interactive",
            play_policy = "goal-aware-patient",
        });
        Assert.Equal("urn:mtg-archive:problem:invalid-play-policy", unknown.GetProperty("type").GetString());
        Assert.Equal(["goal-aware-greedy", "goal-aware-hold"], unknown.GetProperty("allowed").EnumerateArray().Select(e => e.GetString()));

        Assert.DoesNotContain(fake.Received, m => (string?)m["type"] == "start_game");
    }

    /// <summary>Every event kind plays in interactive mode, so
    /// no interactive profile is a 501. A profile with every kind seats the
    /// scripted opponent with the whole card table.</summary>
    [Fact]
    public async Task Every_interactive_event_kind_plays_and_none_is_a_501()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var all = await client.PostAsJsonAsync("/simulate", new
        {
            decklist = Deck(),
            goal = NeedleByThree,
            runs = 2,
            disruption_profile = new[] { new { kind = "board_wipe", turn = 4 }, new { kind = "counterspell", turn = 2 }, new { kind = "board_wipe", turn = 5 } },
            disruption_mode = "interactive",
        });
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.Contains(fake.Received, m => (string?)m["type"] == "start_game"
            && m["opponent"]?.ToJsonString() == """{"cards":["Counterspell","Atomize","Rout"]}""");

        foreach (var kind in new[] { "counterspell", "targeted_removal", "board_wipe" })
        {
            var supported = await client.PostAsJsonAsync("/simulate", new
            {
                decklist = Deck(),
                goal = NeedleByThree,
                runs = 2,
                disruption_profile = new[] { new { kind, turn = 2 } },
                disruption_mode = "interactive",
            });
            Assert.Equal(HttpStatusCode.OK, supported.StatusCode);
        }

        var abstracted = await client.PostAsJsonAsync("/simulate", new
        {
            decklist = Deck(),
            goal = NeedleByThree,
            runs = 2,
            disruption_profile = new[] { new { kind = "board_wipe", turn = 4 } },
            disruption_mode = "abstracted",
        });
        Assert.Equal(HttpStatusCode.OK, abstracted.StatusCode);
    }

    /// <summary>The tracer bullet: the scripted opponent with
    /// nothing due plays exactly as the idle opponent, so an interactive
    /// request with an empty profile gives a delta of 0 and equal curves,
    /// and its baseline arm is the abstracted request's, byte for byte.</summary>
    [Fact]
    public async Task An_interactive_request_with_an_empty_profile_moves_no_number()
    {
        using var app = await RealAppAsync();
        using var client = app.CreateClient();
        object Request(string mode) => new
        {
            decklist = SimulateXmageEndpointTests.Decklist(),
            goal = NeedleByThree,
            runs = 40,
            seed = 63,
            turn_horizon = 3,
            disruption_profile = Array.Empty<object>(),
            disruption_mode = mode,
        };

        var abstracted = await SimulateAsync(client, Request("abstracted"));
        var interactive = await SimulateAsync(client, Request("interactive"));

        Assert.Equal("abstracted", abstracted.GetProperty("disruption_mode").GetString());
        Assert.Equal("interactive", interactive.GetProperty("disruption_mode").GetString());
        var disruption = interactive.GetProperty("disruption");
        Assert.Equal(0, disruption.GetProperty("resilience_delta").GetDouble());
        Assert.Equal(disruption.GetProperty("baseline").GetProperty("curve").GetRawText(), disruption.GetProperty("disrupted").GetProperty("curve").GetRawText());
        Assert.Equal(disruption.GetProperty("baseline").GetProperty("successes").GetInt32(), disruption.GetProperty("disrupted").GetProperty("successes").GetInt32());
        Assert.Equal(abstracted.GetProperty("disruption").GetProperty("baseline").GetRawText(), disruption.GetProperty("baseline").GetRawText());
        Assert.Equal(abstracted.GetProperty("disruption").GetProperty("disrupted").GetRawText(), disruption.GetProperty("disrupted").GetRawText());
        Assert.Equal(0, interactive.GetProperty("engine_errors").GetInt32());
    }

    /// <summary>On the real sidecar: the scripted opponent casts
    /// Counterspell. The baseline arm is the abstracted request's, byte for
    /// byte; the interactive envelope carries the fired and not-cast rates,
    /// exemplar hits, the interactive assumptions and the Scripted opponent
    /// line; and a replay is byte-equal.</summary>
    [Fact]
    public async Task An_interactive_counterspell_casts_a_real_counterspell_and_replays_byte_equal()
    {
        using var app = await RealAppAsync();
        using var client = app.CreateClient();
        object Request(string mode) => new
        {
            decklist = SimulateXmageEndpointTests.Decklist(),
            goal = NeedleByThree,
            runs = 40,
            seed = 63,
            turn_horizon = 3,
            disruption_profile = new[] { new { kind = "counterspell", turn = 1 } },
            disruption_mode = mode,
        };

        var abstracted = await SimulateAsync(client, Request("abstracted"));
        var first = await client.PostAsJsonAsync("/simulate", Request("interactive"));
        var second = await client.PostAsJsonAsync("/simulate", Request("interactive"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var text = await first.Content.ReadAsStringAsync();
        Assert.Equal(text, await second.Content.ReadAsStringAsync());
        var interactive = JsonDocument.Parse(text).RootElement;
        Assert.Equal("interactive", interactive.GetProperty("disruption_mode").GetString());
        Assert.Equal(0, interactive.GetProperty("engine_errors").GetInt32());
        var disruption = interactive.GetProperty("disruption");
        Assert.Equal(abstracted.GetProperty("disruption").GetProperty("baseline").GetRawText(), disruption.GetProperty("baseline").GetRawText());
        foreach (var member in new[] { "goal", "probability", "runs", "successes", "base_seed", "turn_horizon", "policies", "curve", "exemplars", "input", "versions" })
        {
            Assert.Equal(abstracted.GetProperty(member).GetRawText(), interactive.GetProperty(member).GetRawText());
        }

        var @event = Assert.Single(disruption.GetProperty("events").EnumerateArray());
        var fired = @event.GetProperty("fired_rate").GetDouble();
        var notCast = @event.GetProperty("not_cast_rate").GetDouble();
        Assert.True(fired > 0, "the opponent never cast Counterspell");
        Assert.True(fired + notCast <= 1);
        Assert.False(abstracted.GetProperty("disruption").GetProperty("events")[0].TryGetProperty("not_cast_rate", out _));

        Assert.Equal(DisruptionProfile.InteractiveAssumptions, disruption.GetProperty("assumptions").EnumerateArray().Select(a => a.GetString()!));
        Assert.Equal(DisruptionProfile.Assumptions, abstracted.GetProperty("disruption").GetProperty("assumptions").EnumerateArray().Select(a => a.GetString()!));
        Assert.Equal(XmageEngine.SimplificationsFor(GameRules.Commander, DisruptionMode.Interactive), interactive.GetProperty("simplifications").EnumerateArray().Select(e => e.GetString()!));
        Assert.StartsWith("Scripted opponent:", interactive.GetProperty("simplifications")[0].GetString());
        Assert.Equal(XmageEngine.SimplificationsFor(GameRules.Commander), abstracted.GetProperty("simplifications").EnumerateArray().Select(e => e.GetString()!));

        var hits = disruption.GetProperty("disrupted").GetProperty("exemplars").EnumerateArray()
            .SelectMany(e => e.GetProperty("events").EnumerateArray()).ToList();
        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.True(h.GetProperty("hit").GetBoolean()));
        Assert.All(abstracted.GetProperty("disruption").GetProperty("disrupted").GetProperty("exemplars").EnumerateArray()
            .SelectMany(e => e.GetProperty("events").EnumerateArray()), h => Assert.False(h.TryGetProperty("hit", out _)));
        var log = disruption.GetProperty("disrupted").GetProperty("exemplars").EnumerateArray()
            .SelectMany(e => e.GetProperty("log").EnumerateArray()).Select(l => l.GetProperty("message").GetString()).ToList();
        Assert.Contains("Disruption: counterspell casts Counterspell on Pithing Needle; it hits.", log);
    }

    /// <summary>On the real sidecar: the default profile plus a
    /// board wipe on the removal's turn. The scripted opponent casts Rout
    /// at our end step after Atomize, in profile order. The envelope has
    /// fired, answered and not-cast rates for all three events, Rout hits
    /// Sheoldred in some run, and a replay is byte-equal.</summary>
    [Fact]
    public async Task An_interactive_default_profile_plus_a_board_wipe_rates_all_three_events_and_replays_byte_equal()
    {
        using var app = await RealAppAsync();
        using var client = app.CreateClient();
        var request = new
        {
            decklist = SimulateXmageEndpointTests.Decklist(),
            goal = NeedleByThree,
            runs = 40,
            seed = 63,
            turn_horizon = 5,
            disruption_profile = new[] { new { kind = "counterspell", turn = 3 }, new { kind = "targeted_removal", turn = 4 }, new { kind = "board_wipe", turn = 4 } },
            disruption_mode = "interactive",
        };

        var first = await client.PostAsJsonAsync("/simulate", request);
        var second = await client.PostAsJsonAsync("/simulate", request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var text = await first.Content.ReadAsStringAsync();
        Assert.Equal(text, await second.Content.ReadAsStringAsync());
        var envelope = JsonDocument.Parse(text).RootElement;
        Assert.Equal(0, envelope.GetProperty("engine_errors").GetInt32());
        var events = envelope.GetProperty("disruption").GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(["counterspell", "targeted_removal", "board_wipe"], events.Select(e => e.GetProperty("kind").GetString()));
        foreach (var @event in events)
        {
            var fired = @event.GetProperty("fired_rate").GetDouble();
            Assert.InRange(@event.GetProperty("answered_rate").GetDouble(), 0, fired);
            Assert.InRange(fired + @event.GetProperty("not_cast_rate").GetDouble(), 0, 1);
        }

        Assert.True(events[2].GetProperty("fired_rate").GetDouble() > 0, "the opponent never cast Rout");
        var log = envelope.GetProperty("disruption").GetProperty("disrupted").GetProperty("exemplars").EnumerateArray()
            .SelectMany(e => e.GetProperty("log").EnumerateArray()).Select(l => l.GetProperty("message").GetString()!).ToList();
        Assert.Contains(log, l => l.StartsWith("Disruption: board_wipe casts Rout on ", StringComparison.Ordinal));
    }

    /// <summary>On the real sidecar: a deck of Islands, Needles
    /// and Counterspells answers the opponent's Counterspell in the
    /// response window. The baseline arms and the tier-2 members match the
    /// abstracted request byte for byte; the interactive envelope carries
    /// the answered rate, the response cards, the unprofiled responses and
    /// the exemplar answers; and a replay is byte-equal.</summary>
    [Fact]
    public async Task An_interactive_counterspell_is_answered_by_the_decks_counterspell()
    {
        using var app = SimulateXmageEndpointTests.XmageApp(
            await SeedCountersAsync(),
            SimulateXmageEndpointTests.Endpoint(await XmageSidecarContainer.OptionsAsync()));
        using var client = app.CreateClient();
        object Request(string mode) => new
        {
            decklist = CountersDecklist(),
            goal = new { kind = "on_battlefield", card = "Pithing Needle", by_turn = CountersHorizon },
            runs = 40,
            seed = 63,
            turn_horizon = CountersHorizon,
            disruption_profile = new[] { new { kind = "counterspell", turn = CountersEventTurn } },
            disruption_mode = mode,
        };

        var abstracted = await SimulateAsync(client, Request("abstracted"));
        var first = await client.PostAsJsonAsync("/simulate", Request("interactive"));
        var second = await client.PostAsJsonAsync("/simulate", Request("interactive"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var text = await first.Content.ReadAsStringAsync();
        Assert.Equal(text, await second.Content.ReadAsStringAsync());
        var interactive = JsonDocument.Parse(text).RootElement;
        Assert.Equal(0, interactive.GetProperty("engine_errors").GetInt32());
        Assert.Equal(0, abstracted.GetProperty("engine_errors").GetInt32());
        var disruption = interactive.GetProperty("disruption");
        Assert.Equal(abstracted.GetProperty("disruption").GetProperty("baseline").GetRawText(), disruption.GetProperty("baseline").GetRawText());
        foreach (var member in new[] { "goal", "probability", "runs", "successes", "base_seed", "turn_horizon", "policies", "curve", "exemplars", "input", "versions" })
        {
            Assert.Equal(abstracted.GetProperty(member).GetRawText(), interactive.GetProperty(member).GetRawText());
        }

        var @event = Assert.Single(disruption.GetProperty("events").EnumerateArray());
        var fired = @event.GetProperty("fired_rate").GetDouble();
        var answered = @event.GetProperty("answered_rate").GetDouble();
        Assert.True(answered > 0, "the deck never answered the opponent's Counterspell");
        Assert.True(answered <= fired);
        Assert.False(abstracted.GetProperty("disruption").GetProperty("events")[0].TryGetProperty("answered_rate", out _));

        var abstractedFloor = abstracted.GetProperty("disruption").GetProperty("disrupted").GetProperty("probability").GetProperty("floor").GetDouble();
        var interactiveFloor = disruption.GetProperty("disrupted").GetProperty("probability").GetProperty("floor").GetDouble();
        Assert.True(interactiveFloor > abstractedFloor, $"interactive {interactiveFloor} is not above abstracted {abstractedFloor}");
        Assert.Equal(["Counterspell"], disruption.GetProperty("response_cards").EnumerateArray().Select(e => e.GetString()));
        Assert.Empty(interactive.GetProperty("engine_coverage").GetProperty("unprofiled_responses").EnumerateArray());
        Assert.False(abstracted.GetProperty("disruption").TryGetProperty("response_cards", out _));
        Assert.False(abstracted.GetProperty("engine_coverage").TryGetProperty("unprofiled_responses", out _));
        Assert.Equal(DisruptionProfile.InteractiveAssumptions, disruption.GetProperty("assumptions").EnumerateArray().Select(a => a.GetString()!));
        Assert.Equal(XmageEngine.SimplificationsFor(GameRules.Commander, DisruptionMode.Interactive), interactive.GetProperty("simplifications").EnumerateArray().Select(e => e.GetString()!));
        Assert.Contains(interactive.GetProperty("simplifications").EnumerateArray(), s => s.GetString()!.Contains("response window", StringComparison.Ordinal));

        var entries = disruption.GetProperty("disrupted").GetProperty("exemplars").EnumerateArray()
            .SelectMany(e => e.GetProperty("events").EnumerateArray()).ToList();
        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.True(e.TryGetProperty("hit", out _));
            Assert.True(e.TryGetProperty("answered_by", out _));
        });
        Assert.All(abstracted.GetProperty("disruption").GetProperty("disrupted").GetProperty("exemplars").EnumerateArray()
            .SelectMany(e => e.GetProperty("events").EnumerateArray()), e => Assert.False(e.TryGetProperty("answered_by", out _)));
    }

    /// <summary>On the real sidecar: the same seed with a
    /// targeted-removal event. The scripted opponent casts Atomize at our
    /// end step, and the deck's Counterspell answers it in the response
    /// window. The baseline arms and the tier-2 members match the
    /// abstracted request byte for byte, the answered rate is above zero
    /// and at most the fired rate, the interactive floor is above the
    /// abstracted one, and a replay is byte-equal.</summary>
    [Fact]
    public async Task An_interactive_removal_is_answered_by_the_decks_counterspell()
    {
        using var app = SimulateXmageEndpointTests.XmageApp(
            await SeedCountersAsync(),
            SimulateXmageEndpointTests.Endpoint(await XmageSidecarContainer.OptionsAsync()));
        using var client = app.CreateClient();
        object Request(string mode) => new
        {
            decklist = CountersDecklist(),
            goal = new { kind = "on_battlefield", card = "Pithing Needle", by_turn = CountersHorizon },
            runs = 40,
            seed = 63,
            turn_horizon = CountersHorizon,
            disruption_profile = new[] { new { kind = "targeted_removal", turn = CountersEventTurn } },
            disruption_mode = mode,
        };

        var abstracted = await SimulateAsync(client, Request("abstracted"));
        var first = await client.PostAsJsonAsync("/simulate", Request("interactive"));
        var second = await client.PostAsJsonAsync("/simulate", Request("interactive"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var text = await first.Content.ReadAsStringAsync();
        Assert.Equal(text, await second.Content.ReadAsStringAsync());
        var interactive = JsonDocument.Parse(text).RootElement;
        Assert.Equal(0, interactive.GetProperty("engine_errors").GetInt32());
        Assert.Equal(0, abstracted.GetProperty("engine_errors").GetInt32());
        Assert.Equal("interactive", interactive.GetProperty("disruption_mode").GetString());
        var disruption = interactive.GetProperty("disruption");
        Assert.Equal(abstracted.GetProperty("disruption").GetProperty("baseline").GetRawText(), disruption.GetProperty("baseline").GetRawText());
        foreach (var member in new[] { "goal", "probability", "runs", "successes", "base_seed", "turn_horizon", "policies", "curve", "exemplars", "input", "versions" })
        {
            Assert.Equal(abstracted.GetProperty(member).GetRawText(), interactive.GetProperty(member).GetRawText());
        }

        var @event = Assert.Single(disruption.GetProperty("events").EnumerateArray());
        Assert.Equal("targeted_removal", @event.GetProperty("kind").GetString());
        var fired = @event.GetProperty("fired_rate").GetDouble();
        var answered = @event.GetProperty("answered_rate").GetDouble();
        Assert.True(answered > 0, "the deck never answered the opponent's Atomize");
        Assert.True(answered <= fired);
        Assert.True(fired + @event.GetProperty("not_cast_rate").GetDouble() <= 1);

        var abstractedFloor = abstracted.GetProperty("disruption").GetProperty("disrupted").GetProperty("probability").GetProperty("floor").GetDouble();
        var interactiveFloor = disruption.GetProperty("disrupted").GetProperty("probability").GetProperty("floor").GetDouble();
        Assert.True(interactiveFloor > abstractedFloor, $"interactive {interactiveFloor} is not above abstracted {abstractedFloor}");
        Assert.Equal(DisruptionProfile.InteractiveAssumptions, disruption.GetProperty("assumptions").EnumerateArray().Select(a => a.GetString()!));
        Assert.Equal(DisruptionProfile.Assumptions, abstracted.GetProperty("disruption").GetProperty("assumptions").EnumerateArray().Select(a => a.GetString()!));
        Assert.Equal(XmageEngine.SimplificationsFor(GameRules.Commander, DisruptionMode.Interactive), interactive.GetProperty("simplifications").EnumerateArray().Select(e => e.GetString()!));
        Assert.Equal(XmageEngine.SimplificationsFor(GameRules.Commander), abstracted.GetProperty("simplifications").EnumerateArray().Select(e => e.GetString()!));

        var entries = disruption.GetProperty("disrupted").GetProperty("exemplars").EnumerateArray()
            .SelectMany(e => e.GetProperty("events").EnumerateArray()).ToList();
        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.True(e.TryGetProperty("hit", out _));
            Assert.True(e.TryGetProperty("answered_by", out _));
        });
        var log = disruption.GetProperty("disrupted").GetProperty("exemplars").EnumerateArray()
            .SelectMany(e => e.GetProperty("log").EnumerateArray()).Select(l => l.GetProperty("message").GetString()!).ToList();
        Assert.Contains(log, l => l.StartsWith("Disruption: targeted_removal casts Atomize on ", StringComparison.Ordinal));
    }

    /// <summary>On the real sidecar, in Brawl at 25 life: the
    /// scripted opponent casts Atomize at our end step on Talrand, the goal
    /// permanent, and the deck's Snakeskin Veil answers it in the response
    /// window. The Islands make no green, so the Veil is cast only with an
    /// Elvish Spirit Guide's hand mana, which the main phase never spends on
    /// it. The goal is due on the event's turn, so abstracted removal fails
    /// every run. The answered rate is above zero and at most the fired
    /// rate, the interactive floor is above the abstracted one, the Veil is
    /// the deck's one response card, and a replay is byte-equal.</summary>
    [Fact]
    public async Task An_interactive_removal_in_brawl_is_answered_by_a_protection_card()
    {
        var connectionString = await SeedProtectionAsync();
        await SeedBrawlRowsAsync(connectionString, new DateTimeOffset(2026, 10, 4, 9, 5, 0, TimeSpan.Zero));
        using var app = SimulateXmageEndpointTests.XmageApp(
            connectionString,
            SimulateXmageEndpointTests.Endpoint(await XmageSidecarContainer.OptionsAsync()));
        using var client = app.CreateClient();
        object Request(string mode) => new
        {
            decklist = new
            {
                commanders = new[] { new { name = "Talrand, Sky Summoner" } },
                entries = new[]
                {
                    new { card = new { name = "Island" }, quantity = 79 },
                    new { card = new { name = "Elvish Spirit Guide" }, quantity = 10 },
                    new { card = new { name = "Snakeskin Veil" }, quantity = 10 },
                },
            },
            goal = new { kind = "on_battlefield", card = "Talrand, Sky Summoner", by_turn = CountersEventTurn },
            runs = 40,
            seed = 63,
            turn_horizon = CountersEventTurn,
            format = "brawl",
            disruption_profile = new[] { new { kind = "targeted_removal", turn = CountersEventTurn } },
            disruption_mode = mode,
        };

        var abstracted = await SimulateAsync(client, Request("abstracted"));
        var first = await client.PostAsJsonAsync("/simulate", Request("interactive"));
        var second = await client.PostAsJsonAsync("/simulate", Request("interactive"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var text = await first.Content.ReadAsStringAsync();
        Assert.Equal(text, await second.Content.ReadAsStringAsync());
        var envelope = JsonDocument.Parse(text).RootElement;
        Assert.Equal(0, envelope.GetProperty("engine_errors").GetInt32());
        Assert.Equal("brawl", envelope.GetProperty("versions").GetProperty("format").GetString());
        var disruption = envelope.GetProperty("disruption");
        var @event = Assert.Single(disruption.GetProperty("events").EnumerateArray());
        var fired = @event.GetProperty("fired_rate").GetDouble();
        var answered = @event.GetProperty("answered_rate").GetDouble();
        Assert.True(answered > 0, "the deck never answered the opponent's Atomize with Snakeskin Veil");
        Assert.True(answered <= fired);
        Assert.Equal(["Snakeskin Veil"], disruption.GetProperty("response_cards").EnumerateArray().Select(e => e.GetString()));

        // The goal is due on the event's turn: every answered run keeps
        // Talrand, and abstracted removal always takes him.
        var abstractedFloor = abstracted.GetProperty("disruption").GetProperty("disrupted").GetProperty("probability").GetProperty("floor").GetDouble();
        var interactiveFloor = disruption.GetProperty("disrupted").GetProperty("probability").GetProperty("floor").GetDouble();
        Assert.True(interactiveFloor > abstractedFloor, $"interactive {interactiveFloor} is not above abstracted {abstractedFloor}");

        var log = disruption.GetProperty("disrupted").GetProperty("exemplars").EnumerateArray()
            .SelectMany(e => e.GetProperty("log").EnumerateArray()).Select(l => l.GetProperty("message").GetString()!).ToList();
        Assert.StartsWith("END life=25", log.First(m => m.StartsWith("END life=", StringComparison.Ordinal)));
    }

    /// <summary>A fresh database of real cards: Islands, Talrand, Elvish
    /// Spirit Guide with its hand mana, and Snakeskin Veil with its target
    /// hexproof grant.</summary>
    private async Task<string> SeedProtectionAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync($"simulate_protection_{Guid.NewGuid():N}");
        await using var db = PostgresFixture.CreateContext(connectionString);
        await db.Database.MigrateAsync();

        var island = Card("Island", "Basic Land — Island", null, 0);
        island.Supertypes = ["Basic"];
        island.CardTypes = ["Land"];
        island.Subtypes = ["Island"];
        island.CopyLimit = null;
        island.ColorIdentity = ["U"];
        var guide = Card("Elvish Spirit Guide", "Creature — Elf Spirit", "{2}{G}", 3);
        guide.CardTypes = ["Creature"];
        guide.ColorIdentity = ["G"];
        var veil = Card("Snakeskin Veil", "Instant", "{G}", 1);
        veil.CardTypes = ["Instant"];
        veil.ColorIdentity = ["G"];
        var talrand = Commander("Talrand, Sky Summoner", "Legendary Creature — Drake Wizard", "{2}{U}{U}", 4);
        talrand.ColorIdentity = ["U"];
        db.Cards.AddRange(island, guide, veil, talrand);
        await db.SaveChangesAsync();

        db.SourceProfiles.Add(new SourceProfile { CardId = island.Id, ProducedColors = ["U"] });
        db.CardFeatureProfileRows.AddRange(
            new CardFeatureProfileRow { CardId = island.Id, FaceIndex = 0, Develops = true, TapMana = ["{U}"], HasActivatedAbility = true },
            new CardFeatureProfileRow { CardId = guide.Id, FaceIndex = 0, Develops = true, HandMana = ["{G}"], HasActivatedAbility = true },
            new CardFeatureProfileRow
            {
                CardId = veil.Id,
                FaceIndex = 0,
                ResponseGrants = [ProtectionGrant.Hexproof],
                ResponseGrantScopes = [ProtectionGrant.TargetCreature],
            },
            new CardFeatureProfileRow { CardId = talrand.Id, FaceIndex = 0 });
        await db.SaveChangesAsync();
        return connectionString;
    }

    private const int CountersHorizon = 6;

    private const int CountersEventTurn = 4;

    /// <summary>Talrand, 79 Islands, 10 Needles and 10 Counterspells.</summary>
    private static object CountersDecklist() => new
    {
        commanders = new[] { new { name = "Talrand, Sky Summoner" } },
        entries = new[]
        {
            new { card = new { name = "Island" }, quantity = 79 },
            new { card = new { name = "Pithing Needle" }, quantity = 10 },
            new { card = new { name = "Counterspell" }, quantity = 10 },
        },
    };

    /// <summary>A fresh database of real cards: Islands, the Needle,
    /// Counterspell with its counter feature row, and Talrand.</summary>
    private async Task<string> SeedCountersAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync($"simulate_counters_{Guid.NewGuid():N}");
        await using var db = PostgresFixture.CreateContext(connectionString);
        await db.Database.MigrateAsync();

        var island = Card("Island", "Basic Land — Island", null, 0);
        island.Supertypes = ["Basic"];
        island.CardTypes = ["Land"];
        island.Subtypes = ["Island"];
        island.CopyLimit = null;
        island.ColorIdentity = ["U"];
        var needle = Card("Pithing Needle", "Artifact", "{1}", 1);
        needle.ColorIdentity = [];
        var counterspell = Card("Counterspell", "Instant", "{U}{U}", 2);
        counterspell.CardTypes = ["Instant"];
        counterspell.ColorIdentity = ["U"];
        var talrand = Commander("Talrand, Sky Summoner", "Legendary Creature — Drake Wizard", "{2}{U}{U}", 4);
        talrand.ColorIdentity = ["U"];
        db.Cards.AddRange(island, needle, counterspell, talrand);
        await db.SaveChangesAsync();

        db.SourceProfiles.Add(new SourceProfile { CardId = island.Id, ProducedColors = ["U"] });
        db.CardFeatureProfileRows.AddRange(
            new CardFeatureProfileRow { CardId = island.Id, FaceIndex = 0, Develops = true, TapMana = ["{U}"], HasActivatedAbility = true },
            new CardFeatureProfileRow { CardId = needle.Id, FaceIndex = 0 },
            new CardFeatureProfileRow { CardId = counterspell.Id, FaceIndex = 0, ResponseCounterReach = CounterReach.Spell },
            new CardFeatureProfileRow { CardId = talrand.Id, FaceIndex = 0 });
        await db.SaveChangesAsync();
        return connectionString;
    }

    [Fact]
    public async Task Unresolvable_names_are_a_problem_with_the_unresolved_names_extension()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var problem = await ProblemAsync(client, new
        {
            decklist = new
            {
                entries = new[]
                {
                    new { card = new { name = "Pithing Needle" }, quantity = 1 },
                    new { card = new { name = "No Such Card" }, quantity = 1 },
                },
            },
            goal = NeedleByThree,
        });

        Assert.Equal("urn:mtg-archive:problem:unresolved-decklist", problem.GetProperty("type").GetString());
        Assert.Equal("No Such Card",
            Assert.Single(problem.GetProperty("unresolved_names").EnumerateArray()).GetProperty("name").GetString());
    }

    /// <summary>
    /// The format fixes the game rules (ADR 0012): the service sends them on
    /// start_game, and the envelope's simplifications state the start life,
    /// the draw rule, and the free mulligans. No format means Commander.
    /// </summary>
    [Theory]
    [InlineData(null, 40, true, true, 1)]
    [InlineData("brawl", 25, false, false, 1)]
    [InlineData("competitive_brawl", 25, false, false, 0)]
    [InlineData("standard_brawl", 25, false, false, 1)]
    public async Task The_format_fixes_the_game_rules_the_service_sends_and_the_envelope_states(
        string? format, int life, bool commanderDamage, bool draws, int freeMulligans)
    {
        var connectionString = await SeedCorpusAsync(banlist: false);
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var envelope = await SimulateAsync(client, new { decklist = Deck(), goal = NeedleByThree, runs = 2, seed = 1, format });

        var start = fake.Received.First(m => (string?)m["type"] == "start_game");
        Assert.Equal(
            (life, commanderDamage, draws, freeMulligans),
            ((int)start["start_life"]!, (bool)start["commander_damage"]!, (bool)start["starting_player_draws"]!, (int)start["free_mulligans"]!));
        Assert.All(fake.Received.Where(m => (string?)m["type"] == "start_game"), m => Assert.Equal(life, (int)m["start_life"]!));
        Assert.Equal(format ?? "commander", envelope.GetProperty("versions").GetProperty("format").GetString());

        var simplifications = envelope.GetProperty("simplifications").EnumerateArray().Select(s => s.GetString()!).ToList();
        Assert.Contains(simplifications, s => s.StartsWith("Idle opponent:", StringComparison.Ordinal) && s.Contains($"at {life} life", StringComparison.Ordinal));
        Assert.Contains(simplifications, s => s.Contains(
            draws ? "draws on turn 1 (CR 103.8c, multiplayer)" : "on the play: no turn-1 draw (CR 103.8a)",
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains(simplifications, s => s.StartsWith("Free mulligans: " + (freeMulligans == 0 ? "none" : "1"), StringComparison.Ordinal));
    }

    /// <summary>A Brawl run on the real sidecar: the shim plays 25 life on
    /// the play with no engine errors, and the envelope says so.</summary>
    [Fact]
    public async Task A_brawl_run_on_the_sidecar_plays_on_the_play_at_25_life()
    {
        using var app = await RealAppAsync();
        using var client = app.CreateClient();

        var envelope = await SimulateAsync(client, new
        {
            decklist = SimulateXmageEndpointTests.Decklist(),
            goal = NeedleByThree,
            runs = 20,
            seed = 7,
            turn_horizon = 3,
            format = "brawl",
        });

        Assert.Equal("brawl", envelope.GetProperty("versions").GetProperty("format").GetString());
        Assert.Equal(0, envelope.GetProperty("engine_errors").GetInt32());
        Assert.Equal(XmageEngine.SimplificationsFor(GameRules.Brawl), envelope.GetProperty("simplifications").EnumerateArray().Select(e => e.GetString()!));
        var log = envelope.GetProperty("exemplars").EnumerateArray().First().GetProperty("log").EnumerateArray()
            .Select(l => l.GetProperty("message").GetString()!).ToList();
        Assert.StartsWith("END life=25", log.First(m => m.StartsWith("END life=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_brawl_request_with_a_banlist_version_is_a_problem()
    {
        var connectionString = await SeedCorpusAsync();
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var problem = await ProblemAsync(client, new { decklist = Deck(), goal = NeedleByThree, format = "brawl", banlist_version = BanlistVersion });
        Assert.Equal("urn:mtg-archive:problem:unversioned-format", problem.GetProperty("type").GetString());

        var unknown = await ProblemAsync(client, new { decklist = Deck(), goal = NeedleByThree, format = "historic_brawl" });
        Assert.Equal("urn:mtg-archive:problem:unknown-format", unknown.GetProperty("type").GetString());
        Assert.Empty(fake.Received);
    }

    /// <summary>A Brawl verdict reads the format's rows, which have no
    /// banlist version: the commander banned in Commander is legal in
    /// Brawl, and the versions block dates the rows instead of pinning a
    /// banlist version.</summary>
    [Fact]
    public async Task A_brawl_verdict_reads_the_brawl_legality_and_echoes_its_as_of_date()
    {
        var asOf = new DateTimeOffset(2026, 10, 4, 9, 5, 0, TimeSpan.Zero);
        var connectionString = await SeedCorpusAsync();
        await SeedBrawlRowsAsync(connectionString, asOf);

        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        using var app = NewApp(connectionString, SimulateXmageEndpointTests.Endpoint(fake.Options()));
        using var client = app.CreateClient();

        var commander = await SimulateAsync(client, new { decklist = Deck(), goal = NeedleByThree, runs = 2, seed = 1 });
        var brawl = await SimulateAsync(client, new { decklist = Deck(), goal = NeedleByThree, runs = 2, seed = 1, format = "brawl" });

        Assert.Single(commander.GetProperty("deck_verdict").GetProperty("card_findings").EnumerateArray());
        Assert.Empty(brawl.GetProperty("deck_verdict").GetProperty("card_findings").EnumerateArray());
        var versions = brawl.GetProperty("versions");
        Assert.Equal(asOf, versions.GetProperty("legality_as_of").GetDateTimeOffset());
        Assert.False(versions.TryGetProperty("banlist_version", out _));
    }

    /// <summary>Every seeded card is legal in all three Brawl formats, as
    /// of <paramref name="asOf"/>; a card that cannot lead is not eligible
    /// as a commander.</summary>
    private static async Task SeedBrawlRowsAsync(string connectionString, DateTimeOffset asOf)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        foreach (var card in await db.Cards.ToListAsync())
        {
            foreach (var format in new[] { Format.Brawl, Format.CompetitiveBrawl, Format.StandardBrawl })
            {
                foreach (var role in new[] { Role.Commander, Role.Main })
                {
                    db.Legalities.Add(new Legality
                    {
                        CardId = card.Id,
                        Format = format,
                        BanlistVersionId = null,
                        Role = role,
                        Status = role == Role.Commander && !card.CanBeCommander ? LegalityStatus.NotEligible : LegalityStatus.Legal,
                        LegalityAsOf = asOf,
                    });
                }
            }
        }

        await db.SaveChangesAsync();
    }

    private static ArchiveApp NewApp(string connectionString, string endpoint)
        => new(autoMigrate: true, connectionString, settings: new Dictionary<string, string?>
        {
            [XmageOptions.EndpointSetting] = endpoint,
            [XmageOptions.ConnectTimeoutSetting] = "2",
        });

    /// <summary>A host on the real sidecar image over the real-card seed of <see cref="SimulateXmageEndpointTests"/>.</summary>
    private async Task<ArchiveApp> RealAppAsync()
        => SimulateXmageEndpointTests.XmageApp(
            await SimulateXmageEndpointTests.SeedAsync(postgres),
            SimulateXmageEndpointTests.Endpoint(await XmageSidecarContainer.OptionsAsync()));

    private static async Task<JsonElement> SimulateAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/simulate", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> ProblemAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/simulate", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>An illegal deck of 40: the banned commander, 30 basic
    /// Swamps, and more than one copy each of the Needle and the Bear.</summary>
    private static object Deck() => new
    {
        commanders = new[] { new { name = "Test Commander" } },
        entries = new[]
        {
            new { card = new { name = "Test Swamp" }, quantity = 30 },
            new { card = new { name = "Pithing Needle" }, quantity = 4 },
            new { card = new { name = "Test Bear" }, quantity = 5 },
        },
    };

    /// <summary>A legal Commander deck of 100: the unbanned commander, 97
    /// basic Swamps, one Needle, and one Bear.</summary>
    private static object LegalDeck() => new
    {
        commanders = new[] { new { name = "Test Partner" } },
        entries = new[]
        {
            new { card = new { name = "Test Swamp" }, quantity = 97 },
            new { card = new { name = "Pithing Needle" }, quantity = 1 },
            new { card = new { name = "Test Bear" }, quantity = 1 },
        },
    };

    /// <summary>
    /// A fresh database: a black basic land, the Needle with its card
    /// feature profile, a bear with no feature profile, and a commander
    /// banned at the one banlist version.
    /// </summary>
    private async Task<string> SeedCorpusAsync(bool banlist = true)
    {
        var connectionString = await postgres.CreateDatabaseAsync($"simulate_{Guid.NewGuid():N}");
        await using var db = PostgresFixture.CreateContext(connectionString);
        await db.Database.MigrateAsync();

        var swamp = Card("Test Swamp", "Basic Land — Swamp", null, 0);
        swamp.Supertypes = ["Basic"];
        swamp.CardTypes = ["Land"];
        swamp.Subtypes = ["Swamp"];
        swamp.CopyLimit = null;
        var needle = Card("Pithing Needle", "Artifact", "{1}", 1);
        var bear = Card("Test Bear", "Creature — Bear", "{1}{B}", 2);
        bear.CardTypes = ["Creature"];
        var commander = Commander("Test Commander", "Legendary Creature — Phyrexian", "{2}{B}{B}", 4);
        var partner = Commander("Test Partner", "Legendary Creature — Human", "{1}{B}", 2);
        db.Cards.AddRange(swamp, needle, bear, commander, partner);
        await db.SaveChangesAsync();

        db.SourceProfiles.Add(new SourceProfile { CardId = swamp.Id, ProducedColors = ["B"] });
        db.CardFeatureProfileRows.Add(new CardFeatureProfileRow { CardId = needle.Id, FaceIndex = 0 });
        await db.SaveChangesAsync();

        if (!banlist)
        {
            return connectionString;
        }

        var version = new BanlistVersion
        {
            EffectiveDate = DateOnly.Parse(BanlistVersion),
            DatePrecision = DatePrecision.Day,
            AnnouncementUrl = "https://example.org/2025",
        };
        db.BanlistVersions.Add(version);
        await db.SaveChangesAsync();
        var ban = new BanEvent { BanlistVersionId = version.Id, CardId = commander.Id, Action = BanAction.Ban, Scope = BanScope.All };
        db.BanEvents.Add(ban);
        await db.SaveChangesAsync();

        foreach (var card in new[] { swamp, needle, bear, commander, partner })
        {
            var banned = card == commander;
            foreach (var role in new[] { Role.Commander, Role.Main })
            {
                db.Legalities.Add(new Legality
                {
                    CardId = card.Id,
                    BanlistVersionId = version.Id,
                    Role = role,
                    Status = banned ? LegalityStatus.Banned
                        : role == Role.Commander && !card.CanBeCommander ? LegalityStatus.NotEligible
                        : LegalityStatus.Legal,
                    Reason = banned ? ban.Id : null,
                });
            }
        }

        await db.SaveChangesAsync();
        return connectionString;
    }

    private static Card MultiFace(string layout, params (string Name, string TypeLine, string? ManaCost)[] faces)
    {
        var card = Card(string.Join(" // ", faces.Select(f => f.Name)), string.Join(" // ", faces.Select(f => f.TypeLine)), null, 0);
        card.Layout = layout;
        card.Faces = [.. faces.Select((f, i) => new CardFace { FaceIndex = i, Name = f.Name, TypeLine = f.TypeLine, ManaCost = f.ManaCost })];
        return card;
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

    [Fact]
    public void The_parallelism_setting_is_unset_or_at_least_one()
    {
        static IConfiguration Config(string? value) => new ConfigurationBuilder()
            .AddInMemoryCollection([new(SimulateEndpoints.ParallelismSetting, value)])
            .Build();

        Assert.Null(SimulateEndpoints.ParallelismFrom(Config(null)));
        Assert.Equal(3, SimulateEndpoints.ParallelismFrom(Config("3")));
        Assert.Throws<InvalidOperationException>(() => SimulateEndpoints.ParallelismFrom(Config("0")));
    }
}
