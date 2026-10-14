namespace MtgArchive.Simulation;

/// <summary>
/// Category draw probability: P(≥ k cards of a caller-supplied card set
/// among the cards seen by turn N under the format's game rules). The set
/// carries no semantics — there is no server-side category vocabulary — and
/// single-card odds are the k = 1, one-card-set case of this same path,
/// never a separate one.
/// </summary>
public static class CategoryDraw
{
    public static double AtLeastByTurn(
        ResolvedDecklist decklist, IReadOnlySet<Guid> category, int k, int turn, GameRules rules)
        => Hypergeometric.TailAtLeast(
            k,
            category.Sum(decklist.QuantityOf),
            decklist.DeckSize,
            CardsSeen.ByTurn(turn, rules));
}
