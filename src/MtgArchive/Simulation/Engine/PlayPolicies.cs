namespace MtgArchive.Simulation.Engine;

/// <summary>The play policies by wire name (ADR 0003, ADR 0014). A request
/// without a choice plays <see cref="GoalAwarePilot.PolicyName"/>.</summary>
public static class PlayPolicies
{
    /// <summary>The policies, closed at two names. The first is the default.</summary>
    public static readonly IReadOnlyList<string> Names = [GoalAwarePilot.PolicyName, GoalAwareHoldPilot.PolicyName];

    /// <summary>The policy holds mana for interactive disruption, so it plays
    /// only in that mode.</summary>
    public static bool NeedsInteractive(string name) => name == GoalAwareHoldPilot.PolicyName;

    public static IPilotPolicy Create(string name, GoalPredicate goal) => name switch
    {
        GoalAwarePilot.PolicyName => new GoalAwarePilot(goal),
        GoalAwareHoldPilot.PolicyName => new GoalAwareHoldPilot(goal),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, $"Unknown play policy; the policies are {string.Join(", ", Names)}."),
    };
}
