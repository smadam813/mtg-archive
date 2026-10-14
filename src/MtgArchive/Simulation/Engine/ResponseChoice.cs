namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The response window's choice point (ADR 0013; glossary: Response
/// window, Choice point): the disruption card on top of the stack and the
/// cards it aims at. The pilot answers with one of the view's actions, or
/// none. The adapter maps the threat through the disruption card table,
/// never through the deck's card lookup.
/// </summary>
/// <param name="Threat">The disruption card from the fixed table.</param>
/// <param name="ThreatId">The engine's id of the card on the stack.</param>
/// <param name="Victims">The cards it aims at, in the engine's order.</param>
public sealed record ResponseChoice(DisruptionCard Threat, string ThreatId, IReadOnlyList<ResponseVictim> Victims);

/// <summary>One card a disruption card aims at: its engine id, and the
/// face it is, a spell on the stack or a permanent.</summary>
public sealed record ResponseVictim(string Id, DeckCard Card);
