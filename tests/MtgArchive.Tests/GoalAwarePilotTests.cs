using System.Reflection;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// The goal-aware greedy pilot: the floor label, and that the
/// pilot never sees a disruption profile. The priority order on a pilot
/// view is in <see cref="PilotViewPolicyTests"/>.
/// </summary>
public sealed class GoalAwarePilotTests
{
    [Fact]
    public void Results_are_floors_labeled_with_the_greedy_policy()
    {
        Assert.Equal(">= 42.3%, greedy policy", GoalAwarePilot.FloorLabel(0.4239));
        Assert.Equal(">= 100%, greedy policy", GoalAwarePilot.FloorLabel(1));
        Assert.Equal(">= 0%, greedy policy", GoalAwarePilot.FloorLabel(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoalAwarePilot.FloorLabel(1.5));
    }

    [Fact]
    public void The_pilot_is_blind_to_any_disruption_profile()
    {
        // The policies take the goal and nothing else, and no member of theirs
        // names a disruption: a disruption-aware pilot is a future policy
        // through the same seam, never a mode of this one.
        Assert.DoesNotContain(typeof(PilotView).GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static),
            m => m.Name.Contains("Disrupt", StringComparison.OrdinalIgnoreCase));
        foreach (var type in new[] { typeof(GoalAwarePilot), typeof(GoalAwareMulligan), typeof(GoalHeuristics) })
        {
            Assert.DoesNotContain(type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static),
                m => m.Name.Contains("Disrupt", StringComparison.OrdinalIgnoreCase));
            foreach (var constructor in type.GetConstructors())
            {
                Assert.Equal([typeof(GoalPredicate)], constructor.GetParameters().Select(p => p.ParameterType));
            }
        }
    }
}
