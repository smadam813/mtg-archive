using System.Text.Json;
using System.Text.Json.Nodes;
using MtgArchive.Simulation.Engine;
using MtgArchive.Tools.EngineGate.Diagnosis;
using MtgArchive.Tools.EngineGate.Parity;

namespace MtgArchive.Tests;

/// <summary>The trace command of the Obeka turn-4 diagnosis: the log parsers and the classes.</summary>
public sealed class EngineGateTraceTests
{
    private static GameLogEntry Line(int turn, string message) => new(turn, TurnStep.Main, message);

    [Fact]
    public void Etb_counts_each_land_tapped_and_untapped()
    {
        GameLogEntry[] log = [Line(1, "ETB Island untapped"), Line(2, "ETB Watery Grave tapped"), Line(3, "ETB Watery Grave untapped"), Line(3, "RESOLVED Opt")];

        var counts = TraceAnalysis.Etb(log);

        Assert.Equal(new EtbCount(0, 1), counts["Island"]);
        Assert.Equal(new EtbCount(1, 1), counts["Watery Grave"]);
        Assert.Equal(2, counts.Count);
    }

    /// <summary>Phase 3D, Review Focus 2: a name with a comma stays one name.</summary>
    [Fact]
    public void An_end_line_splits_on_known_names_not_on_commas()
    {
        string[] lands = ["Takenuma, Abandoned Mire", "Island", "Blood Crypt"];

        var bf = TraceAnalysis.EndLands("END life=38 lib=90 hand=[Opt] bf=[Blood Crypt(T), Island, Obeka, Brute Chronologist, Takenuma, Abandoned Mire(T)] gy=[Polluted Delta]", lands);

        Assert.Equal([("Blood Crypt", true), ("Island", false), ("Takenuma, Abandoned Mire", true)], bf.Battlefield);
        Assert.Empty(bf.Graveyard);
    }

    private static readonly Dictionary<string, BoardSource> Sources = new()
    {
        ["Island"] = new(["U"], 1),
        ["Swamp"] = new(["B"], 1),
        ["Badlands"] = new(["B", "R"], 1),
        ["Sol Ring"] = new(["C"], 2),
    };

    private static readonly string[] ObekaPips = ["U", "B", "R"];

    [Fact]
    public void A_failure_is_land_short_color_short_or_rules_short()
    {
        Assert.Equal(FailureClass.LandShort, TraceAnalysis.Classify(["Island", "Swamp", "Badlands"], Sources, ObekaPips, manaValue: 4));
        Assert.Equal(FailureClass.ColorShort, TraceAnalysis.Classify(["Island", "Swamp", "Swamp", "Island"], Sources, ObekaPips, manaValue: 4));
        Assert.Equal(FailureClass.RulesShort, TraceAnalysis.Classify(["Island", "Swamp", "Badlands", "Island"], Sources, ObekaPips, manaValue: 4));
    }

    /// <summary>Review finding: one dual land pays one pip, not both of its colors.</summary>
    [Fact]
    public void A_dual_land_pays_one_pip()
    {
        Assert.Equal(FailureClass.ColorShort, TraceAnalysis.Classify(["Badlands", "Island", "Island", "Island"], Sources, ObekaPips, manaValue: 4));
    }

    /// <summary>Review finding: Sol Ring's two mana count toward the mana value, but pay no colored pip.</summary>
    [Fact]
    public void Sol_ring_counts_two_mana_and_no_color()
    {
        Assert.Equal(FailureClass.RulesShort, TraceAnalysis.Classify(["Island", "Swamp", "Badlands", "Sol Ring"], Sources, ObekaPips, manaValue: 5));
        Assert.Equal(FailureClass.ColorShort, TraceAnalysis.Classify(["Island", "Badlands", "Sol Ring"], Sources, ObekaPips, manaValue: 4));
        Assert.Equal(FailureClass.LandShort, TraceAnalysis.Classify(["Island", "Badlands", "Sol Ring", "Alpha Myr"], Sources, ObekaPips, manaValue: 5));
    }

    /// <summary>A trace summary's classes count every failing run without an engine error, and no other run.</summary>
    [Fact]
    public void A_summary_whose_classes_miss_a_failure_throws()
    {
        Dictionary<FailureClass, int> classes = new() { [FailureClass.LandShort] = 3, [FailureClass.Unknown] = 1 };
        TraceSummary Summary(int successes, int errors) => new("x", "xmage", "c", "goal-aware", "sha", null, [], 10, successes, errors, 0, 0, 0,
            new Dictionary<string, EtbCount>(), new Dictionary<string, int>(), classes, [], null);

        Assert.Equal(4, Summary(successes: 5, errors: 1).Classes.Values.Sum());
        Assert.Throws<ArgumentException>(() => Summary(successes: 5, errors: 0));
    }

    /// <summary>Run 68 of the x0 trace: four lands, none of them blue, so the run is short of a color.</summary>
    [Fact]
    public void Run_68_of_the_obeka_trace_is_color_short()
    {
        var deck = ParityDeck.Load(Path.Combine(RepoRoot.Find(), "eval", "parity", "decks", "dq2-obeka.json"));
        var goalCard = deck.Deck.First(c => c.Answers("Obeka, Brute Chronologist"));
        var pips = TraceAnalysis.Pips(goalCard.ManaCost);
        var sources = TraceAnalysis.Sources(deck.Deck, deck.FeatureProfiles);
        const string end = "END life=40 lib=88 hand=[Sliver Construct, Alpha Myr, Alpha Myr] bf=[Alpha Myr, Dragonskull Summit(T), Graven Cairns, Metallic Sliver, Metallic Sliver, Mountain(T), Sliver Construct, Vault of Whispers(T)] gy=[]";

        var lands = TraceAnalysis.EndLands(end, [.. sources.Keys]).Battlefield;

        Assert.Equal(["U", "B", "R"], pips);
        Assert.Equal(["Dragonskull Summit", "Graven Cairns", "Mountain", "Vault of Whispers"], lands.Select(l => l.Land));
        Assert.Equal(FailureClass.ColorShort, TraceAnalysis.Classify(lands.Select(l => l.Land), sources, pips, (int)goalCard.ManaValue));
    }

    /// <summary>Run 37 of the x0 trace: three lands and Sol Ring make five mana and each color, so the run is not land-short.</summary>
    [Fact]
    public void Run_37_of_the_obeka_trace_counts_sol_ring()
    {
        var deck = ParityDeck.Load(Path.Combine(RepoRoot.Find(), "eval", "parity", "decks", "dq2-obeka.json"));
        var sources = TraceAnalysis.Sources(deck.Deck, deck.FeatureProfiles);
        const string end = "END life=39 lib=86 hand=[Sunken Hollow, Underground Sea, Windswept Heath] bf=[Alpha Myr, Badlands, Cascade Bluffs, Metallic Sliver, Metallic Sliver, Metallic Sliver, Riverpyre Verge, Sol Ring] gy=[Fiery Islet, Marsh Flats]";

        var board = TraceAnalysis.EndLands(end, [.. sources.Keys]).Battlefield.Select(b => b.Land).ToList();

        Assert.Equal(2, sources["Sol Ring"].Amount);
        Assert.Equal(["C"], sources["Sol Ring"].Colors);
        Assert.False(sources.ContainsKey("Dark Ritual"));
        Assert.Equal(["Badlands", "Cascade Bluffs", "Riverpyre Verge", "Sol Ring"], board);
        Assert.Equal(FailureClass.RulesShort, TraceAnalysis.Classify(board, sources, ["U", "B", "R"], manaValue: 4));
    }

    /// <summary>Review finding: an MDFC's land face counts under its card name.</summary>
    [Fact]
    public void Lands_by_turn_names_an_mdfc_land_face_by_its_card()
    {
        var mdfc = TestDeckCards.Spell("Sink into Stupor // Soporific Springs", "{1}{U}{U}", "Instant") with
        {
            BackFace = TestDeckCards.Land("Soporific Springs", "U") with { IsBasic = false, Supertypes = [] },
        };
        var lands = TraceAnalysis.LandNames([mdfc, TestDeckCards.Land("Island", "U")]);
        GameLogEntry[] log =
        [
            Line(3, "END life=40 lib=90 hand=[] bf=[Island] gy=[]"),
            Line(4, "END life=40 lib=89 hand=[] bf=[Soporific Springs(T), Island] gy=[]"),
            Line(5, "END life=40 lib=88 hand=[] bf=[Soporific Springs, Island, Swamp] gy=[]"),
        ];

        var set = TraceAnalysis.LandsByTurn(log, 4, lands);

        Assert.Equal(["Island", "Sink into Stupor // Soporific Springs"], set.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_trace_on_xmage_reads_the_etb_lines_and_compares_with_another_label()
    {
        var dir = EngineGateParityTests.StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");

        // A JSON node has one parent: each game needs its own log.
        static JsonArray Log() => new(
            FakeViews.Log(1, "main", "ETB Island tapped"),
            FakeViews.Log(2, "main", "DIAG pay-life Watery Grave"),
            FakeViews.Log(3, "end", "END life=40 lib=90 hand=[] bf=[] gy=[]"));
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3, log: Log()));
        await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["trace", "--cases", cases, "--case", "needle-t3", "--runs", "20", "--label", "o0", "--endpoint", $"{fake.Options().Host}:{fake.Options().Port}"],
            new StringWriter(), new StringWriter());

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["trace", "--cases", cases, "--case", "needle-t3", "--runs", "20", "--label", "x0", "--compare", "o0", "--endpoint", $"{fake.Options().Host}:{fake.Options().Port}"],
            new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        var summary = JsonSerializer.Deserialize<TraceSummary>(File.ReadAllText(Path.Combine(dir, "diagnosis", "x0.json")), ParityJson.Options)!;
        Assert.Equal(0, summary.Successes);
        Assert.Equal(new EtbCount(20, 0), summary.Etb["Island"]);
        Assert.Equal(20, summary.PayLife["Watery Grave"]);
        Assert.False(string.IsNullOrEmpty(summary.EngineVersion));
        Assert.Equal(20, summary.Classes[FailureClass.LandShort]);
        Assert.NotNull(summary.Against);
        Assert.Equal("o0", summary.Against!.Label);
        var failures = File.ReadAllText(Path.Combine(dir, "diagnosis", "x0.failures.txt"));
        Assert.Contains("# run 0, seed ", failures);
        Assert.Contains(", class LandShort\n", failures);
    }

    /// <summary>A trace plays its case under the case's format, so a log can show the Brawl start life.</summary>
    [Fact]
    public async Task A_trace_plays_its_case_under_the_game_rules_of_its_format()
    {
        var dir = EngineGateParityTests.StaplesGate(runs: 4);
        var cases = Path.Combine(dir, "cases.json");
        var edited = ParityCases.Load(cases);
        ParityJson.Write(cases, edited with { Cases = [edited.Cases[0] with { Format = "brawl" }] });
        await using var fake = new FakeSidecar(g => g.OverAsync("horizon", 3));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["trace", "--cases", cases, "--case", "needle-t3", "--runs", "4", "--label", "brawl", "--endpoint", $"{fake.Options().Host}:{fake.Options().Port}"],
            new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        var starts = fake.Received.Where(m => (string?)m["type"] == "start_game").ToList();
        Assert.Equal(4, starts.Count);
        Assert.All(starts, s => Assert.Equal((25, false, false), ((int)s["start_life"]!, (bool)s["starting_player_draws"]!, (bool)s["commander_damage"]!)));
    }

    /// <summary>Item 1: the failures file keeps up to 20 runs of each class, not the first 20 of all.</summary>
    [Fact]
    public async Task The_failures_file_keeps_twenty_runs_per_class()
    {
        var dir = EngineGateParityTests.StaplesGate(runs: 50);
        var cases = Path.Combine(dir, "cases.json");

        // Even games end land-short (an empty board), odd games have no END line (unknown).
        var game = 0;
        JsonArray Log() => Interlocked.Increment(ref game) % 2 == 0
            ? new(FakeViews.Log(3, "end", "END life=40 lib=90 hand=[] bf=[] gy=[]"))
            : new(FakeViews.Log(1, "main", "ETB Island untapped"));
        await using var fake = new FakeSidecar(g => g.OverAsync("horizon", 3, log: Log()));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["trace", "--cases", cases, "--case", "needle-t3", "--runs", "50", "--label", "x1", "--endpoint", $"{fake.Options().Host}:{fake.Options().Port}"],
            new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        var failures = File.ReadAllText(Path.Combine(dir, "diagnosis", "x1.failures.txt"));
        Assert.Equal(20, CountOf(failures, ", class LandShort\n"));
        Assert.Equal(20, CountOf(failures, ", class Unknown\n"));

        static int CountOf(string text, string part) => (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;
    }

    [Fact]
    public void Pay_life_counts_each_card()
    {
        GameLogEntry[] log = [Line(1, "DIAG pay-life Watery Grave"), Line(2, "DIAG pay-life Watery Grave"), Line(2, "DIAG pay-life Blood Crypt"), Line(3, "ETB Island tapped")];

        var counts = TraceAnalysis.PayLife(log);

        Assert.Equal(2, counts["Watery Grave"]);
        Assert.Equal(1, counts["Blood Crypt"]);
        Assert.Equal(2, counts.Count);
    }

    /// <summary>Item 2: an ETB switch with no ETB line means the image lacks the switch.</summary>
    [Fact]
    public void Switches_missing_names_an_etb_switch_that_logged_nothing()
    {
        var none = new Dictionary<string, EtbCount>();
        var some = new Dictionary<string, EtbCount> { ["Island"] = new(1, 0) };
        const string switches = "-Dmtgarchive.diag.etb=true -Dmtgarchive.diag.payLife=true";

        var noPay = new Dictionary<string, int>();
        var paid = new Dictionary<string, int> { ["Watery Grave"] = 1 };

        Assert.Contains("mtgarchive.diag.etb=true", TraceAnalysis.SwitchesMissing(switches, none, paid));
        Assert.Null(TraceAnalysis.SwitchesMissing(switches, some, paid));
        Assert.Null(TraceAnalysis.SwitchesMissing(null, none, noPay));
        Assert.Null(TraceAnalysis.SwitchesMissing("-Dmtgarchive.diag.payLife=true", none, paid));
    }

    /// <summary>Review finding: a pay-life switch with no pay-life line means the image lacks the switch.</summary>
    [Fact]
    public void Switches_missing_names_a_pay_life_switch_that_logged_nothing()
    {
        var some = new Dictionary<string, EtbCount> { ["Island"] = new(1, 0) };

        Assert.Contains("mtgarchive.diag.payLife=true", TraceAnalysis.SwitchesMissing("-Dmtgarchive.diag.etb=true -Dmtgarchive.diag.payLife=true", some, new Dictionary<string, int>()));
    }

    /// <summary>Item 4: --compare with no such trace stops before any game.</summary>
    [Fact]
    public async Task A_missing_compare_label_exits_1_and_writes_no_summary()
    {
        var dir = EngineGateParityTests.StaplesGate(runs: 20);
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["trace", "--cases", Path.Combine(dir, "cases.json"), "--case", "needle-t3", "--runs", "20", "--label", "o1", "--compare", "nothing"], new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("--compare", stderr.ToString());
        Assert.False(File.Exists(Path.Combine(dir, "diagnosis", "o1.json")));
    }

    /// <summary>Item 5: a named sidecar runs with its own switches, and an empty value sets nothing.</summary>
    [Theory]
    [InlineData("--switches needs a new container", "--switches", "-Dmtgarchive.diag.etb=true", "--endpoint", "127.0.0.1:7070")]
    [InlineData("--switches needs a value", "--switches", " ")]
    public void Trace_options_reject_switches_they_cannot_apply(string error, params string[] extra)
    {
        TraceOptions.Parse(["--case", "needle-t3", .. extra], out var errors);

        Assert.Contains(errors, e => e.StartsWith(error, StringComparison.Ordinal));
    }

    /// <summary>Review finding: a label names one file in the diagnosis directory, and not the compared trace.</summary>
    [Theory]
    [InlineData("--label is letters", "--label", "../baseline")]
    [InlineData("--label is letters", "--label", ".hidden")]
    [InlineData("--compare names another trace", "--label", "o4", "--compare", "o4")]
    [InlineData("--compare is a trace label", "--label", "o4", "--compare", "../baseline")]
    public void Trace_options_reject_a_bad_label(string error, params string[] extra)
    {
        TraceOptions.Parse(["--case", "needle-t3", .. extra], out var errors);

        Assert.Contains(errors, e => e.StartsWith(error, StringComparison.Ordinal));
    }

    /// <summary>Review finding: a --compare file that is no trace summary stops before any game.</summary>
    [Theory]
    [InlineData("""{"label":"junk","classes":{"land_short":5}}""")]
    [InlineData("""{"label":"junk"}""")]
    public async Task A_compare_file_that_is_no_summary_exits_1(string junk)
    {
        var dir = EngineGateParityTests.StaplesGate(runs: 20);
        Directory.CreateDirectory(Path.Combine(dir, "diagnosis"));
        File.WriteAllText(Path.Combine(dir, "diagnosis", "junk.json"), junk);
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["trace", "--cases", Path.Combine(dir, "cases.json"), "--case", "needle-t3", "--runs", "20", "--label", "o1", "--compare", "junk"], new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("is not a trace summary", stderr.ToString());
        Assert.False(File.Exists(Path.Combine(dir, "diagnosis", "o1.json")));
    }

    /// <summary>Review finding: a rock with one mana ability per color makes each of them.</summary>
    [Fact]
    public void Sources_join_the_colors_of_every_tap_mana_ability()
    {
        var rock = TestDeckCards.Spell("Two Rock", "{2}", "Artifact");
        var swamp = TestDeckCards.Land("Swamp", "B");
        var profiles = new Dictionary<string, CardFeatureProfile>
        {
            ["Two Rock"] = CardFeatureProfile.None with { Develops = true, TapMana = ["{B}", "{U}", "{C}{C}"], HasActivatedAbility = true },
        };

        var sources = TraceAnalysis.Sources([rock, swamp, TestDeckCards.Spell("Bear", "{1}{G}", "Creature")], new DeckFeatureProfiles(profiles, []));

        Assert.Equal(["B", "U", "C"], sources["Two Rock"].Colors);
        Assert.Equal(2, sources["Two Rock"].Amount);
        Assert.Equal(["B"], sources["Swamp"].Colors);
        Assert.Equal(1, sources["Swamp"].Amount);
        Assert.False(sources.ContainsKey("Bear"));
    }

    /// <summary>Review finding: trace plays no disruption, so a disrupted case stops before any game.</summary>
    [Fact]
    public async Task A_disrupted_case_exits_1()
    {
        var dir = EngineGateParityTests.StaplesGate(runs: 20);
        var cases = Path.Combine(dir, "cases.json");
        var loaded = ParityCases.Load(cases);
        ParityJson.Write(cases, loaded with { Cases = [loaded.Cases[0] with { Disruption = JsonNode.Parse("""[{"kind":"counterspell","turn":3}]""")!.AsArray() }] });
        var stderr = new StringWriter();

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(["trace", "--cases", cases, "--case", "needle-t3", "--runs", "20", "--label", "o0"], new StringWriter(), stderr);

        Assert.Equal(1, exit);
        Assert.Contains("disruption", stderr.ToString());
        Assert.False(File.Exists(Path.Combine(dir, "diagnosis", "o0.json")));
    }

    [Fact]
    public void Trace_options_accept_switches_on_a_new_container()
    {
        TraceOptions.Parse(["--case", "needle-t3", "--switches", "-Dmtgarchive.diag.etb=true"], out var errors);

        Assert.Empty(errors);
    }

    /// <summary>Phase 3E: one path per run, lands unnamed.</summary>
    [Fact]
    public void A_path_lists_the_actions_by_the_goal_turn()
    {
        GameLogEntry[] xmage =
        [
            Line(1, "PILOT land Island"), Line(1, "PILOT cast Sol Ring"), Line(1, "PILOT cannot cast Opt"),
            Line(2, "PILOT activate Polluted Delta"), Line(2, "PILOT cast_commander Obeka, Brute Chronologist"), Line(3, "PILOT cast Opt"),
        ];
        const string expected = "T1 land > T1 cast Sol Ring > T2 activate Polluted Delta > T2 cast Obeka, Brute Chronologist";

        Assert.Equal(expected, TraceAnalysis.Path(xmage, 2));
        Assert.Equal(string.Empty, TraceAnalysis.Path([], 2));
    }

    [Fact]
    public void Paths_keep_the_most_common_first_then_by_text()
    {
        var paths = TraceAnalysis.Paths(["b", "a", "b", "c", "a", "b", "d"], top: 3);

        Assert.Equal([new PathCount("b", 3), new PathCount("a", 2), new PathCount("c", 1)], paths);
    }

    [Fact]
    public async Task A_trace_writes_its_success_paths_and_counts_failed_casts()
    {
        var dir = EngineGateParityTests.StaplesGate(runs: 20);
        static JsonArray Log() => new(
            FakeViews.Log(1, "main", "PILOT land Island"),
            FakeViews.Log(1, "main", "PILOT cannot cast Pithing Needle"),
            FakeViews.Log(3, "end", "END life=40 lib=90 hand=[] bf=[] gy=[]"));
        await using var fake = new FakeSidecar(game => game.OverAsync("horizon", 3, log: Log()));

        var exit = await MtgArchive.Tools.EngineGate.Cli.RunAsync(
            ["trace", "--cases", Path.Combine(dir, "cases.json"), "--case", "needle-t3", "--runs", "20", "--label", "x0", "--endpoint", $"{fake.Options().Host}:{fake.Options().Port}"],
            new StringWriter(), new StringWriter());

        Assert.Equal(0, exit);
        var summary = JsonSerializer.Deserialize<TraceSummary>(File.ReadAllText(Path.Combine(dir, "diagnosis", "x0.json")), ParityJson.Options)!;
        Assert.Equal(20, summary.CannotCast);
        Assert.NotNull(summary.Paths);
        Assert.Empty(summary.Paths!); // no run met the goal
    }
}
