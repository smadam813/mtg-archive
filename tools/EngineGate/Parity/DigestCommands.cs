using System.Globalization;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tools.EngineGate.Parity;

internal enum DigestCheck
{
    /// <summary>The same seeds in two separate sidecar processes (spec §6.3).</summary>
    Determinism,

    /// <summary>The same seeds with the game-copies changes on, then off, in one image (spec §6.4).</summary>
    Leak,
}

/// <summary>One deck's row: how many of its seeds gave another digest in the second process.</summary>
internal sealed record DigestRow(DateTimeOffset At, string Engine, string Deck, int Seeds, int Different, int EngineErrors, int? FirstDifferent)
{
    public string Verdict => Different == 0 && EngineErrors == 0 ? "pass" : "fail";

    public static DigestRow Of(DateTimeOffset at, string engine, string deck, IReadOnlyList<RunRecord> first, IReadOnlyList<RunRecord> second)
    {
        var different = Enumerable.Range(0, first.Count).Where(i => first[i].Digest != second[i].Digest).ToList();
        var errors = Enumerable.Range(0, first.Count).Count(i => first[i].EngineError || second[i].EngineError);
        return new DigestRow(at, engine, deck, first.Count, different.Count, errors, different.Count == 0 ? null : different[0]);
    }

    public static string Header(DigestCheck check) => check == DigestCheck.Determinism
        ? """
          # Determinism

          `EngineGate determinism` appends one row per parity deck (spec §6.3; see `README.md`). Every seed plays in two separate sidecar processes; each must give the same game digest. A game that ends in an engine error proves nothing about play and fails the row. First different is the first run index whose digests differ.

          | Date (UTC) | Engine | Deck | Seeds | Different | Engine errors | First different | Verdict |
          |---|---|---|---|---|---|---|---|
          """
        : """
          # Leak check

          `EngineGate leak` appends one row per parity deck (spec §6.4; see `README.md`). Every seed plays with the game-copies changes of patch 0002 on, then off, in the same image; each must give the same game digest, or a shared library card leaked a playable calculation into the game. A game that ends in an engine error fails the row.

          | Date (UTC) | Engine | Deck | Seeds | Different | Engine errors | First different | Verdict |
          |---|---|---|---|---|---|---|---|
          """;

    public string Markdown() => string.Create(CultureInfo.InvariantCulture,
        $"| {At:yyyy-MM-dd HH:mm} | {Engine} | {Deck} | {Seeds} | {Different} | {EngineErrors} | {(FirstDifferent is { } i ? i.ToString(CultureInfo.InvariantCulture) : "–")} | {Verdict} |");
}

/// <summary>The determinism and leak gates: one deck at a time, the same seeds on two sidecar processes.</summary>
internal static class DigestCommands
{
    public static async Task<int> RunAsync(DigestCheck check, ParityOptions options, TextWriter stdout, TextWriter stderr)
    {
        var cases = ParityCases.Load(options.Cases);
        var caseless = cases.Decks.Where(d => !d.DeterminismOnly && cases.Cases.All(c => c.Deck != d.Name)).Select(d => d.Name).ToList();
        if (caseless.Count > 0)
        {
            stderr.WriteLine($"{string.Join(", ", caseless)}: no case in {options.Cases} names the deck, so the gate has no goal to play it under.");
            return 1;
        }

        if (check == DigestCheck.Leak && options.Endpoints.Count > 0)
        {
            stderr.WriteLine(options.Endpoints.Count > 1
                ? $"warning: the first --endpoint must run with the game-copies changes on, the second with XMAGE_SWITCHES=\"{SidecarContainer.CopiesOff}\"; the gate cannot check either."
                : "warning: the --endpoint must run with the game-copies changes on (no XMAGE_SWITCHES); the gate cannot check it.");
        }

        // Every play is known, and playable, before any sidecar starts.
        var plays = cases.Decks.Select(source => (Source: source, Plays: Plays(cases, source))).ToList();
        if (options.Only is not null)
        {
            // A probe run: one deck (all its rows) or one row. It leaves the tracked report as it is, as parity --only does.
            plays = [.. plays.Select(p => (p.Source, Plays: SelectPlays(p.Plays, p.Source.Name, options.Only))).Where(p => p.Plays.Count > 0)];
            if (plays.Count == 0)
            {
                stderr.WriteLine($"--only: no deck or row named '{options.Only}' in {options.Cases}.");
                return 1;
            }
        }

        await using var first = await SidecarContainer.OpenAsync(options, 0, null, stdout);
        await using var second = await SidecarContainer.OpenAsync(options, 1, SecondSwitches(check), stdout);
        await using var sidecarA = new XmageSidecar(first.Options);
        await using var sidecarB = new XmageSidecar(second.Options);
        var (helloA, helloB) = (await sidecarA.HelloAsync(CancellationToken.None), await sidecarB.HelloAsync(CancellationToken.None));
        if (helloA.Version != helloB.Version)
        {
            stderr.WriteLine($"The two sidecars run different builds ({helloA.Version}, {helloB.Version}): a digest difference would prove nothing.");
            return 1;
        }

        var output = Path.Combine(options.Root, check == DigestCheck.Determinism ? "determinism.md" : "leak.md");
        var failed = false;
        foreach (var (source, deckPlays) in plays)
        {
            var deck = ParityDeck.Load(options.DeckPath(source.Name));
            foreach (var (label, goal, rules, disruption, playPolicy) in deckPlays)
            {
                var (version, a) = await PlayAsync(sidecarA, deck, goal, rules, disruption, playPolicy, options.Seeds, cases.BaseSeed, cases.TurnHorizon);
                var (_, b) = await PlayAsync(sidecarB, deck, goal, rules, disruption, playPolicy, options.Seeds, cases.BaseSeed, cases.TurnHorizon);
                var row = DigestRow.Of(DateTimeOffset.UtcNow, $"xmage {version}", label, a, b);
                if (options.Only is null)
                {
                    Reports.Append(output, DigestRow.Header(check), row.Markdown());
                }

                stdout.WriteLine(row.Markdown());
                failed |= row.Verdict == "fail";
            }
        }

        return failed ? 7 : 0;
    }

    /// <summary>
    /// What the digest gates play a deck under, one row each. A
    /// determinism-only deck plays under its own goal and format. A parity
    /// deck plays under the first case of each distinct game rules among its
    /// cases; with more than one, each row names the format of
    /// that first case.
    /// </summary>
    /// <remarks>Each disrupted case of the deck then adds a row named for the
    /// case, which plays the disrupted arm: its own goal, format and profile
    /// in its own mode and play policy (ADR 0014). The deck rows play no profile.</remarks>
    internal static IReadOnlyList<DigestPlay> Plays(ParityCases cases, DeckSource source)
    {
        if (source.DeterminismOnly)
        {
            return [new DigestPlay(source.Name, source.ParseGoal(), source.Rules(), null, GoalAwarePilot.PolicyName)];
        }

        var own = cases.Cases.Where(c => c.Deck == source.Name).ToList();
        var groups = own.GroupBy(c => c.Rules()).Select(g => g.First()).ToList();
        return
        [
            .. groups.Select(c => new DigestPlay(
                groups.Count == 1 ? source.Name : $"{source.Name} ({c.Format ?? "commander"})",
                c.ParseGoal(),
                c.Rules(),
                null,
                GoalAwarePilot.PolicyName)),
            .. own.Where(c => c.Disruption is not null).Select(c => new DigestPlay(c.Name, c.ParseGoal(), c.Rules(), ParityRunner.DisruptionOf(c), c.PlayPolicyName())),
        ];
    }

    /// <summary>The rows of one deck that --only names: all of them when it names the deck, else the row with that label.</summary>
    internal static IReadOnlyList<DigestPlay> SelectPlays(IReadOnlyList<DigestPlay> plays, string deck, string only) =>
        only == deck ? plays : [.. plays.Where(p => p.Label == only)];

    /// <summary>The XMAGE_SWITCHES of the second process: the game-copies changes off for the leak check.</summary>
    internal static string? SecondSwitches(DigestCheck check) => check == DigestCheck.Leak ? SidecarContainer.CopiesOff : null;

    /// <summary>Every seed at the full horizon, so early stop cannot hide a difference late in the game.</summary>
    private static async Task<(string Version, IReadOnlyList<RunRecord> Records)> PlayAsync(
        XmageSidecar sidecar, ParityDeck deck, GoalPredicate goal, GameRules rules, DisruptionProfile? disruption, string playPolicy, int seeds, long baseSeed, int horizon)
    {
        var engine = await XmageEngine.ConnectAsync(sidecar, (_, _) => Task.FromResult(deck.FeatureProfiles), null, CancellationToken.None, rules);
        var prepared = await engine.PrepareAsync(deck.Deck, CancellationToken.None);
        var records = new RunRecord[seeds];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, seeds),
            new ParallelOptions { MaxDegreeOfParallelism = engine.Parallelism },
            async (index, token) => records[index] = await prepared.PlayAsync(
                new RunSpec(index, RunSeeds.Derive(baseSeed, index), horizon, goal, disruption, EarlyStop: false, CaptureLog: false),
                PlayPolicies.Create(playPolicy, goal),
                new GoalAwareMulligan(goal),
                token));
        return (engine.Version, records);
    }
}

/// <summary>One row of a digest check: the label, the goal, the rules, the
/// disruption profile (null plays none) and the play policy name.</summary>
internal sealed record DigestPlay(string Label, GoalPredicate Goal, GameRules Rules, DisruptionProfile? Disruption, string PlayPolicy);
