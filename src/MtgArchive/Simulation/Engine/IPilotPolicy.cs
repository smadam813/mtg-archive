namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The pilot policy (glossary): the one interface that makes every choice
/// the engine asks the pilot for. It reads only the <see cref="PilotView"/>.
/// It has no payment method: the engine pays mana and chooses targets,
/// modes, and X. Play skill lives here, never in card data (ADR 0002).
/// Implementations are stateless and deterministic given the view.
/// </summary>
public interface IPilotPolicy
{
    /// <summary>The name a result envelope reports for this policy.</summary>
    string Name { get; }

    /// <summary>The id of one of <see cref="PilotView.Actions"/>, or null to pass.</summary>
    string? ChooseAction(PilotView view);

    /// <summary>The id of one of <see cref="PickChoice.Options"/>.</summary>
    string Pick(PilotView view, PickChoice choice);

    /// <summary>Cleanup discard to hand size: exactly <paramref name="count"/>
    /// cards from <see cref="PilotView.Hand"/>.</summary>
    IReadOnlyList<DeckCard> ChooseDiscards(PilotView view, int count);

    /// <summary>The response window (interactive disruption, ADR 0013):
    /// the id of one of <see cref="PilotView.Actions"/> that answers
    /// <see cref="ResponseChoice.Threat"/>, or null to let it resolve.</summary>
    string? Respond(PilotView view, ResponseChoice choice);
}

/// <summary>
/// The mulligan policy (glossary): the keep and bottom choices of the
/// pre-game loop. The engine deals, reshuffles, and enforces the London
/// count; an implementation decides from the <see cref="PilotView"/>.
/// </summary>
public interface IMulliganPolicy
{
    /// <summary>The name a result envelope reports for this policy.</summary>
    string Name { get; }

    /// <summary>Keep <see cref="MulliganChoice.Hand"/> (true) or mulligan again (false).</summary>
    bool KeepHand(PilotView view, MulliganChoice choice);

    /// <summary>The London bottom: exactly <see cref="BottomChoice.Count"/>
    /// cards from <see cref="BottomChoice.Hand"/>, first under first.</summary>
    IReadOnlyList<DeckCard> ChooseBottom(PilotView view, BottomChoice choice);
}
