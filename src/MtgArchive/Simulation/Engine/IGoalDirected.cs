namespace MtgArchive.Simulation.Engine;

/// <summary>A policy that plays toward a goal. The run reads the goal
/// here, so the policy and the goal evaluation never disagree.</summary>
public interface IGoalDirected
{
    GoalPredicate Goal { get; }
}
