using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>A test pilot policy on the pilot view: answers through supplied
/// functions (default pass, the first option, the front of the hand) and
/// records every view it was asked on.</summary>
internal sealed class ScriptedViewPilot : IPilotPolicy
{
    public string Name => "scripted-view";

    public Func<PilotView, string?>? Action { get; init; }

    public Func<PilotView, PickChoice, string>? PickOption { get; init; }

    public List<PilotView> Views { get; } = [];

    public List<PickChoice> Picks { get; } = [];

    public string? ChooseAction(PilotView view)
    {
        Views.Add(view);
        return Action?.Invoke(view);
    }

    public string Pick(PilotView view, PickChoice choice)
    {
        Views.Add(view);
        Picks.Add(choice);
        return PickOption?.Invoke(view, choice) ?? choice.Options[0].Id;
    }

    public IReadOnlyList<DeckCard> ChooseDiscards(PilotView view, int count) => [.. view.Hand.Take(count)];

    /// <summary>Every response choice it was asked, with its view.</summary>
    public List<(PilotView View, ResponseChoice Choice)> Responses { get; } = [];

    /// <summary>Lets every disruption card resolve, unless the test supplies an answer.</summary>
    public Func<PilotView, ResponseChoice, string?>? Answer { get; init; }

    public string? Respond(PilotView view, ResponseChoice choice)
    {
        Responses.Add((view, choice));
        return Answer?.Invoke(view, choice);
    }
}

/// <summary>A test mulligan policy on the pilot view: keeps through a
/// supplied choice, bottoms from the back, and records what it was asked.</summary>
internal sealed class ScriptedViewMulligan : IMulliganPolicy
{
    public string Name => "scripted-view";

    public required Func<MulliganChoice, bool> Keep { get; init; }

    public List<(int MulligansTaken, int KeepSize, int HandInView)> Asked { get; } = [];

    public bool KeepHand(PilotView view, MulliganChoice choice)
    {
        Asked.Add((choice.MulligansTaken, choice.KeepSize, view.Hand.Count));
        return Keep(choice);
    }

    public IReadOnlyList<DeckCard> ChooseBottom(PilotView view, BottomChoice choice) => [.. choice.Hand.TakeLast(choice.Count)];
}
