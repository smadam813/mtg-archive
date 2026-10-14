namespace MtgArchive.Simulation.Engine;

/// <summary>One line of a run's log: the turn and step it happened in.</summary>
public sealed record GameLogEntry(int Turn, TurnStep Step, string Message);
