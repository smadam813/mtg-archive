using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MtgArchive.Tools.EngineGate;

namespace MtgArchive.Tests;

/// <summary>
/// The host gates' console tool (spec §6.5): the CPU sample, the
/// full-horizon request, the report rows, and the throughput and coverage
/// commands against a stand-in service.
/// </summary>
public sealed class EngineGateTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Busy_is_the_share_of_time_not_idle()
        => Assert.Equal(0.25, HostCpu.Busy(new HostCpu.Times(100, 200), new HostCpu.Times(175, 300)));

    [Fact]
    public void Proc_stat_counts_idle_and_iowait_as_idle()
    {
        var times = HostCpu.ParseProcStat("cpu  10 0 20 60 10 0 0 0 0 0")!.Value;

        Assert.Equal(70UL, times.Idle);
        Assert.Equal(100UL, times.Total);
        Assert.Null(HostCpu.ParseProcStat("intr 1 2 3"));
    }

    [Fact]
    public void Full_horizon_sets_every_leaf_to_the_horizon_on_a_copy()
    {
        var request = JsonNode.Parse("""{"turn_horizon":10,"goal":{"kind":"all_of","goals":[{"kind":"cast","card":"A","by_turn":1},{"kind":"on_battlefield","card":"B","by_turn":3}]}}""")!.AsObject();

        var full = Requests.FullHorizon(request);

        Assert.Equal("""{"kind":"all_of","goals":[{"kind":"cast","card":"A","by_turn":10},{"kind":"on_battlefield","card":"B","by_turn":10}]}""", full["goal"]!.ToJsonString());
        Assert.Equal(1, (int)request["goal"]!["goals"]![0]!["by_turn"]!);
    }

    [Theory]
    [InlineData(119.9, 0, true, "pass")]
    [InlineData(120.0, 0, true, "fail")]
    [InlineData(60.0, 101, true, "fail")]
    [InlineData(300.0, 0, false, "info")]
    public void The_gate_row_passes_under_120_seconds_and_one_percent_engine_errors(double seconds, int errors, bool gate, string verdict)
        => Assert.Equal(verdict, new ThroughputRow(Noon, "xmage", "v", "r", 10_000, 10, seconds, errors, ">= 1%, greedy policy", null, gate).Verdict);

    [Fact]
    public void A_throughput_row_is_one_table_line()
    {
        var row = new ThroughputRow(Noon, "xmage", "xmage-1.4.62V1+0123abcd", "rerun-request.json", 10_000, 10, 80, 0, ">= 21.7%, greedy policy", 0.04, true);

        Assert.Equal("| 2026-09-27 12:00 | xmage xmage-1.4.62V1+0123abcd | rerun-request.json | 10000 | 10 | 80.0 | 125 | 0 | >= 21.7%, greedy policy | 4% | pass |", row.Markdown());
    }

    [Fact]
    public void A_paired_row_counts_its_runs_as_pairs_and_adds_games_per_second()
    {
        var row = new ThroughputRow(Noon, "xmage", "xmage-1.4.62V1+0123abcd", "tier-3-request.json", 10_000, 10, 80, 0, ">= 21.7%, greedy policy", 0.04, true, ">= 0.6%, greedy policy");

        Assert.Equal("| 2026-09-27 12:00 | xmage xmage-1.4.62V1+0123abcd | tier-3-request.json | 10000 pairs | 10 | 80.0 | 125 pairs/s, 250 games/s | 0 | baseline >= 21.7%, greedy policy; disrupted >= 0.6%, greedy policy | 4% | pass |", row.Markdown());
    }

    /// <summary>A paired request plays two games per pair, so its 1% bar counts games.</summary>
    [Theory]
    [InlineData(200, "pass")]
    [InlineData(201, "fail")]
    public void A_paired_gate_row_allows_one_percent_of_its_games_as_engine_errors(int errors, string verdict)
        => Assert.Equal(verdict, new ThroughputRow(Noon, "xmage", "v", "r", 10_000, 10, 60, errors, ">= 1%, greedy policy", null, true, ">= 0%, greedy policy").Verdict);

    [Fact]
    public async Task Throughput_times_a_paired_request_once_with_no_full_horizon_row()
    {
        var (request, output) = Files("throughput.md", paired: true);
        var service = new StandInService("xmage");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = await Cli.RunAsync(["throughput", "--request", request, "--out", output, "--sample-seconds", "0"], stdout, stderr, service);

        Assert.Equal(0, exit);
        Assert.NotNull(Assert.Single(service.Bodies)["disruption_profile"]);
        var last = (await File.ReadAllLinesAsync(output))[^1];
        Assert.Contains("| 10000 pairs |", last);
        Assert.Matches(@"\| \d+ pairs/s, \d+ games/s \|", last);
        Assert.Contains("baseline >= 21.7%, greedy policy; disrupted >= 0.6%, greedy policy", last);
        Assert.EndsWith("| pass |", last);
    }

    /// <summary>The committed tier-3 request: the done-query 2
    /// final 99, Needle on the battlefield by turn 4, the default disruption
    /// profile, 10,000 pairs.</summary>
    [Fact]
    public void The_committed_tier_3_request_is_the_final_99_under_the_default_profile()
    {
        var root = RepoRoot.Find();
        var request = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "eval", "engine", "tier-3-request.json")))!.AsObject();
        var final99 = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "eval", "acceptance", "done-query-2", "final-99.json")));

        Assert.True(JsonNode.DeepEquals(final99, request["decklist"]));
        Assert.Equal("""{"kind":"on_battlefield","card":"Pithing Needle","by_turn":4}""", request["goal"]!.ToJsonString());
        Assert.Equal("""[{"kind":"counterspell","turn":3},{"kind":"targeted_removal","turn":4}]""", request["disruption_profile"]!.ToJsonString());
        Assert.Equal(10_000, (int)request["runs"]!);
        Assert.Equal(10, (int)request["turn_horizon"]!);
        Assert.Null(request["disruption_mode"]);
    }

    /// <summary>The committed tier-4 request: the tier-3
    /// request with one more member, <c>disruption_mode</c> interactive, so
    /// the two rows time the same games in the two modes.</summary>
    [Fact]
    public void The_committed_tier_4_request_is_the_tier_3_request_in_interactive_mode()
    {
        var root = RepoRoot.Find();
        var tier3 = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "eval", "engine", "tier-3-request.json")))!.AsObject();
        var tier4 = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "eval", "engine", "tier-4-request.json")))!.AsObject();

        Assert.Equal("interactive", (string?)tier4["disruption_mode"]);
        tier4.Remove("disruption_mode");
        Assert.True(JsonNode.DeepEquals(tier3, tier4));
    }

    [Fact]
    public void A_coverage_row_gives_the_unprofiled_share()
    {
        var row = new CoverageRow(Noon, "xmage", "v", "final.json", 100, [], ["Cultivate", "Rhystic Study"]);

        Assert.Equal("| 2026-09-27 12:00 | xmage v | final.json | 100 | 0 | 2 | 2.0% | Cultivate, Rhystic Study |", row.Markdown());
    }

    [Fact]
    public async Task Throughput_appends_the_gate_row_and_the_full_horizon_row()
    {
        var (request, output) = Files("throughput.md");
        var service = new StandInService("xmage");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = await Cli.RunAsync(["throughput", "--request", request, "--out", output, "--sample-seconds", "0"], stdout, stderr, service);

        Assert.Equal(0, exit);
        var lines = await File.ReadAllLinesAsync(output);
        Assert.Equal("# Engine throughput", lines[0]);
        Assert.EndsWith("| pass |", lines[^2]);
        Assert.Contains("(full horizon)", lines[^1]);
        Assert.EndsWith("| info |", lines[^1]);
        Assert.Equal(1, (int)service.Bodies[0]["goal"]!["by_turn"]!);
        Assert.Equal(10, (int)service.Bodies[1]["goal"]!["by_turn"]!);
    }

    [Fact]
    public async Task Throughput_refuses_a_service_that_runs_goldfish()
    {
        var (request, output) = Files("throughput.md");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = await Cli.RunAsync(["throughput", "--request", request, "--out", output, "--sample-seconds", "0"], stdout, stderr, new StandInService("goldfish"));

        Assert.Equal(3, exit);
        Assert.Contains("old build", stderr.ToString());
        Assert.Contains("docker compose -p mtg-archive -f compose.yaml -f compose.gpu.yml up -d --build xmage service", stderr.ToString());
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Coverage_posts_one_run_and_appends_the_share()
    {
        var (request, output) = Files("coverage.md");
        var service = new StandInService("xmage");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = await Cli.RunAsync(["coverage", "--out", output, request], stdout, stderr, service);

        Assert.Equal(0, exit);
        Assert.Equal(1, (int)service.Bodies[0]["runs"]!);
        Assert.EndsWith("| request.json | 2 | 0 | 1 | 50.0% | Rhystic Study |", (await File.ReadAllLinesAsync(output))[^1]);
    }

    /// <summary>Coverage plays one tier-2 run: it strips the profile and the
    /// mode, because a mode with no profile is a bad request.</summary>
    [Fact]
    public async Task Coverage_strips_the_disruption_profile_and_the_disruption_mode()
    {
        var (request, output) = Files("coverage.md", paired: true);
        var body = JsonNode.Parse(File.ReadAllText(request))!.AsObject();
        body["disruption_mode"] = "interactive";
        File.WriteAllText(request, body.ToJsonString());
        var service = new StandInService("xmage");

        var exit = await Cli.RunAsync(["coverage", "--out", output, request], new StringWriter(), new StringWriter(), service);

        Assert.Equal(0, exit);
        Assert.Null(service.Bodies[0]["disruption_profile"]);
        Assert.False(service.Bodies[0].ContainsKey("disruption_mode"));
    }

    [Fact]
    public async Task An_unknown_command_is_a_usage_error()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        Assert.Equal(1, await Cli.RunAsync(["bogus"], stdout, stderr));
        Assert.Contains("usage:", stderr.ToString());
    }

    /// <summary>A request file (a commander and one entry, goal due turn 1) and an output path in a fresh temp directory.</summary>
    private static (string Request, string Output) Files(string output, bool paired = false)
    {
        var dir = Directory.CreateTempSubdirectory("engine-gate-").FullName;
        var request = Path.Combine(dir, "request.json");
        var profile = paired ? ""","disruption_profile":[{"kind":"counterspell","turn":3}]""" : string.Empty;
        File.WriteAllText(request, $$"""{"decklist":{"commanders":[{"name":"A"}],"entries":[{"card":{"name":"B"},"quantity":99}]},"goal":{"kind":"cast","card":"A","by_turn":1},"runs":10000,"seed":1,"turn_horizon":10{{profile}}}""");
        return (request, Path.Combine(dir, output));
    }

    /// <summary>Answers every /simulate with a small envelope of one engine, and keeps each request body.</summary>
    private sealed class StandInService(string engine) : HttpMessageHandler
    {
        public List<JsonObject> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Bodies.Add(body);
            var disruption = body["disruption_profile"] is null ? string.Empty : ""","disruption":{"disrupted":{"probability":{"label":">= 0.6%, greedy policy"}}}""";
            var envelope = $$$"""{"engine":{"name":"{{{engine}}}","version":"xmage-1.4.62V1+0123abcd"},"runs":10000,"turn_horizon":10,"engine_errors":0,"probability":{"label":">= 21.7%, greedy policy"},"engine_coverage":{"unimplemented":[],"unprofiled":["Rhystic Study"]}{{{disruption}}}}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope, Encoding.UTF8, "application/json") };
        }
    }
}
