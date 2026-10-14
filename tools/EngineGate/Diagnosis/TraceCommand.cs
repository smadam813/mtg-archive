using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MtgArchive.Simulation.Engine;
using MtgArchive.Simulation.Xmage;
using MtgArchive.Tools.EngineGate.Parity;

namespace MtgArchive.Tools.EngineGate.Diagnosis;

/// <summary>
/// <c>EngineGate trace</c>: one comparison on XMage, with every log kept,
/// and the analysis. First made for the Obeka turn-4 diagnosis, now for any
/// case. The failure classes fit a cast goal with colored pips, not an
/// <c>on_battlefield</c> goal of mana value 1. --switches sets
/// XMAGE_SWITCHES on a new sidecar.
/// </summary>
internal static class TraceCommand
{
    /// <summary>The failing runs kept in the failures file, per failure class.</summary>
    private const int FailuresKept = 20;

    /// <summary>The most common success paths kept in the summary.</summary>
    private const int PathsKept = 20;

    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        var o = TraceOptions.Parse(args, out var errors);
        if (errors.Count > 0)
        {
            errors.ForEach(stderr.WriteLine);
            return 1;
        }

        var cases = ParityCases.Load(o.Cases);
        var @case = cases.Cases.FirstOrDefault(c => c.Name == o.Case);
        if (@case is null)
        {
            stderr.WriteLine($"no case named '{o.Case}' in {o.Cases}");
            return 1;
        }

        if (@case.Disruption is not null)
        {
            stderr.WriteLine($"case '{o.Case}' has a disruption; trace plays only cases with none.");
            return 1;
        }

        var parity = new ParityOptions(o.Cases, o.Image, o.Endpoint is null ? [] : [o.Endpoint], o.Threads, 1, 0, o.Runs, null);
        var deckPath = parity.DeckPath(@case.Deck);
        var deck = ParityDeck.Load(deckPath);
        var goal = @case.ParseGoal();
        var goalCard = deck.Deck.First(c => c.Answers(goal.CardNames[0]));
        var pips = TraceAnalysis.Pips(goalCard.ManaCost);
        var landNames = TraceAnalysis.LandNames(deck.Deck);
        var sources = TraceAnalysis.Sources(deck.Deck, deck.FeatureProfiles);
        var sourceNames = sources.Keys.ToList();
        var comparePath = o.Compare is null ? null : Path.Combine(parity.Root, "diagnosis", o.Compare + ".json");
        if (comparePath is not null && !File.Exists(comparePath))
        {
            stderr.WriteLine($"--compare: no trace at {comparePath}; run the trace with --label {o.Compare} first.");
            return 1;
        }

        TraceSummary? other = null;
        if (comparePath is not null)
        {
            try
            {
                other = ParityJson.Read<TraceSummary>(comparePath);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
            {
                stderr.WriteLine($"--compare: {comparePath} is not a trace summary: {ex.Message}");
                return 1;
            }
        }

        var ablations = new List<string>();
        await using var container = await SidecarContainer.OpenAsync(parity, 0, o.Switches, stdout);
        await using var sidecar = new XmageSidecar(container.Options);
        var engine = await XmageEngine.ConnectAsync(sidecar, (_, _) => Task.FromResult(deck.FeatureProfiles), null, CancellationToken.None, @case.Rules());
        var prepared = await engine.PrepareAsync(deck.Deck, CancellationToken.None);
        var version = engine.Version;
        if (o.Switches is not null)
        {
            ablations.Add("switches " + o.Switches);
        }

        var records = await PlayAsync(prepared, goal, o, cases, engine.Parallelism);

        var etb = TraceAnalysis.Etb(records.SelectMany(r => r.Log ?? []));
        var payLife = TraceAnalysis.PayLife(records.SelectMany(r => r.Log ?? []));
        if (TraceAnalysis.SwitchesMissing(o.Switches, etb, payLife) is { } missing)
        {
            stderr.WriteLine(missing);
            return 1;
        }

        var goalTurn = goal.LastTurn;
        var successes = records.Count(r => r.Success);
        var engineErrors = records.Count(r => r.EngineError);
        var wilson = ConfidenceInterval.Wilson(successes, records.Count);
        var classes = Enum.GetValues<FailureClass>().ToDictionary(c => c, _ => 0);
        var kept = Enum.GetValues<FailureClass>().ToDictionary(c => c, _ => 0);
        var failures = new List<string>();
        for (var i = 0; i < records.Count; i++)
        {
            var r = records[i];
            if (r.Success || r.EngineError)
            {
                continue;
            }

            var end = r.Log?.LastOrDefault(l => l.Turn == goalTurn && l.Message.StartsWith(TraceAnalysis.EndPrefix, StringComparison.Ordinal));
            var failure = end is null ? FailureClass.Unknown : TraceAnalysis.Classify(TraceAnalysis.EndLands(end.Message, sourceNames).Battlefield.Select(b => b.Land), sources, pips, (int)goalCard.ManaValue);
            classes[failure]++;
            if (kept[failure] < FailuresKept)
            {
                kept[failure]++;
                failures.Add($"# run {i}, seed {RunSeeds.Derive(cases.BaseSeed, i)}, class {failure}\n{string.Join("\n", (r.Log ?? []).Where(l => l.Turn <= goalTurn).Select(l => $"T{l.Turn} {l.Step}: {l.Message}"))}\n");
            }
        }

        var lift = TraceAnalysis.Lift([.. records.Where(r => !r.EngineError).Select(r => (r.Success, TraceAnalysis.LandsByTurn(r.Log ?? [], goalTurn, landNames)))]);
        var paths = TraceAnalysis.Paths(records.Where(r => r.Success).Select(r => TraceAnalysis.Path(r.Log ?? [], goalTurn)), PathsKept);
        var cannotCast = TraceAnalysis.CannotCast(records.SelectMany(r => r.Log ?? []));
        var against = other is null ? null
            : new TraceComparison(other.Label, other.Successes, other.Runs, Difference.Newcombe(successes, records.Count, other.Successes, other.Runs));

        var summary = new TraceSummary(o.Label, XmageEngine.EngineName, o.Case, o.Mulligan, ParityDeck.Sha(deckPath), version, ablations, records.Count, successes, engineErrors,
            (double)successes / records.Count, wilson.Lower, wilson.Upper,
            etb, payLife, classes, lift, against, paths, cannotCast);
        var dir = Path.Combine(parity.Root, "diagnosis");
        ParityJson.Write(Path.Combine(dir, o.Label + ".json"), summary);
        File.WriteAllText(Path.Combine(dir, o.Label + ".failures.txt"), string.Join("\n", failures).ReplaceLineEndings("\n"));
        stdout.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{o.Label}: {XmageEngine.EngineName} {successes}/{records.Count} ({summary.Floor * 100:0.0}%) [{summary.Lower * 100:0.0}, {summary.Upper * 100:0.0}]{(against is null ? string.Empty : $", against {against.Label} {against.Difference.Estimate * 100:+0.0;-0.0} pp [{against.Difference.Lower * 100:0.0}, {against.Difference.Upper * 100:0.0}]")}"));
        return 0;
    }

    /// <summary>Every run at the full horizon, with its log, as the determinism gate plays it.</summary>
    private static async Task<IReadOnlyList<RunRecord>> PlayAsync(IPreparedDeck deck, GoalPredicate goal, TraceOptions o, ParityCases cases, int parallelism)
    {
        var records = new RunRecord[o.Runs];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, o.Runs),
            new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            async (index, token) =>
            {
                records[index] = await deck.PlayAsync(
                    new RunSpec(index, RunSeeds.Derive(cases.BaseSeed, index), cases.TurnHorizon, goal, null, EarlyStop: false, CaptureLog: true),
                    new GoalAwarePilot(goal),
                    ParityRunner.Mulligan(o.Mulligan, goal),
                    token);
            });
        return records;
    }
}

/// <summary>The command line of <c>EngineGate trace</c>.</summary>
internal sealed partial record TraceOptions(
    string Cases, string Case, string Mulligan, int Runs, string Label, string? Compare,
    string Image, string? Endpoint, string? Switches, int Threads)
{
    public static TraceOptions Parse(IReadOnlyList<string> args, out List<string> errors)
    {
        errors = [];
        string cases = ParityOptions.DefaultCases, image = ParityOptions.DefaultImage, mulligan = GoalAwareMulligan.PolicyName;
        string? @case = null, label = null, compare = null, endpoint = null, switches = null;
        int runs = 10_000, threads = 16;
        for (var i = 0; i < args.Count; i++)
        {
            var option = args[i];
            switch (option)
            {
                case "--cases": cases = Value(args, ref i, errors); break;
                case "--case": @case = Value(args, ref i, errors); break;
                case "--mulligan": mulligan = Value(args, ref i, errors); break;
                case "--runs": runs = Count(option, Value(args, ref i, errors), errors); break;
                case "--threads": threads = Count(option, Value(args, ref i, errors), errors); break;
                case "--label": label = Value(args, ref i, errors); break;
                case "--compare": compare = Value(args, ref i, errors); break;
                case "--image": image = Value(args, ref i, errors); break;
                case "--endpoint": endpoint = Value(args, ref i, errors); break;
                case "--switches": switches = Value(args, ref i, errors); break;
                default: errors.Add($"unknown option '{option}'"); break;
            }
        }

        if (@case is null)
        {
            errors.Add("--case is required");
        }

        if (mulligan is not (GoalAwareMulligan.PolicyName or "land-window"))
        {
            errors.Add("--mulligan is goal-aware or land-window");
        }

        if (switches is not null && string.IsNullOrWhiteSpace(switches))
        {
            errors.Add("--switches needs a value");
        }

        if (switches is not null && endpoint is not null)
        {
            errors.Add("--switches needs a new container; a named --endpoint runs with its own switches");
        }

        if (endpoint is { Length: > 0 })
        {
            try
            {
                ParityOptions.EndpointOptions(endpoint);
            }
            catch (ArgumentException ex)
            {
                errors.Add($"--endpoint: {ex.Message}");
            }
        }

        label ??= $"{XmageEngine.EngineName}-{@case}";
        if (!LabelPattern().IsMatch(label))
        {
            errors.Add("--label is letters, digits, '.', '_' and '-', and starts with a letter or digit");
        }

        if (compare == label)
        {
            errors.Add("--compare names another trace, not this one's --label");
        }

        if (compare is not null && !LabelPattern().IsMatch(compare))
        {
            errors.Add("--compare is a trace label: letters, digits, '.', '_' and '-', starting with a letter or digit");
        }

        return new TraceOptions(cases, @case ?? string.Empty, mulligan, runs, label, compare, image, endpoint, switches, threads);
    }

    /// <summary>A label names a file in the diagnosis directory: no path separators, no leading dot.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex LabelPattern();

    private static string Value(IReadOnlyList<string> args, ref int i, List<string> errors)
    {
        if (i + 1 < args.Count)
        {
            return args[++i];
        }

        errors.Add($"'{args[i]}' needs a value");
        return string.Empty;
    }

    private static int Count(string option, string text, List<string> errors)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 1)
        {
            return value;
        }

        errors.Add($"{option} is a whole number of at least 1");
        return 1;
    }
}
