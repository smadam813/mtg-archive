using System.Collections.Concurrent;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>Records every view the goal-aware pilot sees, then lets it answer.</summary>
internal sealed class XmageViewRecorder(GoalAwarePilot inner) : IPilotPolicy, IGoalDirected
{
    public ConcurrentQueue<PilotView> Views { get; } = new();

    /// <summary>Every pick the pilot answered, in order.</summary>
    public ConcurrentQueue<PickChoice> Picks { get; } = new();

    public GoalPredicate Goal => inner.Goal;

    public string Name => inner.Name;

    public string? ChooseAction(PilotView view)
    {
        Views.Enqueue(view);
        return inner.ChooseAction(view);
    }

    public string Pick(PilotView view, PickChoice choice)
    {
        Picks.Enqueue(choice);
        Views.Enqueue(view);
        return inner.Pick(view, choice);
    }

    public IReadOnlyList<DeckCard> ChooseDiscards(PilotView view, int count)
    {
        Views.Enqueue(view);
        return inner.ChooseDiscards(view, count);
    }

    public string? Respond(PilotView view, ResponseChoice choice)
    {
        Views.Enqueue(view);
        return inner.Respond(view, choice);
    }
}
