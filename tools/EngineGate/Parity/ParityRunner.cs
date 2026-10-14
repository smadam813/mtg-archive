using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tools.EngineGate.Parity;

/// <summary>One arm of one comparison on one engine: null for a tier-2
/// case, "baseline" or "disrupted" for a paired one.</summary>
internal sealed record ArmResult(string? Arm, int Runs, int Successes, IReadOnlyList<double> Curve, int EngineErrors);

/// <summary>Plays the parity cases through the batch runner that /simulate uses.</summary>
internal static class ParityRunner
{
    /// <summary>Each case under each mulligan policy, named case/policy, in file order. <paramref name="only"/> keeps one case.</summary>
    public static IEnumerable<(string Name, ParityCase Case, string Mulligan)> Comparisons(ParityCases cases, string? only)
        => from c in cases.Cases
           where only is null || c.Name == only
           from m in cases.Mulligans
           select ($"{c.Name}/{m}", c, m);

    /// <summary>The arms of a case: none for a plain case, "baseline" and "disrupted" for a paired one.</summary>
    public static string?[] ArmsOf(ParityCase @case) => @case.Disruption is null ? [null] : ["baseline", "disrupted"];

    public static string NameOf(string comparison, string? arm) => arm is null ? comparison : $"{comparison}/{arm}";

    public static IMulliganPolicy Mulligan(string name, GoalPredicate goal) => name switch
    {
        GoalAwareMulligan.PolicyName => new GoalAwareMulligan(goal),
        "land-window" => new LandWindowMulligan(),
        _ => throw new InvalidDataException($"Unknown mulligan policy '{name}'; the policies are goal-aware and land-window."),
    };

    /// <summary>
    /// The disruption profile a case plays in its own mode; null for a tier-2
    /// case. An interactive case plays every event kind: the scripted
    /// opponent casts a disruption card for each (ADR 0013).
    /// </summary>
    public static DisruptionProfile? DisruptionOf(ParityCase @case)
    {
        var mode = @case.Mode();
        return @case.ParseDisruption() is { } profile ? profile with { Mode = mode } : null;
    }

    /// <summary>Stops the gate before any game when a case cannot play.</summary>
    public static void RequirePlayable(IEnumerable<ParityCase> cases)
    {
        foreach (var @case in cases)
        {
            DisruptionOf(@case);
            @case.PlayPolicyName();
        }
    }

    /// <summary>One case on one engine at one base seed: one arm, or both arms of a paired case.</summary>
    public static async Task<IReadOnlyList<ArmResult>> PlayAsync(
        IGameEngine engine, IPreparedDeck deck, ParityCase @case, string mulligan, int runs, long seed, int horizon, CancellationToken cancellationToken)
    {
        var goal = @case.ParseGoal();
        var options = new MonteCarloOptions(runs, seed, horizon, goal, DisruptionOf(@case));
        var pilot = PlayPolicies.Create(@case.PlayPolicyName(), goal);
        var policy = Mulligan(mulligan, goal);
        if (!options.Paired)
        {
            return [Arm(null, await MonteCarlo.RunAsync(engine, deck, pilot, policy, options, cancellationToken))];
        }

        var paired = await MonteCarlo.RunPairedAsync(engine, deck, pilot, policy, options, cancellationToken);
        return [Arm("baseline", paired.Baseline), Arm("disrupted", paired.Disrupted)];
    }

    private static ArmResult Arm(string? arm, MonteCarloResult result) => new(arm, result.Runs, result.Successes, result.Curve, result.EngineErrors);
}
