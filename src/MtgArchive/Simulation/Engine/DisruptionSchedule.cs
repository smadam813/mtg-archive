namespace MtgArchive.Simulation.Engine;

/// <summary>One end-of-turn event that fired: its outcome and the
/// permanents it removes, in order.</summary>
public sealed record DisruptionFire(DisruptionOutcome Outcome, IReadOnlyList<ViewPermanent> Victims);

/// <summary>
/// The one-shot scheduler of a disruption profile and its targeting rule
/// (ADR 0005). There is no response window: every event resolves
/// unconditionally. The rule is adversarial toward the goal: the most
/// expensive goal card the event can still affect, then a name tie-break.
/// With no such card present, the most expensive legal target. There is no
/// opponent-policy seam.
/// <para>
/// The schedule picks the victims and keeps the fired bookkeeping; the
/// engine adapter carries the victims out. A counterspell fires when the
/// pilot casts its target, after the pilot spends the mana. Targeted
/// removal and a board wipe fire at the end of the turn, after the end-step
/// window, and the engine evaluates the goal afterward, so point-in-time
/// <c>on_battlefield</c> sees the loss. The batch runner hands the profile
/// to the engine, never to the pilot.
/// </para>
/// </summary>
public sealed class DisruptionSchedule
{
    private readonly List<DisruptionOutcome> _events;

    /// <summary>The cards <c>on_battlefield</c> leaves name: the only goal
    /// cards a permanent's loss can affect. A <c>cast</c> leaf holds for
    /// good once its cast resolves.</summary>
    private readonly HashSet<string> _battlefieldGoalNames;

    private readonly bool _goalNamesACreature;

    /// <param name="profile">The events, in profile order.</param>
    /// <param name="goal">The run's goal; null for a run without one.</param>
    /// <param name="cards">The deck's cards: a board wipe waits for a goal
    /// creature only when the goal names a creature among them.</param>
    public DisruptionSchedule(DisruptionProfile profile, GoalPredicate? goal, IEnumerable<DeckCard> cards)
    {
        Goal = goal;
        _events = profile.Events.Select(e => new DisruptionOutcome(e)).ToList();
        _battlefieldGoalNames = (goal?.Leaves ?? [])
            .OfType<GoalPredicate.OnBattlefield>()
            .Select(l => l.Card)
            .ToHashSet(StringComparer.Ordinal);
        // A back face counts by its own name only, so a full-name goal reads
        // the front face as it did before back faces existed.
        _goalNamesACreature = cards.Any(c =>
            (c.IsCreature && _battlefieldGoalNames.Any(c.Answers))
            || (c.BackFace is { IsCreature: true } back && _battlefieldGoalNames.Contains(back.Name)));
    }

    public GoalPredicate? Goal { get; }

    /// <summary>One outcome per event of the profile, in profile order,
    /// with their fates so far. The batch runner aggregates on this order.</summary>
    public IReadOnlyList<DisruptionOutcome> Events => _events;

    /// <summary>
    /// The cast of <paramref name="card"/> meets an armed counterspell: the
    /// counterspell fires and comes back, or null when the cast resolves.
    /// The adapter then puts the card away: a card to the graveyard, a
    /// commander to the command zone. The mana stays spent and the tax
    /// rises. The cast is the target when the goal wants the card. It is
    /// also the target when no wanted card is <paramref name="reachable"/>
    /// (in hand, library, or command zone): the spell on the stack is then
    /// the only legal target.
    /// </summary>
    public DisruptionOutcome? Counter(int turn, DeckCard card, IGoalBoard board, IEnumerable<DeckCard> reachable)
    {
        if (ProposeCounter(turn, card, board, reachable) is not { } counter)
        {
            return null;
        }

        counter.Fire(turn, [card.Name]);
        return counter;
    }

    /// <summary>
    /// Interactive disruption (ADR 0013): the armed counterspell that the
    /// tier-3 counter predicate would fire on this cast, unchanged, or null.
    /// Nothing is spent: the schedule commits only when the engine reports
    /// that the opponent cast the card (<see cref="Commit"/>), and a card
    /// that was not cast leaves the event armed (<see cref="NotCast"/>).
    /// </summary>
    public DisruptionOutcome? ProposeCounter(int turn, DeckCard card, IGoalBoard board, IEnumerable<DeckCard> reachable)
    {
        var counter = _events.FirstOrDefault(e => e.Event.Kind == DisruptionKind.Counterspell && e.IsArmed(turn));
        return counter is null || (Goal is { } goal && !goal.Wants(board, card) && reachable.Any(c => goal.Wants(board, c)))
            ? null
            : counter;
    }

    /// <summary>Spends a proposed event: the opponent cast its card on
    /// <paramref name="turn"/>, at these victims. A non-null
    /// <paramref name="answeredBy"/> records that our deck answered it,
    /// with these cards.</summary>
    public void Commit(DisruptionOutcome proposed, int turn, IReadOnlyList<VictimHit> hits, IReadOnlyList<string>? answeredBy = null)
    {
        Require(proposed);
        proposed.Commit(turn, hits, answeredBy);
    }

    /// <summary>A proposed event whose card was not cast: it stays armed.</summary>
    public void NotCast(DisruptionOutcome proposed, string reason)
    {
        Require(proposed);
        proposed.MarkNotCast(reason);
    }

    private void Require(DisruptionOutcome proposed)
    {
        if (!_events.Contains(proposed))
        {
            throw new ArgumentException("The outcome is not an event of this schedule.", nameof(proposed));
        }
    }

    /// <summary>The end-of-turn events, in profile order: each armed
    /// targeted removal and board wipe fires when it sees a qualifying
    /// target on the battlefield as it stands after the fires before it.</summary>
    public IReadOnlyList<DisruptionFire> EndOfTurn(int turn, IReadOnlyList<ViewPermanent> battlefield)
    {
        var standing = battlefield.ToList();
        var fires = new List<DisruptionFire>();
        foreach (var outcome in _events.Where(e => e.IsArmed(turn)))
        {
            IReadOnlyList<ViewPermanent> victims = outcome.Event.Kind switch
            {
                DisruptionKind.TargetedRemoval => RemovalTarget(standing) is { } target ? [target] : [],
                DisruptionKind.BoardWipe => WipeTargets(standing),
                _ => [],
            };
            if (victims.Count == 0)
            {
                continue;
            }

            outcome.Fire(turn, [.. victims.Select(v => v.Card.Name)]);
            fires.Add(new DisruptionFire(outcome, victims));
            standing.RemoveAll(p => victims.Contains(p));
        }

        return fires;
    }

    /// <summary>
    /// Interactive disruption (ADR 0013): the first armed end-of-turn event
    /// in profile order that is not in <paramref name="tried"/> and has a
    /// candidate on this battlefield, with its candidates in order, or null.
    /// Nothing is spent: the schedule commits only on the engine's report
    /// (<see cref="Commit"/>, <see cref="NotCast"/>). Targeted removal
    /// proposes the removal order. A board wipe proposes under the unchanged
    /// tier-3 wipe condition, and its candidates are the creatures the wipe
    /// takes; the disruption card's own text decides what dies.
    /// </summary>
    public EndOfTurnProposal? ProposeEndOfTurn(int turn, IReadOnlyList<ViewPermanent> battlefield, IReadOnlySet<DisruptionOutcome> tried)
    {
        foreach (var outcome in _events.Where(e => e.IsArmed(turn) && !tried.Contains(e)))
        {
            IReadOnlyList<ViewPermanent> candidates = outcome.Event.Kind switch
            {
                DisruptionKind.TargetedRemoval => RemovalOrder(battlefield),
                DisruptionKind.BoardWipe => WipeTargets(battlefield),
                _ => [],
            };
            if (candidates.Count > 0)
            {
                return new EndOfTurnProposal(outcome, candidates);
            }
        }

        return null;
    }

    /// <summary>
    /// The removal order (ADR 0013), which extends the tier-3 rule: the
    /// goal-named nonland permanents by mana value descending, then by name,
    /// then every other nonland permanent in the same order. The tier-3
    /// target is its first entry.
    /// </summary>
    public IReadOnlyList<ViewPermanent> RemovalOrder(IReadOnlyList<ViewPermanent> battlefield)
    {
        var legal = battlefield.Where(p => !p.Card.IsLand).ToList();
        return [.. ByCost(legal.Where(IsGoalCard)), .. ByCost(legal.Where(p => !IsGoalCard(p)))];
    }

    /// <summary>The most expensive goal-named nonland permanent, then the
    /// most expensive nonland permanent; name breaks ties. Null when the
    /// battlefield holds no nonland permanent.</summary>
    private ViewPermanent? RemovalTarget(IReadOnlyList<ViewPermanent> battlefield) => RemovalOrder(battlefield).FirstOrDefault();

    /// <summary>Every creature when one is a goal creature, or when the
    /// goal names no creature; otherwise nothing.</summary>
    private List<ViewPermanent> WipeTargets(IReadOnlyList<ViewPermanent> battlefield)
    {
        var creatures = battlefield.Where(p => p.Card.IsCreature).ToList();
        return !_goalNamesACreature || creatures.Any(IsGoalCard) ? creatures : [];
    }

    private bool IsGoalCard(ViewPermanent permanent) => _battlefieldGoalNames.Any(permanent.Card.Answers);

    private static IEnumerable<ViewPermanent> ByCost(IEnumerable<ViewPermanent> permanents)
        => permanents.OrderByDescending(p => p.Card.ManaValue).ThenBy(p => p.Card.Name, StringComparer.Ordinal);
}

/// <summary>An end-of-turn event the schedule proposes under interactive
/// disruption (ADR 0013), and its candidates in order.</summary>
public sealed record EndOfTurnProposal(DisruptionOutcome Outcome, IReadOnlyList<ViewPermanent> Candidates);
