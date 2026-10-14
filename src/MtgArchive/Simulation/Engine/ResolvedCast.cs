namespace MtgArchive.Simulation.Engine;

/// <summary>One resolved cast: the card and the turn it resolved on.</summary>
public sealed record ResolvedCast(DeckCard Card, int Turn);
