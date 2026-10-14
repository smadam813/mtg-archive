namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The part of a game that a goal predicate reads: the turn, the cards on
/// the battlefield, and the casts that resolved. A game of any engine and
/// the pilot view both implement it, so one predicate evaluates on each.
/// </summary>
public interface IGoalBoard
{
    int Turn { get; }

    /// <summary>The card of each permanent on the battlefield, as the
    /// battlefield shows it: a back face is the face it entered as.</summary>
    IEnumerable<DeckCard> BattlefieldCards { get; }

    /// <summary>Every cast that resolved, in order. A countered cast is absent.</summary>
    IReadOnlyList<ResolvedCast> ResolvedCasts { get; }
}
