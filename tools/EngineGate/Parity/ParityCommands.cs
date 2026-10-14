using System.Globalization;
using MtgArchive.Simulation.Engine;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tools.EngineGate.Parity;

/// <summary>The engine regression gate's commands (spec §6.1).</summary>
internal static class ParityCommands
{
    /// <summary>
    /// Runs the serving XMage on every comparison at the base seed and at
    /// the fresh seed, and writes baseline.json. Run it after cutover, after
    /// each pilot change, and after each accepted tag move, so
    /// <c>parity</c> compares a candidate against the engine that serves
    /// floors. With --only the new entries replace their names in the
    /// existing baseline, which must name XMage.
    /// </summary>
    public static async Task<int> BaselineAsync(ParityOptions options, TextWriter stdout, TextWriter stderr)
    {
        var cases = ParityCases.Load(options.Cases);
        if (NoSuchCase(options, cases, stderr))
        {
            return 1;
        }

        // An --only merge keeps the other entries, so they must come from the serving engine too.
        var old = options.Only is not null && File.Exists(options.BaselinePath) ? ParityBaseline.Load(options.BaselinePath) : null;
        if (old is not null && OtherEngine(old, options, stderr, "parity-baseline --only cannot merge into it; run parity-baseline without --only"))
        {
            return 1;
        }

        ParityRunner.RequirePlayable(ParityRunner.Comparisons(cases, options.Only).Select(c => c.Case));
        var runs = options.Runs ?? cases.Runs;
        var entries = new List<BaselineEntry>();
        var errors = new List<string>();
        await using var container = await SidecarContainer.OpenAsync(options, 0, null, stdout);
        await using var sidecar = new XmageSidecar(container.Options);
        foreach (var group in ParityRunner.Comparisons(cases, options.Only).GroupBy(c => (c.Case.Deck, Rules: c.Case.Rules())))
        {
            var path = options.DeckPath(group.Key.Deck);
            var deck = ParityDeck.Load(path);
            var sha = ParityDeck.Sha(path);
            var engine = await XmageEngine.ConnectAsync(sidecar, (_, _) => Task.FromResult(deck.FeatureProfiles), null, CancellationToken.None, group.Key.Rules);
            var prepared = await engine.PrepareAsync(deck.Deck, CancellationToken.None);
            foreach (var (name, @case, mulligan) in group)
            {
                var first = await ParityRunner.PlayAsync(engine, prepared, @case, mulligan, runs, cases.BaseSeed, cases.TurnHorizon, CancellationToken.None);
                var fresh = await ParityRunner.PlayAsync(engine, prepared, @case, mulligan, runs, cases.FreshSeed, cases.TurnHorizon, CancellationToken.None);
                foreach (var (arm, again) in first.Zip(fresh))
                {
                    var armName = ParityRunner.NameOf(name, arm.Arm);
                    if (arm.EngineErrors * 100 > runs || again.EngineErrors * 100 > runs)
                    {
                        errors.Add($"{armName}: {arm.EngineErrors} engine errors at seed {cases.BaseSeed}, {again.EngineErrors} at seed {cases.FreshSeed}, of {runs} runs each");
                    }

                    entries.Add(new BaselineEntry(armName, @case.Name, @case.Deck, sha, @case.Goal, mulligan, @case.Disruption, arm.Arm, runs,
                        cases.BaseSeed, arm.Successes, arm.Curve, cases.FreshSeed, again.Successes, again.Curve, cases.TurnHorizon, engine.Version, @case.Format, @case.DisruptionMode, @case.PlayPolicy));
                    stdout.WriteLine($"{armName}: {arm.Successes}/{runs} at seed {cases.BaseSeed}, {again.Successes}/{runs} at seed {cases.FreshSeed}");
                }
            }
        }

        // An engine error counts as a failure, so a baseline with more than
        // 1% of them would understate the floor every candidate meets.
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                stderr.WriteLine(error);
            }

            stderr.WriteLine("More than 1% of a comparison's games ended in an engine error; baseline.json is unchanged.");
            return 8;
        }

        // A kept entry keeps the build that played it: an entry from before
        // per-entry versions takes its file's header version.
        IEnumerable<BaselineEntry> kept = [];
        if (old is not null)
        {
            kept = old.Comparisons.Where(e => entries.All(n => n.Name != e.Name)).Select(e => e with { EngineVersion = e.EngineVersion ?? old.Version });
        }

        var order = ParityRunner.Comparisons(cases, null).Select(c => c.Name).ToList();
        List<BaselineEntry> all = [.. kept.Concat(entries).OrderBy(e => order.IndexOf(ComparisonOf(e.Name))).ThenBy(e => e.Name, StringComparer.Ordinal)];
        new ParityBaseline(
                XmageEngine.EngineName,
                ParityBaseline.VersionOf(all),
                DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                all)
            .Save(options.BaselinePath);
        return 0;
    }

    /// <summary>A baseline that an engine other than XMage recorded, such as
    /// our retired engine before cutover. The regression gate compares XMage
    /// against XMage only.</summary>
    private static bool OtherEngine(ParityBaseline baseline, ParityOptions options, TextWriter stderr, string remedy)
    {
        if (baseline.Engine == XmageEngine.EngineName)
        {
            return false;
        }

        stderr.WriteLine($"{options.BaselinePath} records engine '{baseline.Engine}', not '{XmageEngine.EngineName}': {remedy}.");
        return true;
    }

    /// <summary>An --only that names no case: the gate would pass, or write a baseline, on no evidence.</summary>
    private static bool NoSuchCase(ParityOptions options, ParityCases cases, TextWriter stderr)
    {
        if (options.Only is null || cases.Cases.Any(c => c.Name == options.Only))
        {
            return false;
        }

        stderr.WriteLine($"--only: no case named '{options.Only}' in {options.Cases}.");
        return true;
    }

    /// <summary>The comparison an entry belongs to: its name up to the arm ("case/policy").</summary>
    private static string ComparisonOf(string name)
    {
        var second = name.IndexOf('/', name.IndexOf('/') + 1);
        return second < 0 ? name : name[..second];
    }

    /// <summary>The log line the shim writes when the activation cap hides an ability.</summary>
    public const string CapPrefix = "CAP ";

    /// <summary>
    /// Runs the candidate sidecar on every comparison and writes report.md (spec §6.1);
    /// with --only it prints the rows and leaves report.md as it is.
    /// Checks the baseline first: a baseline of another engine, or a missing
    /// or stale entry, stops the gate before any game. Exit 0 when every comparison passes, 6 otherwise.
    /// </summary>
    public static async Task<int> ParityAsync(ParityOptions options, TextWriter stdout, TextWriter stderr)
    {
        var cases = ParityCases.Load(options.Cases);
        if (NoSuchCase(options, cases, stderr))
        {
            return 1;
        }

        var runs = options.Runs ?? cases.Runs;
        if (!File.Exists(options.BaselinePath))
        {
            stderr.WriteLine($"No baseline at {options.BaselinePath}: run parity-baseline first.");
            return 1;
        }

        var baseline = ParityBaseline.Load(options.BaselinePath);
        if (OtherEngine(baseline, options, stderr, "run parity-baseline to record the serving XMage first"))
        {
            return 1;
        }

        var comparisons = ParityRunner.Comparisons(cases, options.Only).ToList();
        var problems = new List<string>();
        var recorded = new Dictionary<string, BaselineEntry>(StringComparer.Ordinal);
        foreach (var (name, @case, mulligan) in comparisons)
        {
            var sha = ParityDeck.Sha(options.DeckPath(@case.Deck));
            string?[] arms = ParityRunner.ArmsOf(@case);
            foreach (var arm in arms)
            {
                var armName = ParityRunner.NameOf(name, arm);
                switch (baseline.Find(armName, sha, runs, cases, @case, mulligan))
                {
                    case (_, { } problem):
                        problems.Add(problem);
                        break;
                    case ({ } entry, _):
                        recorded[armName] = entry;
                        break;
                }
            }
        }

        // With --only, check the entries against every comparison, not only the named case.
        var allNames = ParityRunner.Comparisons(cases, null)
            .SelectMany(c => ParityRunner.ArmsOf(c.Case).Select(arm => ParityRunner.NameOf(c.Name, arm)))
            .ToList();
        var accepted = AcceptedDifferences.Load(options.AcceptedPath);
        problems.AddRange(accepted.Problems(allNames, options.Root));

        if (problems.Count > 0)
        {
            problems.ForEach(stderr.WriteLine);
            return 1;
        }

        ParityRunner.RequirePlayable(comparisons.Select(c => c.Case));
        await using var container = await SidecarContainer.OpenAsync(options, 0, null, stdout);
        await using var sidecar = new XmageSidecar(container.Options);
        var rows = new List<ComparisonRow>();
        var decks = new List<DeckSection>();
        var version = string.Empty;
        foreach (var group in comparisons.GroupBy(c => (c.Case.Deck, Rules: c.Case.Rules())))
        {
            var path = options.DeckPath(group.Key.Deck);
            var deck = ParityDeck.Load(path);
            var engine = await XmageEngine.ConnectAsync(sidecar, (_, _) => Task.FromResult(deck.FeatureProfiles), null, CancellationToken.None, group.Key.Rules);
            version = engine.Version;
            var prepared = await engine.PrepareAsync(deck.Deck, CancellationToken.None);
            foreach (var (name, @case, mulligan) in group)
            {
                var first = await ParityRunner.PlayAsync(engine, prepared, @case, mulligan, runs, cases.BaseSeed, cases.TurnHorizon, CancellationToken.None);
                IReadOnlyList<ArmResult>? fresh = null;
                for (var i = 0; i < first.Count; i++)
                {
                    var arm = first[i];
                    var armName = ParityRunner.NameOf(name, arm.Arm);
                    var entry = recorded[armName];
                    var difference = Difference.Newcombe(arm.Successes, arm.Runs, entry.Successes, entry.Runs);
                    Difference? again = null;
                    if (!difference.ContainsZero && arm.EngineErrors * 100 <= arm.Runs)
                    {
                        fresh ??= await ParityRunner.PlayAsync(engine, prepared, @case, mulligan, runs, cases.FreshSeed, cases.TurnHorizon, CancellationToken.None);
                        again = Difference.Newcombe(fresh[i].Successes, fresh[i].Runs, entry.FreshSuccesses, entry.Runs);
                    }

                    int? freshErrors = again is null ? null : fresh![i].EngineErrors;
                    var row = new ComparisonRow(armName, @case.Exercises, runs, entry.Successes, arm.Successes, arm.EngineErrors, freshErrors, difference, again,
                        Verdicts.Of(difference, again, arm.EngineErrors, arm.Runs, freshErrors ?? 0, accepted.For(armName)));
                    rows.Add(row);
                    stdout.WriteLine(row.Summary());
                }
            }

            // The cap count goes only into report.md, which --only does not write.
            // A deck under two formats gets one section, from its first format.
            if (options.Only is null && decks.All(d => d.Name != group.Key.Deck))
            {
                var capGoal = group.First().Case.ParseGoal();
                decks.Add(new DeckSection(group.Key.Deck, deck, prepared.Coverage, await CapHitsAsync(engine, prepared, capGoal, options.CapSeeds, cases.BaseSeed, cases.TurnHorizon), options.CapSeeds));
            }
        }

        // report.md is the whole gate's record: a one-case run must not replace it.
        if (options.Only is null)
        {
            File.WriteAllText(options.ReportPath, ParityReport.Render(DateTimeOffset.UtcNow, baseline, version, cases, runs, rows, decks, accepted));
            stdout.WriteLine($"report: {options.ReportPath}");
        }
        else
        {
            stdout.WriteLine($"--only {options.Only}: {options.ReportPath} not written; the rows above are this run's result.");
        }

        return rows.All(r => r.Passes) ? 0 : 6;
    }

    /// <summary>The runs, of <paramref name="seeds"/> logged full-horizon runs, that reach the activation cap.</summary>
    private static async Task<int> CapHitsAsync(IGameEngine engine, IPreparedDeck deck, GoalPredicate goal, int seeds, long baseSeed, int horizon)
    {
        var hits = 0;
        await Parallel.ForEachAsync(
            Enumerable.Range(0, seeds),
            new ParallelOptions { MaxDegreeOfParallelism = engine.Parallelism },
            async (index, token) =>
            {
                var record = await deck.PlayAsync(
                    new RunSpec(index, RunSeeds.Derive(baseSeed, index), horizon, goal, null, EarlyStop: false, CaptureLog: true),
                    new GoalAwarePilot(goal),
                    new GoalAwareMulligan(goal),
                    token);
                if (record.Log?.Any(l => l.Message.StartsWith(CapPrefix, StringComparison.Ordinal)) == true)
                {
                    Interlocked.Increment(ref hits);
                }
            });
        return hits;
    }
}
