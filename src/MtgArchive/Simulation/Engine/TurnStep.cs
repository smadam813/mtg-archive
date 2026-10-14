namespace MtgArchive.Simulation.Engine;

/// <summary>The step of a turn a game is in. <see cref="Mulligan"/> is the
/// pre-game loop.</summary>
public enum TurnStep
{
    Mulligan,

    Untap,
    Upkeep,
    Draw,
    Main,
    End,
    Cleanup,
}
