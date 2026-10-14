namespace MtgArchive.Simulation;

/// <summary>
/// The single definition of the cards-seen convention: cards seen by turn N
/// = 7 + N with a turn-1 draw (CR 103.8c) and 7 + max(0, N − 1) on the play
/// (CR 103.8a), from the format's game rules. The opener is 7 cards, that
/// is, turn 0. Tier 1 and tier 2 must never diverge on this: tier 1 consumes
/// this one definition, and the engine plays the same game rules.
/// </summary>
public static class CardsSeen
{
    public const int Opener = 7;

    public static int ByTurn(int turn, GameRules rules)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(turn);
        return Opener + (rules.StartingPlayerDraws ? turn : Math.Max(0, turn - 1));
    }
}
