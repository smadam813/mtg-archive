namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The goal-aware greedy: the play policy of ADR 0003. The run's goal
/// predicate is its objective. At every main-phase choice its priority
/// order is: goal-satisfying actions, tutors that can find a goal piece,
/// the land drop, ramp, draw, other castables. Within one tier an action
/// that keeps every needed color goes first (see
/// <see cref="GoalHeuristics.LosesNeededColor"/>). Then the action with
/// the higher mana value goes first, then the order of the view's actions.
/// An action that only a card in hand can pay for (Simian Spirit Guide)
/// must be a goal action, a tutor, or ramp, because the payment exiles the
/// card.
/// </summary>
/// <remarks>
/// Every other choice follows the same order. A search picks a goal piece
/// first. A put-back, a discard, and a bottom shed the least useful card
/// first. Trigger order resolves the most useful source first. A scry
/// keeps goal pieces, tutors for one, and one land when the hand holds
/// none, and bottoms the rest. Cards that stay on top go most useful on
/// top. "You may shuffle" shuffles when the seen cards hold no goal piece
/// and no tutor. A name pick names a card with no activated ability. The
/// engine pays mana; the pilot never casts a card whose mana ability works
/// from hand unless the goal wants it. The pilot is deterministic given
/// the view and does no lookahead. It reads only the pilot view, which
/// holds what the player knows. It never reads the unseen library order,
/// so all randomness comes from the shuffle. It never sees a disruption
/// profile (tier-3 invariant). In a response window it answers a
/// disruption card that threatens the goal with the cheapest proven
/// answer (<see cref="Respond"/>). Its results are floors, labeled by
/// <see cref="FloorLabel"/>.
/// </remarks>
public sealed class GoalAwarePilot(GoalPredicate goal) : IPilotPolicy, IGoalDirected
{
    public const string PolicyName = "goal-aware-greedy";

    /// <summary>The floor label of a probability under this policy, in the
    /// envelope's exact format: ">= X%, greedy policy". The percent rounds
    /// down to one decimal, so the label never overstates.</summary>
    public static string FloorLabel(double probability)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(probability);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(probability, 1.0);
        var percent = Math.Floor(probability * 1000) / 10;
        return $">= {percent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}%, greedy policy";
    }

    public GoalPredicate Goal { get; } = goal;

    public string Name => PolicyName;

    public string? ChooseAction(PilotView view) => ChooseAction(view, (_, _) => true);

    /// <summary>The priority order over the actions that <paramref name="allow"/>
    /// lets through, given the action and its tier. The hold policy
    /// (ADR 0014) filters with it and ranks the rest the same way.</summary>
    internal string? ChooseAction(PilotView view, Func<EngineAction, ActionTier, bool> allow)
    {
        EngineAction? best = null;
        (ActionTier Tier, bool LosesColor, decimal NegatedManaValue)? bestRank = null;
        foreach (var action in view.Actions)
        {
            if (GoalHeuristics.Tier(view, Goal, action) is not { } tier
                || (tier > ActionTier.Ramp && GoalHeuristics.NeedsHandMana(view, action))
                || !allow(action, tier))
            {
                continue;
            }

            var rank = (tier, GoalHeuristics.LosesNeededColor(view, Goal, action), -GoalHeuristics.ManaValue(action));
            if (bestRank is null || rank.CompareTo(bestRank.Value) < 0)
            {
                best = action;
                bestRank = rank;
            }
        }

        return best?.Id;
    }

    public string Pick(PilotView view, PickChoice choice)
    {
        var cards = choice.Options.Where(o => !o.IsNothing).ToList();
        return choice.Purpose switch
        {
            PickPurpose.TriggerOrder => MostUseful(view, cards).Id,
            PickPurpose.Scry => ScryPick(view, choice, cards).Id,
            PickPurpose.OrderTop or PickPurpose.PutBack => LeastUseful(view, cards).Id,
            PickPurpose.Shuffle => choice.Find(choice.Seen!.Any(c => Advances(view, c)) ? PickOption.NoShuffleId : PickOption.ShuffleId)!.Id,
            PickPurpose.Tutor => (cards.FirstOrDefault(o => Goal.Wants(view, o.Card!))
                ?? cards.FirstOrDefault(o => o.Card!.IsLand && GoalHeuristics.ProducesNeededColor(view, Goal, o.Card!))
                ?? cards[0]).Id,
            PickPurpose.Name => NameWithoutSelfLock(view, choice).Id,
            _ => choice.Options[0].Id,
        };
    }

    public IReadOnlyList<DeckCard> ChooseDiscards(PilotView view, int count)
        => GoalHeuristics.LeastUsefulFirst(view, Goal, view.Hand).Take(count).ToList();

    /// <summary>
    /// The response rule (ADR 0013), stateless and with no lookahead. With
    /// no goal threat it lets the disruption card resolve. Otherwise it
    /// takes the cheapest action whose response feature proves an answer:
    /// a counter whose reach covers the card's types and colors, or a
    /// one-shot protection grant that answers the card and whose scope
    /// covers a threatened victim. Phase-out qualifies only when every
    /// threatened leaf is due after this turn, because the permanent is
    /// still phased out at this turn's end. It skips a card the goal wants,
    /// an "unless its controller pays" counter and a pact. Mana from a card
    /// in hand may pay. Ties go to the view's order.
    /// </summary>
    public string? Respond(PilotView view, ResponseChoice choice)
    {
        var threatened = Threatened(view, choice);
        if (threatened.Count == 0)
        {
            return null;
        }

        var phaseOutHolds = threatened.All(v => LeavesDue(view, v.Card).All(l => l.ByTurn > view.Turn));
        EngineAction? best = null;
        var bestManaValue = 0m;
        foreach (var action in view.Actions)
        {
            if (!Answers(view, action, choice.Threat, threatened, phaseOutHolds))
            {
                continue;
            }

            var manaValue = GoalHeuristics.ManaValue(action);
            if (best is null || manaValue < bestManaValue)
            {
                best = action;
                bestManaValue = manaValue;
            }
        }

        return best?.Id;
    }

    /// <summary>The victims that threaten the goal. A counterspell's victim
    /// does when the goal wants its face. A removal victim does when it
    /// answers an <c>on_battlefield</c> leaf due this turn or later. A wipe's
    /// victims are the creatures on the battlefield, and each one that
    /// answers such a leaf threatens the goal.</summary>
    private List<ResponseVictim> Threatened(PilotView view, ResponseChoice choice) => choice.Threat.Kind switch
    {
        DisruptionKind.Counterspell => [.. choice.Victims.Where(v => Goal.Wants(view, v.Card))],
        DisruptionKind.TargetedRemoval or DisruptionKind.BoardWipe => [.. choice.Victims.Where(v => LeavesDue(view, v.Card).Any())],
        _ => [],
    };

    /// <summary>The <c>on_battlefield</c> leaves the permanent answers whose
    /// turn is this turn or later: losing it now can fail each one.</summary>
    private IEnumerable<GoalPredicate.OnBattlefield> LeavesDue(PilotView view, DeckCard permanent)
        => Goal.Leaves.OfType<GoalPredicate.OnBattlefield>().Where(l => l.ByTurn >= view.Turn && permanent.Answers(l.Card));

    /// <summary>A cast that proves an answer, on a card the goal does not
    /// want: a hard counter whose reach covers the threat, or a grant that
    /// answers the threat and covers a threatened victim. A phase-out grant
    /// also needs <paramref name="phaseOutHolds"/>.</summary>
    private bool Answers(PilotView view, EngineAction action, DisruptionCard threat, List<ResponseVictim> threatened, bool phaseOutHolds)
    {
        if (action.Kind is not (EngineActionKind.Cast or EngineActionKind.CastCommander)
            || Goal.Wants(view, action.Face)
            || action.Features.Response is not { } response)
        {
            return false;
        }

        return (response.Counter is { UnlessPays: null, LosesGame: false } counter && counter.Reaches(threat.Types, threat.Colors))
            || response.Grants.Any(g => g.Answers(threat)
                && threatened.Any(v => g.Covers(v.Card))
                && (g.Grant != ProtectionGrant.PhaseOut || phaseOutHolds));
    }

    /// <summary>The name pick with no opponent to lock: a card with no
    /// activated ability, mana abilities included, so the lock never touches
    /// the deck's own cards. The first option when every card has one.</summary>
    private static PickOption NameWithoutSelfLock(PilotView view, PickChoice choice)
        => choice.Options.FirstOrDefault(o => o.Card is { } card && !view.Features(card).HasActivatedAbility)
            ?? choice.Options[0];

    /// <summary>A goal piece, or a tutor that finds one.</summary>
    private bool Advances(PilotView view, DeckCard card) => GoalHeuristics.Classify(view, Goal, card) <= ActionTier.Tutor;

    private PickOption LeastUseful(PilotView view, List<PickOption> cards)
    {
        var leastUseful = GoalHeuristics.LeastUsefulFirst(view, Goal, cards.Select(o => o.Card!))[0];
        return cards.First(o => o.Card == leastUseful);
    }

    private PickOption MostUseful(PilotView view, List<PickOption> cards)
    {
        var mostUseful = GoalHeuristics.LeastUsefulFirst(view, Goal, cards.Select(o => o.Card!))[^1];
        return cards.First(o => o.Card == mostUseful);
    }

    /// <summary>
    /// A scry keeps every goal piece and every tutor for one. With no land
    /// in hand and none kept, it also keeps one land, a needed color first.
    /// It bottoms the other seen cards, least useful first, then keeps the
    /// rest.
    /// </summary>
    private PickOption ScryPick(PilotView view, PickChoice choice, List<PickOption> cards)
    {
        var bottom = choice.Seen!.Where(c => !Advances(view, c)).ToList();
        var keepsLand = choice.Seen!.Any(c => c.IsLand && Advances(view, c));
        if (!keepsLand && !view.Hand.Any(c => c.IsLand)
            && bottom.Where(c => c.IsLand).OrderByDescending(c => GoalHeuristics.ProducesNeededColor(view, Goal, c)).FirstOrDefault() is { } land)
        {
            bottom.Remove(land);
        }

        if (bottom.Count == 0)
        {
            return choice.Find(PickOption.KeepRestId)!;
        }

        var shed = GoalHeuristics.LeastUsefulFirst(view, Goal, bottom)[0];
        return cards.First(o => o.Card!.Name == shed.Name);
    }
}
