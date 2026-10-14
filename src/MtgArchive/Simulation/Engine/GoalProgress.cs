namespace MtgArchive.Simulation.Engine;

/// <summary>
/// One run's evaluation of its goal. The engine adapter calls
/// <see cref="EndOfTurn"/> after each turn, at the state a fatal turn ended
/// in, and <see cref="Finish"/> when the run ends. Each call takes the sink
/// the run's log lines go to.
/// <see cref="Curve"/> records, per turn, whether the whole predicate held
/// at that turn's end, deadline aside. Index 0 is turn 1. It is the run's
/// point on the per-turn cumulative curve. <see cref="Verdict"/> is the
/// run's success: every leaf held at the end of its own turn. A leaf whose
/// turn the run never finished fails.
/// </summary>
public sealed class GoalProgress(GoalPredicate goal)
{
    private readonly List<bool> _curve = [];

    private readonly Dictionary<int, bool> _leafVerdicts = [];

    private readonly Dictionary<int, int> _leafFirstHeld = [];

    public GoalPredicate Goal { get; } = goal;

    public IReadOnlyList<bool> Curve => _curve;

    /// <summary>Whether the goal held at the end of <paramref name="turn"/>;
    /// false when the run never finished that turn.</summary>
    public bool HeldAtEndOfTurn(int turn) => turn >= 1 && turn <= _curve.Count && _curve[turn - 1];

    /// <summary>The turn by which every leaf had held at some turn's end,
    /// deadlines aside; null until every leaf has held. A success always
    /// has one. The leaves need not hold on the same turn, so the curve
    /// can be false at this turn.</summary>
    public int? MetTurn => _leafFirstHeld.Count == Goal.Leaves.Count ? _leafFirstHeld.Values.Max() : null;

    /// <summary>Null until decided.</summary>
    public bool? Verdict { get; private set; }

    /// <summary>
    /// <see cref="Verdict"/> and <see cref="MetTurn"/> are both set. The
    /// envelope's success count reads the verdict and its curve reads the
    /// met turn, and no later turn changes either, so a run whose goal is
    /// settled may stop early.
    /// </summary>
    public bool IsSettled => Verdict is not null && MetTurn is not null;

    /// <summary>Records the end of the current turn. A turn the loop
    /// never played (a run resumed from a later turn) records false.</summary>
    public void EndOfTurn(IGoalBoard board, Action<string> record)
    {
        if (board.Turn <= _curve.Count)
        {
            throw new InvalidOperationException($"Goal evaluation already recorded turn {board.Turn}.");
        }

        while (_curve.Count < board.Turn - 1)
        {
            _curve.Add(false);
        }

        _curve.Add(Goal.HoldsNow(board));
        var leaves = Goal.Leaves;
        for (var i = 0; i < leaves.Count; i++)
        {
            var held = leaves[i].HoldsNow(board);
            if (held)
            {
                _leafFirstHeld.TryAdd(i, board.Turn);
            }

            if (leaves[i].LastTurn != board.Turn || _leafVerdicts.ContainsKey(i))
            {
                continue;
            }

            _leafVerdicts[i] = held;
            record($"Goal {leaves[i]} {(held ? "holds" : "fails")} at the end of turn {board.Turn}.");
        }

        if (Verdict is null && (_leafVerdicts.Values.Any(v => !v) || _leafVerdicts.Count == leaves.Count))
        {
            Decide(record, _leafVerdicts.Values.All(v => v));
        }
    }

    /// <summary>Fails an undecided goal: the run ended first.</summary>
    public void Finish(Action<string> record)
    {
        if (Verdict is null)
        {
            record("The run ended with the goal undecided.");
            Decide(record, false);
        }
    }

    private void Decide(Action<string> record, bool met)
    {
        Verdict = met;
        record(met ? $"Goal met: {Goal}." : $"Goal missed: {Goal}.");
    }
}
