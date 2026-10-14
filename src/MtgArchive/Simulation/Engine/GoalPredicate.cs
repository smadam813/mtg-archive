namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The closed goal-predicate vocabulary a run measures (ADR 0003):
/// <c>on_battlefield(card, by_turn)</c>, <c>cast(card, by_turn)</c>, and
/// <c>all_of</c> composition. A name outside the vocabulary is a hard
/// failure at parse time. The same predicate is the goal-aware pilot's
/// objective. <c>on_battlefield</c> is point-in-time: the card is on the
/// battlefield at the end of turn <c>by_turn</c>. Removal before that turn
/// fails it, and a rebuild before that turn meets it. <c>cast</c> counts
/// resolved casts only. A countered cast never satisfies it. The predicate
/// reads an <see cref="IGoalBoard"/>, so it evaluates on any engine's game
/// and on the pilot view.
/// </summary>
public abstract record GoalPredicate
{
    public const string OnBattlefieldKind = "on_battlefield";

    public const string CastKind = "cast";

    public const string AllOfKind = "all_of";

    /// <summary>The vocabulary, closed at three names.</summary>
    public static readonly IReadOnlyList<string> Vocabulary = [OnBattlefieldKind, CastKind, AllOfKind];

    private GoalPredicate()
    {
    }

    /// <summary>The one goal the named sources agree on, or null when none
    /// names one. A null goal names nothing. Two different goals throw, and
    /// the message names each source with its goal.</summary>
    public static GoalPredicate? SingleOf(IEnumerable<(string Source, GoalPredicate? Goal)> named)
    {
        var set = named.Where(n => n.Goal is not null).ToList();
        var distinct = set.Select(n => n.Goal!).Distinct().ToList();
        if (distinct.Count > 1)
        {
            throw new ArgumentException($"The run names more than one goal: {string.Join("; ", set.Select(n => $"{n.Source} {n.Goal}"))}.");
        }

        return distinct.SingleOrDefault();
    }

    public abstract string Kind { get; }

    /// <summary>The last turn at which any part of the predicate is decided.</summary>
    public abstract int LastTurn { get; }

    /// <summary>The leaf predicates, in order.</summary>
    public abstract IReadOnlyList<GoalPredicate> Leaves { get; }

    /// <summary>The distinct card names the predicate names.</summary>
    public IReadOnlyList<string> CardNames
        => Leaves.Select(l => l.CardName!).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The card a leaf names; null for a composition.</summary>
    public abstract string? CardName { get; }

    /// <summary>
    /// Whether the predicate holds now, deadline aside. The engine asks at
    /// the end of each turn. The answer at the end of a leaf's own turn is
    /// its verdict. The answer at the end of every turn is one point of
    /// the per-turn curve.
    /// </summary>
    public abstract bool HoldsNow(IGoalBoard board);

    /// <summary>The goal still wants the card, on either face: a leaf names
    /// it and does not hold now. The deadline does not matter here. After
    /// it passes, the card still counts for the curve.</summary>
    public bool Wants(IGoalBoard board, DeckCard card) => WantedFace(board, card) is not null;

    /// <summary>The leaves that still want the card, on either face.</summary>
    public IEnumerable<GoalPredicate> LeavesWanting(IGoalBoard board, DeckCard card)
        => LeavesWantingFace(board, card).Concat(card.AsBackFace() is { } back ? LeavesWantingFace(board, back) : []);

    private IEnumerable<GoalPredicate> LeavesWantingFace(IGoalBoard board, DeckCard face)
        => Leaves.Where(l => face.Answers(l.CardName!) && !l.HoldsNow(board));

    /// <summary>The face of the card the goal still wants: the card as it
    /// is, or its back face when only the back face is wanted. Null when
    /// the goal wants neither.</summary>
    public DeckCard? WantedFace(IGoalBoard board, DeckCard card)
        => LeavesWantingFace(board, card).Any() ? card
            : card.AsBackFace() is { } back && LeavesWantingFace(board, back).Any() ? back
            : null;

    /// <summary>The card is on the battlefield at the end of turn <see cref="ByTurn"/>.</summary>
    public sealed record OnBattlefield(string Card, int ByTurn) : GoalPredicate
    {
        public override string Kind => OnBattlefieldKind;

        public override int LastTurn => ByTurn;

        public override string? CardName => Card;

        public override IReadOnlyList<GoalPredicate> Leaves => [this];

        public override bool HoldsNow(IGoalBoard board)
            => board.BattlefieldCards.Any(c => c.Answers(Card));

        public override string ToString() => $"{Kind}(\"{Card}\", by_turn {ByTurn})";
    }

    /// <summary>A cast of the card resolved on or before turn <see cref="ByTurn"/>.</summary>
    public sealed record Cast(string Card, int ByTurn) : GoalPredicate
    {
        public override string Kind => CastKind;

        public override int LastTurn => ByTurn;

        public override string? CardName => Card;

        public override IReadOnlyList<GoalPredicate> Leaves => [this];

        public override bool HoldsNow(IGoalBoard board)
            => board.ResolvedCasts.Any(c => c.Card.Answers(Card));

        public override string ToString() => $"{Kind}(\"{Card}\", by_turn {ByTurn})";
    }

    /// <summary>Every member holds, each by its own turn.</summary>
    public sealed record AllOf(IReadOnlyList<GoalPredicate> Goals) : GoalPredicate
    {
        private readonly IReadOnlyList<GoalPredicate> _leaves = Goals.SelectMany(g => g.Leaves).ToList();

        public override string Kind => AllOfKind;

        public override int LastTurn => Goals.Max(g => g.LastTurn);

        public override string? CardName => null;

        public override IReadOnlyList<GoalPredicate> Leaves => _leaves;

        public override bool HoldsNow(IGoalBoard board) => Goals.All(g => g.HoldsNow(board));

        public override string ToString() => $"{Kind}({string.Join(", ", Goals)})";

        public bool Equals(AllOf? other) => other is not null && Goals.SequenceEqual(other.Goals);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var goal in Goals)
            {
                hash.Add(goal);
            }

            return hash.ToHashCode();
        }
    }
}
