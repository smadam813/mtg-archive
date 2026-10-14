using System.Text.Json;
using System.Text.Json.Nodes;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;
using MtgArchive.Tools.EngineGate.Parity;

namespace MtgArchive.Tests;

/// <summary>
/// The parity gate's logic (spec §6.1): the Newcombe interval, the verdict
/// with its fresh-seed confirmation, and the frozen files.
/// The host run is not a test; these pin what it computes.
/// </summary>
public sealed class EngineGateParityTests
{
    /// <summary>Newcombe (1998), Statistics in Medicine 17, table II, method 10.</summary>
    [Theory]
    [InlineData(56, 70, 48, 80, 0.0524, 0.3339)]
    [InlineData(9, 10, 3, 10, 0.1705, 0.8090)]
    public void Newcombe_matches_the_published_examples(int s1, int n1, int s2, int n2, double lower, double upper)
    {
        var d = Difference.Newcombe(s1, n1, s2, n2);

        Assert.Equal(lower, d.Lower, 4);
        Assert.Equal(upper, d.Upper, 4);
        Assert.Equal((double)s1 / n1 - (double)s2 / n2, d.Estimate, 10);
    }

    [Fact]
    public void Equal_floors_give_an_interval_around_zero()
    {
        var d = Difference.Newcombe(2170, 10_000, 2170, 10_000);

        Assert.True(d.ContainsZero);
        Assert.Equal(-d.Upper, d.Lower, 10);
    }

    [Fact]
    public void The_verdict_passes_on_zero_then_on_the_fresh_seed_and_otherwise_fails()
    {
        var zero = new Difference(0.001, -0.01, 0.012);
        var off = new Difference(-0.036, -0.047, -0.025);

        Assert.Equal(Verdicts.Pass, Verdicts.Of(zero, null, 0, 10_000));
        Assert.Equal(Verdicts.PassFresh, Verdicts.Of(off, zero, 0, 10_000));
        Assert.Equal(Verdicts.Fail, Verdicts.Of(off, off, 0, 10_000));
        Assert.Equal(Verdicts.Errors, Verdicts.Of(zero, null, 101, 10_000));
        Assert.Throws<ArgumentException>(() => Verdicts.Of(off, null, 0, 10_000));
    }

    /// <summary>Phase 3E: an accepted entry turns a fail into accepted only when both seeds point its way.</summary>
    [Fact]
    public void An_accepted_entry_turns_a_fail_into_accepted_only_in_its_direction()
    {
        var zero = new Difference(0.001, -0.01, 0.012);
        var lower = new Difference(-0.036, -0.047, -0.025);
        var higher = new Difference(0.036, 0.025, 0.047);
        var down = new AcceptedDifference("c/goal-aware", AcceptedDifference.Lower, "rules the baseline lacks", "diagnosis/x.md");
        var up = down with { Direction = AcceptedDifference.Higher };

        Assert.Equal(Verdicts.Accepted, Verdicts.Of(lower, lower, 0, 10_000, accepted: down));
        Assert.Equal(Verdicts.Fail, Verdicts.Of(lower, lower, 0, 10_000, accepted: up));
        Assert.Equal(Verdicts.Fail, Verdicts.Of(lower, higher, 0, 10_000, accepted: down));
        Assert.Equal(Verdicts.Pass, Verdicts.Of(zero, null, 0, 10_000, accepted: down));
        Assert.Equal(Verdicts.PassFresh, Verdicts.Of(lower, zero, 0, 10_000, accepted: down));
        Assert.Equal(Verdicts.Errors, Verdicts.Of(lower, lower, 101, 10_000, accepted: down));
    }

    /// <summary>Phase 3E, Review Focus 1: a bad accepted file stops the gate before any game.</summary>
    [Fact]
    public void Accepted_problems_name_an_unknown_comparison_a_bad_direction_and_missing_evidence()
    {
        var root = Path.Combine(Path.GetTempPath(), $"accepted-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "diagnosis"));
        File.WriteAllText(Path.Combine(root, "diagnosis", "x.md"), "# x");
        var good = new AcceptedDifference("a/goal-aware", AcceptedDifference.Lower, "cause", "diagnosis/x.md");
        var file = new AcceptedDifferences(
        [
            good,
            good with { Comparison = "typo/goal-aware" },
            good with { Comparison = "b/goal-aware", Direction = "xmage_lower" },
            good with { Comparison = "c/goal-aware", Evidence = "diagnosis/none.md" },
            good with { Comparison = "d/goal-aware", Cause = " " },
            good,
        ]);

        var problems = file.Problems(["a/goal-aware", "b/goal-aware", "c/goal-aware", "d/goal-aware"], root);

        Assert.Contains(problems, p => p.Contains("typo/goal-aware", StringComparison.Ordinal) && p.Contains("no such comparison", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("b/goal-aware", StringComparison.Ordinal) && p.Contains("direction", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("c/goal-aware", StringComparison.Ordinal) && p.Contains("evidence", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("d/goal-aware", StringComparison.Ordinal) && p.Contains("cause", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("a/goal-aware", StringComparison.Ordinal) && p.Contains("twice", StringComparison.Ordinal));
        Assert.Equal(5, problems.Count);
        Assert.Empty(new AcceptedDifferences([good]).Problems(["a/goal-aware"], root));
        Assert.Empty(AcceptedDifferences.Load(Path.Combine(root, "missing.json")).Accepted);
        var empty = Path.Combine(root, "empty.json");
        File.WriteAllText(empty, "{}");
        Assert.Empty(AcceptedDifferences.Load(empty).Problems(["a/goal-aware"], root));
        File.WriteAllText(empty, "null");
        Assert.Empty(AcceptedDifferences.Load(empty).Accepted);
    }

    /// <summary>Phase 3E: an accepted comparison exits 0 and the report names its cause.</summary>
    [Fact]
    public async Task Parity_accepts_a_named_comparison_and_reports_its_cause()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        Assert.Equal(0, await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter()));
        RewriteBaseline(dir, successes: 20, freshSuccesses: 20);
        Directory.CreateDirectory(Path.Combine(dir, "diagnosis"));
        File.WriteAllText(Path.Combine(dir, "diagnosis", "needle.md"), "# needle");
        ParityJson.Write(Path.Combine(dir, "accepted.json"), new AcceptedDifferences(
        [
            new("needle-t3/goal-aware", AcceptedDifference.Lower, "a rule the baseline lacks", "diagnosis/needle.md"),
            new("needle-t3/land-window", AcceptedDifference.Lower, "a rule the baseline lacks", "diagnosis/needle.md"),
        ]));
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", Endpoint(fake), "--cap-seeds", "3"], new StringWriter(), new StringWriter());

        var report = File.ReadAllText(Path.Combine(dir, "report.md"));
        Assert.Equal(0, exit);
        Assert.Contains("| accepted |", report);
        Assert.Contains("## Accepted differences", report);
        Assert.Contains("a rule the baseline lacks", report);
        Assert.Contains("diagnosis/needle.md", report);
    }

    /// <summary>Phase 3E, Review Focus 1 at the command level.</summary>
    [Fact]
    public async Task Parity_refuses_an_accepted_entry_for_no_comparison()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        Assert.Equal(0, await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter()));
        Directory.CreateDirectory(Path.Combine(dir, "diagnosis"));
        File.WriteAllText(Path.Combine(dir, "diagnosis", "needle.md"), "# needle");
        ParityJson.Write(Path.Combine(dir, "accepted.json"), new AcceptedDifferences(
            [new("needle-t9/goal-aware", AcceptedDifference.Lower, "cause", "diagnosis/needle.md")]));
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", "127.0.0.1:1"], new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("needle-t9/goal-aware", stderr.ToString());
    }

    /// <summary>Engine errors at the fresh seed pull its floor down; they must not read as a pass.</summary>
    [Fact]
    public void Engine_errors_in_more_than_one_percent_of_the_fresh_runs_give_the_errors_verdict()
    {
        var zero = new Difference(0.001, -0.01, 0.012);
        var off = new Difference(-0.036, -0.047, -0.025);

        Assert.Equal(Verdicts.Errors, Verdicts.Of(off, zero, 0, 10_000, freshEngineErrors: 101));
        Assert.Equal(Verdicts.Errors, Verdicts.Of(off, off, 0, 10_000, freshEngineErrors: 101));
        Assert.Equal(Verdicts.PassFresh, Verdicts.Of(off, zero, 0, 10_000, freshEngineErrors: 100));
    }

    private static ParityCases PairedCases() => new([new DeckSource("d", "d.json")], 100, 1, 2, 10, ["goal-aware", "land-window"],
    [
        new ParityCase("paired", "d", JsonNode.Parse("""{"kind":"cast","card":"X","by_turn":4}""")!.AsObject(),
            JsonNode.Parse("""[{"kind":"targeted_removal","turn":4}]""")!.AsArray(), "x"),
    ]);

    private static ParityBaseline DisruptedBaseline(ParityCases cases, int successes, int freshSuccesses)
        => new("xmage", "v", "2026-09-28",
        [
            .. from m in cases.Mulligans
               from arm in new[] { "baseline", "disrupted" }
               select new BaselineEntry($"paired/{m}/{arm}", "paired", "d", "sha", cases.Cases[0].Goal, m, cases.Cases[0].Disruption, arm, 100,
                   1, arm == "disrupted" ? successes : 50, [], 2, arm == "disrupted" ? freshSuccesses : 50, [], 10),
        ]);

    [Fact]
    public void The_baseline_entry_is_stale_when_its_case_changed()
    {
        var cases = PairedCases();
        var baseline = DisruptedBaseline(cases, 0, 0);
        var @case = cases.Cases[0];
        string? Problem(ParityCases c) => baseline.Find("paired/goal-aware/disrupted", "sha", 100, c, c.Cases[0], "goal-aware").Problem;

        Assert.Null(Problem(cases));
        Assert.Contains("stale: the disruption changed", Problem(cases with { Cases = [@case with { Disruption = JsonNode.Parse("""[{"kind":"targeted_removal","turn":3}]""")!.AsArray() }] }));
        Assert.Contains("stale: the goal changed", Problem(cases with { Cases = [@case with { Goal = JsonNode.Parse("""{"kind":"cast","card":"X","by_turn":3}""")!.AsObject() }] }));
        Assert.Contains("stale: the base seed changed", Problem(cases with { BaseSeed = 5 }));
        Assert.Contains("stale: the fresh seed changed", Problem(cases with { FreshSeed = 5 }));
        Assert.Contains("stale: the turn horizon changed", Problem(cases with { TurnHorizon = 9 }));
        Assert.Contains("stale: the format changed", Problem(cases with { Cases = [@case with { Format = "brawl" }] }));
        // Commander named is Commander unnamed: the same game rules.
        Assert.Null(Problem(cases with { Cases = [@case with { Format = "commander" }] }));
        // Key order is not a change: the goal compares as JSON.
        Assert.Null(Problem(cases with { Cases = [@case with { Goal = JsonNode.Parse("""{"by_turn":4,"card":"X","kind":"cast"}""")!.AsObject() }] }));
    }

    /// <summary>Null is abstracted, so naming abstracted keeps the
    /// entry; any other mode makes it stale.</summary>
    [Fact]
    public void The_baseline_entry_is_stale_when_its_disruption_mode_changed()
    {
        var cases = PairedCases();
        var baseline = DisruptedBaseline(cases, 0, 0);
        var @case = cases.Cases[0];
        string? Problem(ParityCase c) => baseline.Find("paired/goal-aware/disrupted", "sha", 100, cases with { Cases = [c] }, c, "goal-aware").Problem;

        Assert.Null(Problem(@case with { DisruptionMode = "abstracted" }));
        Assert.Contains("stale: the disruption mode changed", Problem(@case with { DisruptionMode = "interactive" }));
        var interactive = baseline with { Comparisons = [.. baseline.Comparisons.Select(e => e with { DisruptionMode = "interactive" })] };
        Assert.Contains("stale: the disruption mode changed", interactive.Find("paired/goal-aware/disrupted", "sha", 100, cases, @case, "goal-aware").Problem);
    }

    /// <summary>The hold case plays the protection case's deck,
    /// goal, format and profile under the hold policy; no other case names a
    /// policy, so every earlier entry reads as goal-aware-greedy.</summary>
    [Fact]
    public void The_hold_case_plays_the_protection_case_under_the_hold_policy()
    {
        var cases = ParityCases.Load(Path.Combine(RepoRoot.Find(), "eval", "parity", "cases.json"));
        var protection = Assert.Single(cases.Cases, c => c.Name == "bq1-wrenn-and-six-responses-t3-interactive");
        var hold = Assert.Single(cases.Cases, c => c.Name == "bq1-wrenn-and-six-responses-t3-interactive-hold");

        Assert.Equal("goal-aware-hold", hold.PlayPolicyName());
        Assert.Equal((protection.Deck, protection.Format, protection.DisruptionMode), (hold.Deck, hold.Format, hold.DisruptionMode));
        Assert.Equal(protection.Goal.ToJsonString(), hold.Goal.ToJsonString());
        Assert.Equal(protection.Disruption!.ToJsonString(), hold.Disruption!.ToJsonString());
        Assert.Equal(["goal-aware-hold"], cases.Cases.Where(c => c.PlayPolicy is not null).Select(c => c.PlayPolicyName()));
        Assert.All(cases.Cases.Where(c => c.PlayPolicy is null), c => Assert.Equal("goal-aware-greedy", c.PlayPolicyName()));
    }

    /// <summary>Null is goal-aware-greedy, so naming it keeps
    /// the entry; the hold policy makes it stale.</summary>
    [Fact]
    public void The_baseline_entry_is_stale_when_its_play_policy_changed()
    {
        var cases = PairedCases();
        var baseline = DisruptedBaseline(cases, 0, 0);
        var @case = cases.Cases[0] with { DisruptionMode = "interactive" };
        var interactive = baseline with { Comparisons = [.. baseline.Comparisons.Select(e => e with { DisruptionMode = "interactive" })] };
        string? Problem(ParityCase c) => interactive.Find("paired/goal-aware/disrupted", "sha", 100, cases with { Cases = [c] }, c, "goal-aware").Problem;

        Assert.Null(Problem(@case));
        Assert.Null(Problem(@case with { PlayPolicy = "goal-aware-greedy" }));
        Assert.Contains("stale: the play policy changed", Problem(@case with { PlayPolicy = "goal-aware-hold" }));
    }

    [Theory]
    [InlineData("goal-aware-patient", "interactive", "paired: unknown play policy 'goal-aware-patient'; the policies are goal-aware-greedy, goal-aware-hold.")]
    [InlineData("goal-aware-hold", null, "paired: the play policy goal-aware-hold needs an interactive disruption mode.")]
    [InlineData("goal-aware-hold", "abstracted", "paired: the play policy goal-aware-hold needs an interactive disruption mode.")]
    public void A_bad_play_policy_stops_the_gate_before_any_game(string policy, string? mode, string message)
    {
        var @case = PairedCases().Cases[0] with { DisruptionMode = mode, PlayPolicy = policy };

        var error = Assert.Throws<InvalidDataException>(() => ParityRunner.RequirePlayable([@case]));

        Assert.Equal(message, error.Message);
    }

    /// <summary>A misspelt member would drop out in silence and play the case
    /// in the default mode; the reader stops the gate instead.</summary>
    [Theory]
    [InlineData("case")]
    [InlineData("top")]
    [InlineData("deck")]
    public void The_cases_reader_rejects_an_unmapped_member(string where)
    {
        var dir = StaplesGate(runs: 4);
        var path = Path.Combine(dir, "cases.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var target = where switch
        {
            "case" => json["cases"]![0]!.AsObject(),
            "deck" => json["decks"]![0]!.AsObject(),
            _ => json,
        };
        target["disruption_mod"] = "interactive";
        File.WriteAllText(path, json.ToJsonString());

        var error = Assert.Throws<InvalidDataException>(() => ParityCases.Load(path));

        Assert.Contains("disruption_mod", error.Message);
        Assert.Contains(path, error.Message);
    }

    [Fact]
    public void The_baseline_reader_rejects_an_unmapped_member()
    {
        var path = Path.Combine(Path.GetTempPath(), $"baseline-{Guid.NewGuid():N}.json");
        var json = JsonNode.Parse(JsonSerializer.Serialize(DisruptedBaseline(PairedCases(), 0, 0), ParityJson.Options))!.AsObject();
        json["comparisons"]![0]!["disruption_mod"] = "interactive";
        File.WriteAllText(path, json.ToJsonString());

        Assert.Contains("disruption_mod", Assert.Throws<InvalidDataException>(() => ParityBaseline.Load(path)).Message);
    }

    [Theory]
    [InlineData("tabletop", true, "paired: unknown disruption mode 'tabletop'; the modes are abstracted, interactive")]
    [InlineData("interactive", false, "paired: a disruption mode needs a disruption profile")]
    public void A_bad_disruption_mode_stops_the_gate_when_the_cases_load(string mode, bool profile, string message)
    {
        var dir = StaplesGate(runs: 4);
        var @case = PairedCases().Cases[0] with { Deck = "staples", DisruptionMode = mode };
        EditCases(dir, c => c with { Cases = [profile ? @case : @case with { Disruption = null }] });

        var error = Assert.Throws<InvalidDataException>(() => ParityCases.Load(Path.Combine(dir, "cases.json")));

        Assert.Contains(message, error.Message);
    }

    /// <summary>The committed files gain no member: a reload writes the same bytes.</summary>
    [Fact]
    public void The_committed_baseline_round_trips_byte_for_byte()
    {
        var path = Path.Combine(RepoRoot.Find(), "eval", "parity", "baseline.json");
        var copy = Path.Combine(Path.GetTempPath(), $"baseline-{Guid.NewGuid():N}.json");

        ParityBaseline.Load(path).Save(copy);

        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), File.ReadAllText(copy));
        // Only the tier-4 cases record a mode; every other entry keeps none.
        var interactive = ParityCases.Load(Path.Combine(Path.GetDirectoryName(path)!, "cases.json")).Cases.Where(c => c.DisruptionMode is not null).Select(c => c.Name).ToHashSet();
        Assert.All(ParityBaseline.Load(path).Comparisons, e => Assert.Equal(interactive.Contains(e.Case) ? DisruptionProfile.InteractiveMode : null, e.DisruptionMode));
    }

    /// <summary>Every event kind has an engine path, so an
    /// interactive case with all three kinds plays. Its disrupted arm seats
    /// the scripted opponent with the whole card table.</summary>
    [Fact]
    public async Task Parity_baseline_plays_an_interactive_case_with_every_event_kind()
    {
        var dir = StaplesGate(runs: 4);
        EditCases(dir, c => c with
        {
            Mulligans = ["goal-aware"],
            Cases = [c.Cases[0] with { Disruption = JsonNode.Parse("""[{"kind":"counterspell","turn":2},{"kind":"targeted_removal","turn":3},{"kind":"board_wipe","turn":3}]""")!.AsArray(), DisruptionMode = "interactive" }],
        });
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));

        Assert.Equal(0, await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["parity-baseline", "--cases", Path.Combine(dir, "cases.json"), "--endpoint", Endpoint(fake)], new StringWriter(), new StringWriter()));

        Assert.Equal(["interactive", "interactive"], ParityBaseline.Load(Path.Combine(dir, "baseline.json")).Comparisons.Select(c => c.DisruptionMode));
        var scripted = fake.Received.Where(m => (string?)m["type"] == "start_game" && m["opponent"] is not null).ToList();
        Assert.NotEmpty(scripted);
        Assert.All(scripted, m => Assert.Equal("""{"cards":["Counterspell","Atomize","Rout"]}""", m["opponent"]!.ToJsonString()));
    }

    /// <summary>An interactive counterspell case plays. Its
    /// disrupted arm seats the scripted opponent and records the mode; its
    /// baseline arm seats the idle opponent.</summary>
    [Fact]
    public async Task Parity_baseline_plays_an_interactive_counterspell_case()
    {
        var dir = StaplesGate(runs: 4);
        EditCases(dir, c => c with
        {
            Mulligans = ["goal-aware"],
            Cases = [c.Cases[0] with { Disruption = JsonNode.Parse("""[{"kind":"counterspell","turn":2}]""")!.AsArray(), DisruptionMode = "interactive" }],
        });
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));

        Assert.Equal(0, await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["parity-baseline", "--cases", Path.Combine(dir, "cases.json"), "--endpoint", Endpoint(fake)], new StringWriter(), new StringWriter()));

        var baseline = ParityBaseline.Load(Path.Combine(dir, "baseline.json"));
        Assert.Equal(["interactive", "interactive"], baseline.Comparisons.Select(c => c.DisruptionMode));
        var starts = fake.Received.Where(m => (string?)m["type"] == "start_game").ToList();
        Assert.Contains(starts, m => m["opponent"] is null);
        Assert.Contains(starts, m => m["opponent"] is not null);
    }

    /// <summary>An abstracted case named as such plays and records the mode;
    /// a case with no mode records none, so old entries keep their bytes.</summary>
    [Fact]
    public async Task Parity_baseline_records_the_disruption_mode_of_a_case_that_names_one()
    {
        var dir = StaplesGate(runs: 4, secondCase: true);
        var profile = JsonNode.Parse("""[{"kind":"counterspell","turn":2}]""")!.AsArray();
        EditCases(dir, c => c with
        {
            Mulligans = ["goal-aware"],
            Cases = [c.Cases[0] with { Disruption = profile, DisruptionMode = "abstracted" }, c.Cases[1] with { Disruption = profile.DeepClone().AsArray() }],
        });

        Assert.Equal(0, await BaselineAsync(["--cases", Path.Combine(dir, "cases.json")], new StringWriter(), new StringWriter()));

        var baseline = ParityBaseline.Load(Path.Combine(dir, "baseline.json"));
        Assert.Equal(["abstracted", "abstracted", null, null], baseline.Comparisons.Select(c => c.DisruptionMode));
        Assert.DoesNotContain("disruption_mode\": null", File.ReadAllText(Path.Combine(dir, "baseline.json")));
    }

    [Fact]
    public void The_report_counts_accepted_rows_apart_and_lists_the_entries()
    {
        var cases = PairedCases();
        var lower = new Difference(-0.036, -0.047, -0.025);
        var zero = new Difference(0.001, -0.01, 0.012);
        var entry = new AcceptedDifference("a", AcceptedDifference.Lower, "rules the baseline lacks", "diagnosis/x.md");
        var spare = new AcceptedDifference("b", AcceptedDifference.Lower, "not needed", "diagnosis/x.md");
        ComparisonRow[] rows =
        [
            new("a", "e", 100, 50, 46, 0, 0, lower, lower, Verdicts.Accepted),
            new("b", "e", 100, 50, 50, 0, null, zero, null, Verdicts.Pass),
        ];

        var report = ParityReport.Render(DateTimeOffset.UnixEpoch, DisruptedBaseline(cases, 0, 0), "x", cases, 100, rows, [], new AcceptedDifferences([entry, spare]));

        Assert.Contains("**Verdict: 1 of 2 comparisons pass, 1 accepted.**", report);
        Assert.Contains("| a | lower | accepted | rules the baseline lacks | diagnosis/x.md |", report);
        Assert.Contains("| b | lower | pass; the entry is not needed | not needed | diagnosis/x.md |", report);
    }

    [Fact]
    public void The_report_names_a_disrupted_arm_the_baseline_never_meets()
    {
        var cases = PairedCases();

        var never = ParityReport.Render(DateTimeOffset.UnixEpoch, DisruptedBaseline(cases, 0, 0), "x", cases, 100, [], [], new AcceptedDifferences([]));
        var sometimes = ParityReport.Render(DateTimeOffset.UnixEpoch, DisruptedBaseline(cases, 0, 3), "x", cases, 100, [], [], new AcceptedDifferences([]));

        Assert.Contains("The baseline meets the goal in no disrupted run", never);
        Assert.DoesNotContain("The baseline meets the goal in no disrupted run", sometimes);
    }

    [Fact]
    public void The_report_header_states_the_runs_the_gate_played()
    {
        var cases = PairedCases();

        var report = ParityReport.Render(DateTimeOffset.UnixEpoch, DisruptedBaseline(cases, 0, 0), "x", cases, 37, [], [], new AcceptedDifferences([]));

        Assert.Contains("plays 37 games per engine", report);
    }

    /// <summary>Records that hold arrays compare them by reference, so the
    /// round trip compares each value's JSON.</summary>
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, ParityJson.Options);

    [Fact]
    public void A_parity_deck_round_trips_through_its_file()
    {
        var golden = TestDecks.Load("staples-needle-goal-aware");
        var profiles = golden.Profiles;
        var deck = new ParityDeck("staples", "golden", [new Substitution("A", 1, "B")], new Decklist(), golden.Deck, profiles.ByFace, ["X"]);
        var path = Path.Combine(Path.GetTempPath(), $"parity-deck-{Guid.NewGuid():N}.json");

        deck.Save(path);
        var loaded = ParityDeck.Load(path);

        Assert.Equal(golden.Deck.Select(Json), loaded.Deck.Select(Json));
        Assert.Equal(profiles.ByFace.OrderBy(p => p.Key).Select(p => (p.Key, Json(p.Value))), loaded.Profiles.OrderBy(p => p.Key).Select(p => (p.Key, Json(p.Value))));
        Assert.Equal(["X"], loaded.Unprofiled);
        Assert.Equal([new Substitution("A", 1, "B")], loaded.Substitutions);
        Assert.Equal(64, ParityDeck.Sha(path).Length);
    }

    [Fact]
    public void A_comparison_is_a_case_under_each_mulligan_policy_and_a_disrupted_case_has_two_arms_in_its_name()
    {
        var cases = new ParityCases([new DeckSource("d", "d.json")], 100, 1, 2, 10, ["goal-aware", "land-window"],
        [
            new ParityCase("a", "d", JsonNode.Parse("""{"kind":"cast","card":"X","by_turn":1}""")!.AsObject(), null, "x"),
            new ParityCase("b", "d", JsonNode.Parse("""{"kind":"cast","card":"X","by_turn":1}""")!.AsObject(), null, "x"),
        ]);

        Assert.Equal(["a/goal-aware", "a/land-window", "b/goal-aware", "b/land-window"], ParityRunner.Comparisons(cases, null).Select(c => c.Name));
        Assert.Equal(["b/goal-aware", "b/land-window"], ParityRunner.Comparisons(cases, "b").Select(c => c.Name));
        Assert.Equal("a/goal-aware/disrupted", ParityRunner.NameOf("a/goal-aware", "disrupted"));
        Assert.Equal("a/goal-aware", ParityRunner.NameOf("a/goal-aware", null));
    }

    [Fact]
    public void The_committed_cases_parse()
    {
        var path = Path.Combine(RepoRoot.Find(), "eval", "parity", "cases.json");
        var cases = ParityCases.Load(path);

        Assert.Equal(["dq2-obeka", "dq2-final-99", "dq1-golos", "brawl-seek-conjure", "bq1-wrenn-and-six", "bq1-wrenn-and-six-responses", "bq2-jolly-balloon-man", "brawl-calibration"], cases.Decks.Select(d => d.Name));
        Assert.All(cases.Decks, d => Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, d.Source)), d.Source));
        Assert.All(cases.Cases, c => Assert.Contains(c.Deck, cases.Decks.Select(d => d.Name)));
        Assert.All(cases.Cases, c => Assert.True(c.ParseGoal().LastTurn <= cases.TurnHorizon));
        // The tier-3 case plays abstracted with no mode named; the tier-4 cases name interactive.
        Assert.Equal(["dq2-needle-t4-disrupted", "dq2-final-99-needle-t4-interactive", "bq1-wrenn-and-six-responses-t3-interactive", "bq1-wrenn-and-six-responses-t3-interactive-hold"], cases.Cases.Where(c => c.ParseDisruption() is not null).Select(c => c.Name));
        Assert.Null(cases.Cases.Single(c => c.Name == "dq2-needle-t4-disrupted").DisruptionMode);
        Assert.Equal(["dq2-final-99-needle-t4-interactive", "bq1-wrenn-and-six-responses-t3-interactive", "bq1-wrenn-and-six-responses-t3-interactive-hold"], cases.Cases.Where(c => c.DisruptionMode is not null).Select(c => c.Name));
        Assert.All(cases.Cases.Where(c => c.DisruptionMode is not null), c => Assert.Equal(DisruptionMode.Interactive, c.Mode()));
        // The Commander cases name no format: their comparisons and baseline entries stay as they were.
        Assert.All(cases.Cases.Where(c => c.Deck.StartsWith("dq", StringComparison.Ordinal)), c => Assert.Null(c.Format));
        Assert.Equal(32, ParityRunner.Comparisons(cases, null).Count());
        Assert.Equal(10_000, cases.Runs);
    }

    /// <summary>The frozen Brawl acceptance decks join the gate,
    /// each with a commander-cast-by-turn-3 case in its format. The BQ1 deck
    /// plays under Brawl and under Competitive Brawl.</summary>
    [Theory]
    [InlineData("bq1-wrenn-and-six-t3-brawl", "bq1-wrenn-and-six", "brawl", "Wrenn and Six", 100)]
    [InlineData("bq1-wrenn-and-six-t3-competitive", "bq1-wrenn-and-six", "competitive_brawl", "Wrenn and Six", 100)]
    [InlineData("bq2-jolly-balloon-man-t3", "bq2-jolly-balloon-man", "standard_brawl", "The Jolly Balloon Man", 60)]
    public void Each_brawl_acceptance_deck_has_a_commander_cast_by_turn_3_case_in_its_format(string name, string deckName, string format, string commander, int size)
    {
        var path = Path.Combine(RepoRoot.Find(), "eval", "parity", "cases.json");
        var cases = ParityCases.Load(path);
        var @case = Assert.Single(cases.Cases, c => c.Name == name);

        Assert.Equal(deckName, @case.Deck);
        Assert.Equal(format, @case.Format);
        Assert.Equal("cast", (string?)@case.Goal["kind"]);
        Assert.Equal(commander, (string?)@case.Goal["card"]);
        Assert.Equal(3, (int)@case.Goal["by_turn"]!);
        var deck = ParityDeck.Load(Path.Combine(Path.GetDirectoryName(path)!, "decks", deckName + ".json"));
        Assert.Equal(commander, Assert.Single(deck.Deck, c => c.IsCommander).Name);
        Assert.Equal(size, deck.Deck.Sum(c => c.Quantity));
        Assert.Empty(deck.Substitutions);
        Assert.StartsWith("../acceptance/brawl-query-", cases.Decks.Single(d => d.Name == deckName).Source, StringComparison.Ordinal);
    }

    /// <summary>The standing counters case. The done-query 2
    /// final 99 is frozen with no filler and with its response features, and
    /// plays Needle by turn 4 under the default profile in interactive mode,
    /// in Commander.</summary>
    [Fact]
    public void The_counters_case_plays_the_final_99_with_its_own_counterspells_in_interactive_mode()
    {
        var root = RepoRoot.Find();
        var path = Path.Combine(root, "eval", "parity", "cases.json");
        var cases = ParityCases.Load(path);
        var @case = Assert.Single(cases.Cases, c => c.Name == "dq2-final-99-needle-t4-interactive");

        Assert.Equal("dq2-final-99", @case.Deck);
        Assert.Null(@case.Format);
        Assert.Equal("""{"kind":"on_battlefield","card":"Pithing Needle","by_turn":4}""", @case.Goal.ToJsonString());
        Assert.Equal("""[{"kind":"counterspell","turn":3},{"kind":"targeted_removal","turn":4}]""", @case.Disruption!.ToJsonString());
        Assert.Equal(DisruptionMode.Interactive, ParityRunner.DisruptionOf(@case)!.Mode);
        Assert.Equal("../acceptance/done-query-2/final-99.json", cases.Decks.Single(d => d.Name == "dq2-final-99").Source);

        var deck = ParityDeck.Load(Path.Combine(Path.GetDirectoryName(path)!, "decks", "dq2-final-99.json"));
        var final99 = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "eval", "acceptance", "done-query-2", "final-99.json")))!;
        var names = final99["entries"]!.AsArray().Select(e => (string)e!["card"]!["name"]!).Append((string)final99["commanders"]![0]!["name"]!).Order(StringComparer.Ordinal);
        Assert.Empty(deck.Substitutions);
        Assert.Equal(names, deck.Deck.Select(c => c.Name).Order(StringComparer.Ordinal));
        Assert.Equal(100, deck.Deck.Sum(c => c.Quantity));
        Assert.Equal("Obeka, Brute Chronologist", Assert.Single(deck.Deck, c => c.IsCommander).Name);
        Assert.Equal(CounterReach.Spell, deck.Profiles["Counterspell"].Response!.Counter!.Reach);
        Assert.True(deck.Profiles["Pact of Negation"].Response!.Counter!.LosesGame);
    }

    /// <summary>The standing protection case. A second freeze of
    /// the Brawl acceptance query 1 deck carries the response features; the
    /// first freeze, and so its baselines, stay as they were.</summary>
    [Fact]
    public void The_protection_case_plays_a_second_freeze_of_the_bq1_deck_with_its_response_features()
    {
        var path = Path.Combine(RepoRoot.Find(), "eval", "parity", "cases.json");
        var cases = ParityCases.Load(path);
        var @case = Assert.Single(cases.Cases, c => c.Name == "bq1-wrenn-and-six-responses-t3-interactive");

        Assert.Equal("bq1-wrenn-and-six-responses", @case.Deck);
        Assert.Equal("brawl", @case.Format);
        Assert.Equal("""{"kind":"on_battlefield","card":"Wrenn and Six","by_turn":3}""", @case.Goal.ToJsonString());
        Assert.Equal("""[{"kind":"targeted_removal","turn":3}]""", @case.Disruption!.ToJsonString());
        Assert.Equal(DisruptionMode.Interactive, ParityRunner.DisruptionOf(@case)!.Mode);
        Assert.Equal(cases.Decks.Single(d => d.Name == "bq1-wrenn-and-six").Source, cases.Decks.Single(d => d.Name == "bq1-wrenn-and-six-responses").Source);

        var decks = Path.Combine(Path.GetDirectoryName(path)!, "decks");
        var first = ParityDeck.Load(Path.Combine(decks, "bq1-wrenn-and-six.json"));
        var second = ParityDeck.Load(Path.Combine(decks, "bq1-wrenn-and-six-responses.json"));
        Assert.Empty(second.Substitutions);
        Assert.Equal(first.Deck.Select(c => (c.Name, c.Quantity, c.IsCommander)), second.Deck.Select(c => (c.Name, c.Quantity, c.IsCommander)));
        Assert.Equal(first.Unprofiled, second.Unprofiled);
        Assert.All(first.Profiles.Values, p => Assert.Null(p.Response));
        Assert.Equal(["Heroic Intervention", "Snakeskin Veil", "Veil of Summer"], second.Profiles.Where(p => p.Value.Response is not null).Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Equal(first.Profiles.Keys.Order(StringComparer.Ordinal), second.Profiles.Keys.Order(StringComparer.Ordinal));
        Assert.All(first.Profiles, p => Assert.Equal(p.Value, second.Profiles[p.Key] with { Response = null }));
    }

    /// <summary>The calibration case has a closed form. One
    /// 0-mana card, no tutor and no draw spell: the goal is met only when the
    /// card is in the kept hand, so the result counts the turn-1 draw (none
    /// on the play) and the free mulligans (one in Brawl, none in
    /// Competitive Brawl).</summary>
    [Fact]
    public void The_calibration_deck_holds_one_zero_mana_goal_card_and_no_tutor()
    {
        var path = Path.Combine(RepoRoot.Find(), "eval", "parity", "cases.json");
        var cases = ParityCases.Load(path);
        var calibration = cases.Cases.Where(c => c.Deck == "brawl-calibration").ToList();

        Assert.Equal(["brawl", "competitive_brawl"], calibration.Select(c => c.Format));
        Assert.All(calibration, c => Assert.Equal("""{"kind":"cast","card":"Ornithopter","by_turn":1}""", c.Goal.ToJsonString()));
        var deck = ParityDeck.Load(Path.Combine(Path.GetDirectoryName(path)!, "decks", "brawl-calibration.json"));
        Assert.Equal(100, deck.Deck.Sum(c => c.Quantity));
        var library = deck.Deck.Where(c => !c.IsCommander).ToList();
        Assert.Equal(1, Assert.Single(library, c => c.Name == "Ornithopter" && c.ManaValue == 0).Quantity);
        Assert.Equal(40, library.Where(c => c.IsLand).Sum(c => c.Quantity));
        Assert.All(library.Where(c => !c.IsLand && c.Name != "Ornithopter"), c => Assert.True(c.ManaValue >= 2, c.Name));
        Assert.All(deck.Profiles.Values, p =>
        {
            Assert.Null(p.Tutor);
            Assert.False(p.Draws);
        });
    }

    /// <summary>The XMage-implemented seek cards: every Mage.Sets card that
    /// calls <c>seekCard</c> or <c>SeekCardEffect</c> at the pinned tag
    /// (engine/xmage/XMAGE_TAG), read from the sidecar build's source.</summary>
    private static readonly string[] XmageSeekCards =
    [
        "Bounty of the Deep", "Faceless Agent", "Hollowhenge Wrangler", "Interdisciplinary Studies", "Jukai Liberator", "Kindred Denial",
        "Manor Guardian", "Obsessive Collector", "Skyshroud Lookout", "Soulstealer Axe", "Wandering Treefolk",
    ];

    /// <summary>One Brawl deck that only the determinism and leak
    /// gates play holds every XMage seek card and conjure cards.</summary>
    [Fact]
    public void The_determinism_only_brawl_deck_holds_every_xmage_seek_card_and_conjure_cards()
    {
        var path = Path.Combine(RepoRoot.Find(), "eval", "parity", "cases.json");
        var cases = ParityCases.Load(path);
        var source = Assert.Single(cases.Decks, d => d.DeterminismOnly);

        Assert.Equal("brawl", source.Format);
        Assert.Equal(GameRules.Brawl, source.Rules());
        Assert.True(source.ParseGoal().LastTurn <= cases.TurnHorizon);
        var deck = ParityDeck.Load(Path.Combine(Path.GetDirectoryName(path)!, "decks", source.Name + ".json"));
        var names = deck.Deck.Select(c => c.Name).ToHashSet();
        Assert.Subset(names, XmageSeekCards.ToHashSet());
        Assert.Contains("Expedition Supplier", names); // conjure
        Assert.Contains("Shoreline Scout", names); // conjure
        Assert.Equal(100, deck.Deck.Sum(c => c.Quantity));
        Assert.Single(deck.Deck, c => c.IsCommander);
    }

    /// <summary>A parity directory with one frozen deck (the golden staples
    /// deck), one Needle goal and both mulligan policies. With
    /// <paramref name="secondCase"/>, a turn-2 Needle goal follows.</summary>
    internal static string StaplesGate(int runs, bool secondCase = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"parity-{Guid.NewGuid():N}");
        var golden = TestDecks.Load("staples-needle-goal-aware");
        new ParityDeck("staples", "golden", [], new Decklist(), golden.Deck, golden.Profiles.ByFace, [])
            .Save(Path.Combine(dir, "decks", "staples.json"));
        List<ParityCase> list = [new ParityCase("needle-t3", "staples", JsonNode.Parse("""{"kind":"on_battlefield","card":"Pithing Needle","by_turn":3}""")!.AsObject(), null, "the golden staples deck")];
        if (secondCase)
        {
            list.Add(new ParityCase("needle-t2", "staples", JsonNode.Parse("""{"kind":"on_battlefield","card":"Pithing Needle","by_turn":2}""")!.AsObject(), null, "the golden staples deck"));
        }

        ParityJson.Write(Path.Combine(dir, "cases.json"), new ParityCases(
            [new DeckSource("staples", "golden")], runs, 7, 8, 3, ["goal-aware", "land-window"], list));
        return dir;
    }

    /// <summary>Rewrites the gate's cases.json.</summary>
    private static void EditCases(string dir, Func<ParityCases, ParityCases> edit)
    {
        var path = Path.Combine(dir, "cases.json");
        ParityJson.Write(path, edit(ParityCases.Load(path)));
    }

    private static string Endpoint(FakeSidecar fake) => $"{fake.Options().Host}:{fake.Options().Port}";

    /// <summary>Runs parity-baseline against its own fake sidecar, whose
    /// games end at the horizon with no goal met.</summary>
    private static async Task<int> BaselineAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        return await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity-baseline", .. args, "--endpoint", Endpoint(fake)], stdout, stderr);
    }

    /// <summary>A case edit after the baseline: the gate stops before any game.</summary>
    [Theory]
    [InlineData("goal")]
    [InlineData("horizon")]
    [InlineData("seed")]
    [InlineData("format")]
    public async Task Parity_refuses_a_baseline_whose_case_changed(string field)
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        Assert.Equal(0, await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter()));
        EditCases(dir, c => field switch
        {
            "goal" => c with { Cases = [c.Cases[0] with { Goal = JsonNode.Parse("""{"kind":"on_battlefield","card":"Pithing Needle","by_turn":2}""")!.AsObject() }] },
            "horizon" => c with { TurnHorizon = 4 },
            "format" => c with { Cases = [c.Cases[0] with { Format = "brawl" }] },
            _ => c with { FreshSeed = 9 },
        });
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", Endpoint(fake)], new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("stale", stderr.ToString());
        Assert.Contains("run parity-baseline again", stderr.ToString());
        Assert.DoesNotContain(fake.Received, m => (string?)m["type"] == "start_game");
    }

    /// <summary>An --only that names no case would pass the gate on no evidence.</summary>
    [Fact]
    public async Task Parity_only_with_no_such_case_writes_nothing_and_fails()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter());
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", Endpoint(fake), "--only", "needle-t9", "--cap-seeds", "0"], new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("no case named 'needle-t9'", stderr.ToString());
        Assert.False(File.Exists(Path.Combine(dir, "report.md")));
        Assert.DoesNotContain(fake.Received, m => (string?)m["type"] == "start_game");
    }

    [Fact]
    public async Task Parity_baseline_only_with_no_such_case_writes_nothing_and_fails()
    {
        var dir = StaplesGate(runs: 20);
        var stderr = new StringWriter();

        var exit = await BaselineAsync(["--cases", Path.Combine(dir, "cases.json"), "--only", "needle-t9"], new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("no case named 'needle-t9'", stderr.ToString());
        Assert.False(File.Exists(Path.Combine(dir, "baseline.json")));
    }

    [Fact]
    public async Task Parity_baseline_only_replaces_the_named_case_and_keeps_the_others()
    {
        var dir = StaplesGate(runs: 20, secondCase: true);
        var cases = Path.Combine(dir, "cases.json");
        await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter());
        var path = Path.Combine(dir, "baseline.json");
        var marked = ParityBaseline.Load(path);
        // A baseline from before per-entry versions: only its header names the build.
        (marked with { Version = "marked", Comparisons = [.. marked.Comparisons.Select(c => c with { Successes = 999, EngineVersion = null })] }).Save(path);

        var exit = await BaselineAsync(["--cases", cases, "--only", "needle-t2"], new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        var merged = ParityBaseline.Load(path);
        Assert.Equal(["needle-t3/goal-aware", "needle-t3/land-window", "needle-t2/goal-aware", "needle-t2/land-window"], merged.Comparisons.Select(c => c.Name));
        Assert.All(merged.Comparisons.Where(c => c.Case == "needle-t3"), c => Assert.Equal(999, c.Successes));
        Assert.All(merged.Comparisons.Where(c => c.Case == "needle-t2"), c => Assert.InRange(c.Successes, 0, 20));
        // The kept entries keep the build that played them: the header says the baseline mixes two.
        Assert.All(merged.Comparisons.Where(c => c.Case == "needle-t3"), c => Assert.Equal("marked", c.EngineVersion));
        Assert.StartsWith("mixed: ", merged.Version);
        Assert.Contains("marked", merged.Version);
    }

    /// <summary>A baseline that our retired engine recorded: the regression
    /// gate compares XMage against XMage only, so it stops before any game.</summary>
    [Fact]
    public async Task Parity_refuses_a_baseline_another_engine_recorded()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        Assert.Equal(0, await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter()));
        var path = Path.Combine(dir, "baseline.json");
        (ParityBaseline.Load(path) with { Engine = "goldfish" }).Save(path);
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", Endpoint(fake)], new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("'goldfish'", stderr.ToString());
        Assert.Contains("parity-baseline", stderr.ToString());
        Assert.DoesNotContain(fake.Received, m => (string?)m["type"] == "start_game");
    }

    /// <summary>An --only merge would mix two engines in one baseline.</summary>
    [Fact]
    public async Task Parity_baseline_only_refuses_to_merge_into_a_baseline_another_engine_recorded()
    {
        var dir = StaplesGate(runs: 20, secondCase: true);
        var cases = Path.Combine(dir, "cases.json");
        Assert.Equal(0, await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter()));
        var path = Path.Combine(dir, "baseline.json");
        (ParityBaseline.Load(path) with { Engine = "goldfish" }).Save(path);
        var before = File.ReadAllText(path);
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity-baseline", "--cases", cases, "--only", "needle-t2", "--endpoint", Endpoint(fake)], new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("'goldfish'", stderr.ToString());
        Assert.Contains("without --only", stderr.ToString());
        Assert.Equal(before, File.ReadAllText(path));
        Assert.DoesNotContain(fake.Received, m => (string?)m["type"] == "start_game");
    }

    [Fact]
    public async Task A_full_baseline_names_one_build()
    {
        var dir = StaplesGate(runs: 20);

        await BaselineAsync(["--cases", Path.Combine(dir, "cases.json")], new StringWriter(), new StringWriter());

        var baseline = ParityBaseline.Load(Path.Combine(dir, "baseline.json"));
        Assert.DoesNotContain("mixed", baseline.Version);
        Assert.All(baseline.Comparisons, c => Assert.Equal(baseline.Version, c.EngineVersion));
    }

    [Fact]
    public async Task The_baseline_records_both_seeds_of_every_comparison_and_the_deck_hash()
    {
        var dir = StaplesGate(runs: 40);
        var stdout = new StringWriter();

        var exit = await BaselineAsync(["--cases", Path.Combine(dir, "cases.json")], stdout, new StringWriter());

        Assert.Equal(0, exit);
        var baseline = ParityBaseline.Load(Path.Combine(dir, "baseline.json"));
        Assert.Equal("xmage", baseline.Engine);
        Assert.Equal("xmage-1.4.62V1+0123abcd", baseline.Version);
        Assert.Equal(["needle-t3/goal-aware", "needle-t3/land-window"], baseline.Comparisons.Select(c => c.Name));
        Assert.All(baseline.Comparisons, c =>
        {
            Assert.Equal(40, c.Runs);
            Assert.Equal((7L, 8L), (c.BaseSeed, c.FreshSeed));
            Assert.Equal(3, c.Curve.Count);
            Assert.Equal(ParityDeck.Sha(Path.Combine(dir, "decks", "staples.json")), c.DeckSha);
        });

        // The same run again gives the same numbers: the fake is deterministic.
        var first = File.ReadAllText(Path.Combine(dir, "baseline.json"));
        await BaselineAsync(["--cases", Path.Combine(dir, "cases.json")], new StringWriter(), new StringWriter());
        Assert.Equal(
            ParityBaseline.Load(Path.Combine(dir, "baseline.json")).Comparisons.Select(c => (c.Successes, c.FreshSuccesses)),
            JsonSerializer.Deserialize<ParityBaseline>(first, ParityJson.Options)!.Comparisons.Select(c => (c.Successes, c.FreshSuccesses)));
    }

    /// <summary>One deck under two formats plays each case under
    /// its own game rules, and the baseline records the format.</summary>
    [Fact]
    public async Task Parity_baseline_plays_each_case_under_its_format_and_records_it()
    {
        var dir = StaplesGate(runs: 4, secondCase: true);
        EditCases(dir, c => c with { Mulligans = ["goal-aware"], Cases = [c.Cases[0] with { Format = "competitive_brawl" }, c.Cases[1]] });
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["parity-baseline", "--cases", Path.Combine(dir, "cases.json"), "--endpoint", Endpoint(fake)], new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        // The Competitive Brawl case plays first, at 25 life, then the Commander case at 40.
        var lives = fake.Received.Where(m => (string?)m["type"] == "start_game").Select(m => (int)m["start_life"]!).ToList();
        Assert.Equal([25, 40], lives.Distinct());
        Assert.Equal(lives.Count / 2, lives.Count(l => l == 25));
        var baseline = ParityBaseline.Load(Path.Combine(dir, "baseline.json"));
        Assert.Equal(["competitive_brawl", null], baseline.Comparisons.Select(c => c.Format));
    }

    [Fact]
    public async Task Parity_capture_is_no_longer_a_command()
    {
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity-capture"], new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.DoesNotContain("parity-capture", MtgArchive.Tools.EngineGate.Cli.Usage);
    }

    [Fact]
    public async Task An_unknown_gate_option_is_a_usage_error()
    {
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--bogus"], new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("unknown option '--bogus'", stderr.ToString());
    }

    [Theory]
    [InlineData("localhost", "is not host:port")]
    [InlineData("localhost:port", "is not host:port")]
    [InlineData("127.0.0.1:1,127.0.0.1:1", "name one process")]
    [InlineData("localhost:1,127.0.0.1:1", "name one process")]
    [InlineData("a:1,b:2,c:3", "at most two")]
    public async Task A_bad_endpoint_is_a_usage_error(string endpoints, string message)
    {
        var stderr = new StringWriter();
        string[] args = ["determinism", .. endpoints.Split(',').SelectMany(e => new[] { "--endpoint", e })];

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(args, new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains(message, stderr.ToString());
        Assert.Contains("usage:", stderr.ToString());
    }

    [Fact]
    public void Two_ports_on_one_host_are_two_processes()
    {
        Assert.False(ParityOptions.SameProcess(ParityOptions.EndpointOptions("127.0.0.1:7070"), ParityOptions.EndpointOptions("localhost:7071")));
        Assert.True(ParityOptions.SameProcess(ParityOptions.EndpointOptions("LOCALHOST:7070"), ParityOptions.EndpointOptions("[::1]:7070")));
    }

    [Fact]
    public async Task Parity_only_prints_its_rows_and_leaves_the_report()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter());
        RewriteBaseline(dir, successes: 0, freshSuccesses: 0);
        File.WriteAllText(Path.Combine(dir, "report.md"), "the full gate's report");
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        await using var noCap = new FakeSidecar(game => game.OverAsync("horizon", 3));
        var stdout = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", Endpoint(fake), "--only", "needle-t3", "--cap-seeds", "3"], stdout, new StringWriter());
        await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", Endpoint(noCap), "--only", "needle-t3", "--cap-seeds", "0"], new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        Assert.Contains("needle-t3/goal-aware: baseline 0/20", stdout.ToString());
        Assert.Equal("the full gate's report", File.ReadAllText(Path.Combine(dir, "report.md")));
        // The cap games feed only report.md: --cap-seeds 3 plays no more games than 0.
        Assert.Equal(noCap.Received.Count(m => (string?)m["type"] == "start_game"), fake.Received.Count(m => (string?)m["type"] == "start_game"));
    }

    [Fact]
    public async Task Parity_fails_a_comparison_the_sidecar_misses_on_both_seeds_and_writes_the_report()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        Assert.Equal(0, await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter()));
        RewriteBaseline(dir, successes: 20, freshSuccesses: 20);
        // Every fake game ends at once with no goal met: XMage's floor is 0.
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        var endpoint = $"{fake.Options().Host}:{fake.Options().Port}";
        var stdout = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", endpoint, "--cap-seeds", "3"], stdout, new StringWriter());

        var report = File.ReadAllText(Path.Combine(dir, "report.md"));
        Assert.Equal(6, exit);
        Assert.Contains("# Engine regression report", report);
        Assert.Contains("needle-t3/goal-aware", report);
        Assert.Contains("0 of 3 runs reach the cap", report);
        Assert.Contains("| fail |", report);
    }

    private static void RewriteBaseline(string dir, int successes, int freshSuccesses)
    {
        var path = Path.Combine(dir, "baseline.json");
        var loaded = ParityBaseline.Load(path);
        (loaded with { Comparisons = [.. loaded.Comparisons.Select(c => c with { Successes = successes, FreshSuccesses = freshSuccesses })] }).Save(path);
    }

    [Fact]
    public async Task Parity_passes_a_comparison_that_fails_at_the_base_seed_but_passes_at_the_fresh_seed()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter());
        RewriteBaseline(dir, successes: 20, freshSuccesses: 0);
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", $"{fake.Options().Host}:{fake.Options().Port}", "--cap-seeds", "0"], new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        Assert.Contains("| pass (fresh seed) |", File.ReadAllText(Path.Combine(dir, "report.md")));
        // The fresh seed (8) played: its derived seeds reached the sidecar.
        Assert.Contains(fake.Received, m => (string?)m["type"] == "start_game" && (long?)m["seed"] == MtgArchive.Simulation.Engine.RunSeeds.Derive(8, 0));
    }

    [Fact]
    public async Task Parity_passes_equal_floors()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter());
        // Rewrite the baseline to 0 successes, as the fake plays.
        var baselinePath = Path.Combine(dir, "baseline.json");
        var zero = ParityBaseline.Load(baselinePath);
        (zero with { Comparisons = [.. zero.Comparisons.Select(c => c with { Successes = 0, FreshSuccesses = 0 })] }).Save(baselinePath);
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", $"{fake.Options().Host}:{fake.Options().Port}", "--cap-seeds", "0"], new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        Assert.Contains("every comparison passes", File.ReadAllText(Path.Combine(dir, "report.md")));
    }

    /// <summary>Phase 3C, Review Focus 1: a deck file that changed after its baseline is refused before any game.</summary>
    [Fact]
    public async Task Parity_refuses_a_stale_baseline()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter());
        File.AppendAllText(Path.Combine(dir, "decks", "staples.json"), "\n");
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", $"{fake.Options().Host}:{fake.Options().Port}"], new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("stale", stderr.ToString());
        Assert.DoesNotContain(fake.Received, m => (string?)m["type"] == "start_game");
    }

    /// <summary>Phase 3C, Review Focus 5: engine errors fail a comparison and never read as a low floor.</summary>
    [Fact]
    public async Task Parity_reports_engine_errors()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter());
        await using var fake = new FakeSidecar(game => game.OverAsync("engine_error", 0, error: "boom"));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", $"{fake.Options().Host}:{fake.Options().Port}", "--cap-seeds", "0"], new StringWriter(), new StringWriter());

        Assert.Equal(6, exit);
        Assert.Contains("| engine errors |", File.ReadAllText(Path.Combine(dir, "report.md")));
    }

    /// <summary>Review finding: a baseline whose games end in engine errors
    /// would deflate the reference floor, so parity-baseline writes nothing.</summary>
    [Theory]
    [InlineData(7L)]
    [InlineData(8L)]
    public async Task Parity_baseline_refuses_engine_errors(long errorSeed)
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        var errorSeeds = Enumerable.Range(0, 20).Select(i => RunSeeds.Derive(errorSeed, i)).ToHashSet();
        await using var fake = new FakeSidecar(game => errorSeeds.Contains((int)game.Start["seed"]!)
            ? game.OverAsync("engine_error", 0, error: "boom")
            : game.OverAsync("horizon", 3));
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity-baseline", "--cases", cases, "--endpoint", Endpoint(fake)], new StringWriter(), stderr);

        Assert.Equal(8, exit);
        Assert.Contains("engine error", stderr.ToString());
        Assert.False(File.Exists(Path.Combine(dir, "baseline.json")));
    }

    /// <summary>Engine errors only at the fresh seed: they must not turn a base-seed fail into a pass.</summary>
    [Fact]
    public async Task Parity_reports_engine_errors_at_the_fresh_seed()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        await BaselineAsync(["--cases", cases], new StringWriter(), new StringWriter());
        RewriteBaseline(dir, successes: 20, freshSuccesses: 0);
        var freshSeeds = Enumerable.Range(0, 20).Select(i => RunSeeds.Derive(8, i)).ToHashSet();
        await using var fake = new FakeSidecar(game => freshSeeds.Contains((int)game.Start["seed"]!)
            ? game.OverAsync("engine_error", 0, error: "boom")
            : game.OverAsync("horizon", 3));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--endpoint", Endpoint(fake), "--cap-seeds", "0"], new StringWriter(), new StringWriter());

        Assert.Equal(6, exit);
        var report = File.ReadAllText(Path.Combine(dir, "report.md"));
        Assert.Contains("| engine errors |", report);
        Assert.Contains("| 0; fresh 20 |", report);
    }

    /// <summary>The report states the runs the gate played, not the cases' default.</summary>
    [Fact]
    public async Task Parity_reports_the_runs_of_a_runs_override()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        await BaselineAsync(["--cases", cases, "--runs", "10"], new StringWriter(), new StringWriter());
        RewriteBaseline(dir, successes: 0, freshSuccesses: 0);
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["parity", "--cases", cases, "--runs", "10", "--endpoint", Endpoint(fake), "--cap-seeds", "0"], new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        Assert.Contains("plays 10 games per engine", File.ReadAllText(Path.Combine(dir, "report.md")));
    }

    private static string Digest(long seed) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(seed)));

    /// <summary>Phase 3C, Review Focus 3: one seed that plays differently in the second process fails the row and is named.</summary>
    [Fact]
    public async Task Determinism_counts_the_seeds_whose_digests_differ()
    {
        var dir = StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        var odd = RunSeeds.Derive(7, 2); // the cases' base seed is 7
        await using var first = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));
        await using var second = new FakeSidecar(game =>
        {
            var seed = (long)game.Start["seed"]!;
            return game.OverAsync("horizon", 3, digest: seed == odd ? new string('f', 64) : Digest(seed));
        });
        var stdout = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["determinism", "--cases", cases, "--seeds", "5",
             "--endpoint", $"{first.Options().Host}:{first.Options().Port}", "--endpoint", $"{second.Options().Host}:{second.Options().Port}"],
            stdout, new StringWriter());

        Assert.Equal(7, exit);
        var table = File.ReadAllText(Path.Combine(dir, "determinism.md"));
        Assert.Contains("| staples | 5 | 1 | 0 | 2 | fail |", table);
    }

    /// <summary>A game that ends in an engine error proves nothing about play: it fails the row.</summary>
    [Fact]
    public async Task Determinism_fails_a_row_whose_games_end_in_engine_errors()
    {
        var dir = StaplesGate(runs: 20);
        await using var first = new FakeSidecar(game => game.OverAsync("engine_error", 0, error: "boom"));
        await using var second = new FakeSidecar(game => game.OverAsync("engine_error", 0, error: "boom"));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["determinism", "--cases", Path.Combine(dir, "cases.json"), "--seeds", "5", "--endpoint", Endpoint(first), "--endpoint", Endpoint(second)],
            new StringWriter(), new StringWriter());

        Assert.Equal(7, exit);
        Assert.Contains("| staples | 5 | 0 | 5 | – | fail |", File.ReadAllText(Path.Combine(dir, "determinism.md")));
    }

    [Fact]
    public async Task Leak_passes_when_every_digest_matches()
    {
        var dir = StaplesGate(runs: 20);
        await using var first = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));
        await using var second = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["leak", "--cases", Path.Combine(dir, "cases.json"), "--seeds", "5",
             "--endpoint", $"{first.Options().Host}:{first.Options().Port}", "--endpoint", $"{second.Options().Host}:{second.Options().Port}"],
            new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        Assert.Contains("| staples | 5 | 0 | 0 | – | pass |", File.ReadAllText(Path.Combine(dir, "leak.md")));
    }

    [Fact]
    public async Task Leak_fails_when_a_digest_differs_and_warns_that_it_cannot_check_the_switches()
    {
        var dir = StaplesGate(runs: 20);
        await using var first = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));
        await using var second = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: new string('f', 64)));
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["leak", "--cases", Path.Combine(dir, "cases.json"), "--seeds", "5", "--endpoint", Endpoint(first), "--endpoint", Endpoint(second)],
            new StringWriter(), stderr);

        Assert.Equal(7, exit);
        Assert.Contains("| staples | 5 | 5 | 0 | 0 | fail |", File.ReadAllText(Path.Combine(dir, "leak.md")));
        Assert.Contains("the gate cannot check either", stderr.ToString());
    }

    [Fact]
    public void Only_names_a_deck_or_one_of_its_rows()
    {
        var goal = new GoalPredicate.Cast("Sol Ring", 1);
        var rules = GameRules.Commander;
        DigestPlay[] plays = [new("deck", goal, rules, null, "goal-aware-greedy"), new("deck-disrupted", goal, rules, null, "goal-aware-greedy")];

        Assert.Equal(2, DigestCommands.SelectPlays(plays, "deck", "deck").Count);
        Assert.Equal(["deck-disrupted"], DigestCommands.SelectPlays(plays, "deck", "deck-disrupted").Select(p => p.Label));
        Assert.Empty(DigestCommands.SelectPlays(plays, "deck", "other"));
    }

    [Fact]
    public void Only_the_leak_check_starts_its_second_sidecar_with_the_game_copies_changes_off()
    {
        Assert.Equal(SidecarContainer.CopiesOff, DigestCommands.SecondSwitches(DigestCheck.Leak));
        Assert.Null(DigestCommands.SecondSwitches(DigestCheck.Determinism));
    }

    [Fact]
    public async Task Determinism_refuses_two_sidecars_on_different_builds()
    {
        var dir = StaplesGate(runs: 20);
        await using var first = new FakeSidecar(game => game.OverAsync("horizon", 3));
        await using var second = new FakeSidecar(game => game.OverAsync("horizon", 3)) { Version = "xmage-1.4.62V1+ffffffff" };
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["determinism", "--cases", Path.Combine(dir, "cases.json"), "--seeds", "5", "--endpoint", Endpoint(first), "--endpoint", Endpoint(second)],
            new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("different builds", stderr.ToString());
        Assert.False(File.Exists(Path.Combine(dir, "determinism.md")));
        Assert.DoesNotContain(first.Received, m => (string?)m["type"] == "start_game");
    }

    /// <summary>A case's format sets the game rules each digest gate plays under; no format is Commander.</summary>
    [Theory]
    [InlineData(null, 40, true, 1)]
    [InlineData("brawl", 25, false, 1)]
    [InlineData("competitive_brawl", 25, false, 0)]
    public async Task Determinism_plays_each_deck_under_the_game_rules_of_its_case_format(string? format, int life, bool draws, int freeMulligans)
    {
        var dir = StaplesGate(runs: 20);
        EditCases(dir, c => c with { Cases = [.. c.Cases.Select(x => x with { Format = format })] });
        await using var first = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));
        await using var second = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["determinism", "--cases", Path.Combine(dir, "cases.json"), "--seeds", "3", "--endpoint", Endpoint(first), "--endpoint", Endpoint(second)],
            new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        foreach (var fake in new[] { first, second })
        {
            var starts = fake.Received.Where(m => (string?)m["type"] == "start_game").ToList();
            Assert.Equal(3, starts.Count);
            Assert.All(starts, s =>
            {
                Assert.Equal(life, (int)s["start_life"]!);
                Assert.Equal(draws, (bool)s["starting_player_draws"]!);
                Assert.Equal(freeMulligans, (int)s["free_mulligans"]!);
                Assert.Equal(format is null, (bool)s["commander_damage"]!);
            });
        }
    }

    /// <summary>A parity deck with cases in two game rules plays
    /// in the digest gates once under each, from the first case of each; a
    /// deck with cases in one game rules keeps its one row and its name.</summary>
    [Fact]
    public async Task The_digest_gates_play_a_parity_deck_once_under_each_game_rules_of_its_cases()
    {
        var dir = StaplesGate(runs: 4, secondCase: true);
        EditCases(dir, c => c with
        {
            Cases = [c.Cases[0] with { Format = "brawl" }, c.Cases[1] with { Format = "competitive_brawl" }, c.Cases[0] with { Name = "needle-t3-standard", Format = "standard_brawl" }],
        });
        await using var first = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));
        await using var second = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["leak", "--cases", Path.Combine(dir, "cases.json"), "--seeds", "2", "--endpoint", Endpoint(first), "--endpoint", Endpoint(second)],
            new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        var table = File.ReadAllText(Path.Combine(dir, "leak.md"));
        // Standard Brawl plays under the Brawl game rules, so it adds no row.
        Assert.Contains("| staples (brawl) | 2 | 0 | 0 | – | pass |", table);
        Assert.Contains("| staples (competitive_brawl) | 2 | 0 | 0 | – | pass |", table);
        Assert.DoesNotContain("standard_brawl", table);
        Assert.Equal([1, 1, 0, 0], first.Received.Where(m => (string?)m["type"] == "start_game").Select(m => (int)m["free_mulligans"]!));
    }

    /// <summary>A deck with its own goal has no parity case. The
    /// digest gates play it under its goal and format; the baseline skips it.</summary>
    [Fact]
    public async Task A_determinism_only_deck_plays_in_the_digest_gates_under_its_format_and_never_in_parity()
    {
        var dir = StaplesGate(runs: 4);
        File.Copy(Path.Combine(dir, "decks", "staples.json"), Path.Combine(dir, "decks", "seek.json"));
        EditCases(dir, c => c with
        {
            Decks = [.. c.Decks, new DeckSource("seek", "golden", JsonNode.Parse("""{"kind":"cast","card":"Sol Ring","by_turn":2}""")!.AsObject(), "brawl")],
        });
        await using var first = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));
        await using var second = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["determinism", "--cases", Path.Combine(dir, "cases.json"), "--seeds", "3", "--endpoint", Endpoint(first), "--endpoint", Endpoint(second)],
            new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        var table = File.ReadAllText(Path.Combine(dir, "determinism.md"));
        Assert.Contains("| staples | 3 | 0 | 0 | – | pass |", table);
        Assert.Contains("| seek | 3 | 0 | 0 | – | pass |", table);
        // Three Commander games, then three Brawl games, in each process.
        Assert.Equal([40, 40, 40, 25, 25, 25], first.Received.Where(m => (string?)m["type"] == "start_game").Select(m => (int)m["start_life"]!));

        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3));
        Assert.Equal(0, await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["parity-baseline", "--cases", Path.Combine(dir, "cases.json"), "--endpoint", Endpoint(fake)], new StringWriter(), new StringWriter()));
        Assert.All(ParityBaseline.Load(Path.Combine(dir, "baseline.json")).Comparisons, c => Assert.Equal("staples", c.Deck));
        Assert.DoesNotContain(fake.Received, m => (int?)m["start_life"] == 25);
    }

    [Theory]
    [InlineData(true, false, "seek: a deck with its own goal is determinism-only; no case may name it")]
    [InlineData(false, true, "seek: a deck format needs a deck goal; a parity deck's format is on each case")]
    public void A_determinism_only_deck_that_a_case_names_or_that_lacks_a_goal_stops_the_gate(bool named, bool formatOnly, string message)
    {
        var dir = StaplesGate(runs: 4);
        var goal = formatOnly ? null : JsonNode.Parse("""{"kind":"cast","card":"Sol Ring","by_turn":2}""")!.AsObject();
        EditCases(dir, c => c with
        {
            Decks = [.. c.Decks, new DeckSource("seek", "golden", goal, "brawl")],
            Cases = named ? [.. c.Cases, c.Cases[0] with { Name = "seek-t3", Deck = "seek" }] : c.Cases,
        });

        var error = Assert.Throws<InvalidDataException>(() => ParityCases.Load(Path.Combine(dir, "cases.json")));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void A_case_with_an_unknown_format_stops_the_gate_when_the_cases_load()
    {
        var dir = StaplesGate(runs: 20);
        EditCases(dir, c => c with { Cases = [c.Cases[0] with { Format = "vintage" }] });

        var error = Assert.Throws<InvalidDataException>(() => ParityCases.Load(Path.Combine(dir, "cases.json")));

        Assert.Contains("needle-t3: unknown format 'vintage'", error.Message);
    }

    /// <summary>Each disrupted case gets its own digest row, named
    /// for the case, and plays its own profile. The deck rows keep their
    /// labels and play no profile.</summary>
    [Fact]
    public async Task The_digest_gates_play_each_disrupted_case_under_its_own_profile()
    {
        var dir = StaplesGate(runs: 4, secondCase: true);
        EditCases(dir, c => c with
        {
            Cases = [c.Cases[0], c.Cases[1] with { Name = "needle-t2-disrupted", Disruption = JsonNode.Parse("""[{"kind":"counterspell","turn":2}]""")!.AsArray() }],
        });
        await using var first = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));
        await using var second = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["leak", "--cases", Path.Combine(dir, "cases.json"), "--seeds", "2", "--endpoint", Endpoint(first), "--endpoint", Endpoint(second)],
            new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        var rows = File.ReadAllLines(Path.Combine(dir, "leak.md")).Where(l => l.StartsWith("| 20", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains("| staples | 2 | 0 | 0 | – | pass |", rows[0]);
        Assert.Contains("| needle-t2-disrupted | 2 | 0 | 0 | – | pass |", rows[1]);
        // The deck row watches no casts; the counterspell case does.
        foreach (var fake in new[] { first, second })
        {
            Assert.Equal([false, false, true, true], fake.Received.Where(m => (string?)m["type"] == "start_game").Select(m => (bool)m["watch_casts"]!));
        }
    }

    /// <summary>An interactive board-wipe case gets its own
    /// digest row and plays against the scripted opponent.</summary>
    [Fact]
    public async Task The_digest_gates_play_an_interactive_board_wipe_case()
    {
        var dir = StaplesGate(runs: 4);
        EditCases(dir, c => c with { Cases = [c.Cases[0], c.Cases[0] with { Name = "needle-t3-interactive", Disruption = JsonNode.Parse("""[{"kind":"board_wipe","turn":2}]""")!.AsArray(), DisruptionMode = "interactive" }] });
        await using var first = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));
        await using var second = new FakeSidecar(game => game.OverAsync("horizon", 3, digest: Digest((long)game.Start["seed"]!)));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["determinism", "--cases", Path.Combine(dir, "cases.json"), "--seeds", "2", "--endpoint", Endpoint(first), "--endpoint", Endpoint(second)],
            new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        var rows = File.ReadAllLines(Path.Combine(dir, "determinism.md")).Where(l => l.StartsWith("| 20", StringComparison.Ordinal)).ToList();
        Assert.Contains(rows, r => r.Contains("| needle-t3-interactive |", StringComparison.Ordinal));
        Assert.Contains(first.Received, m => (string?)m["type"] == "start_game" && m["opponent"] is not null);
    }

    [Fact]
    public async Task Determinism_refuses_a_deck_that_no_case_names()
    {
        var dir = StaplesGate(runs: 20);
        EditCases(dir, c => c with { Decks = [.. c.Decks, new DeckSource("orphan", "nowhere")] });
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["determinism", "--cases", Path.Combine(dir, "cases.json"), "--endpoint", "127.0.0.1:1", "--endpoint", "127.0.0.1:2"],
            new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("orphan: no case", stderr.ToString());
        Assert.False(File.Exists(Path.Combine(dir, "determinism.md")));
    }
}
