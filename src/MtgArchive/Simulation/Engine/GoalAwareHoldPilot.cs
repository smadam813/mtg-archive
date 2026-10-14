namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The goal-aware hold policy (ADR 0014): the opt-in play policy for
/// interactive disruption. It plays as <see cref="GoalAwarePilot"/> with two
/// holds on the main phase, and every other choice is the greedy pilot's.
/// <list type="bullet">
/// <item><b>Card hold, from turn 1.</b> It never casts a response card whose
/// only rank is the "anything else" tier. A response card that the greedy
/// order ranks higher (it draws, tutors or ramps) plays as it does there.</item>
/// <item><b>Mana reserve, while the goal holds.</b> While the whole goal
/// predicate holds now, it keeps untapped mana worth the cheapest response
/// card in hand that answers removal or a wipe aimed at a goal card. It takes
/// an action that costs mana only when the untapped mana left after it is
/// at least the reserve. When the goal stops holding, the reserve stops. The
/// reserve counts mana value only, never colors.</item>
/// </list>
/// The response window keeps the greedy rule (ADR 0013). The policy is
/// stateless and reads only the pilot view, so it never sees a disruption
/// profile.
/// </summary>
public sealed class GoalAwareHoldPilot(GoalPredicate goal) : IPilotPolicy, IGoalDirected
{
    public const string PolicyName = "goal-aware-hold";

    private readonly GoalAwarePilot _greedy = new(goal);

    public GoalPredicate Goal => _greedy.Goal;

    public string Name => PolicyName;

    public string? ChooseAction(PilotView view)
    {
        var victims = Victims(view);
        var answering = victims.Count == 0 ? [] : AnsweringCards(view, victims);
        var available = answering.Count == 0 ? 0 : Available(view);
        return _greedy.ChooseAction(view, (action, tier) => !IsHeldCard(action, tier) && LeavesReserve(view, action, Reserve(answering, action), available));
    }

    public string Pick(PilotView view, PickChoice choice) => _greedy.Pick(view, choice);

    public IReadOnlyList<DeckCard> ChooseDiscards(PilotView view, int count) => _greedy.ChooseDiscards(view, count);

    public string? Respond(PilotView view, ResponseChoice choice) => _greedy.Respond(view, choice);

    /// <summary>A cast of a response card that has no rank above "anything
    /// else".</summary>
    private static bool IsHeldCard(EngineAction action, ActionTier tier)
        => tier == ActionTier.Other
        && action.Kind is EngineActionKind.Cast or EngineActionKind.CastCommander
        && action.Features.Response is { } response
        && DisruptionCards.AnswersAny(response);

    /// <summary>
    /// The goal cards on the battlefield while the whole goal holds now: the
    /// permanents that answer an <c>on_battlefield</c> leaf due this turn or
    /// later, the cards the greedy response rule protects. Empty before the
    /// goal holds.
    /// </summary>
    private List<DeckCard> Victims(PilotView view)
        => !Goal.HoldsNow(view) ? []
        : [.. view.Battlefield
            .Where(p => Goal.Leaves.OfType<GoalPredicate.OnBattlefield>().Any(l => l.ByTurn >= view.Turn && p.Card.Answers(l.Card)))
            .Select(p => p.Card)];

    /// <summary>The hand cards whose response answers targeted removal or a
    /// board wipe aimed at a victim. A counter that reaches the removal
    /// counts.</summary>
    private static List<DeckCard> AnsweringCards(PilotView view, List<DeckCard> victims)
        => [.. view.Hand.Where(card => view.Features(card).Response is { } response && Answers(response, victims))];

    /// <summary>
    /// The mana value of the cheapest answering card left in hand after
    /// <paramref name="action"/>; zero with no such card. The card that the
    /// action casts has left the hand, so it never reserves mana for itself.
    /// </summary>
    private static int Reserve(List<DeckCard> answering, EngineAction action)
    {
        var skipped = action.Kind is not EngineActionKind.Cast;
        var reserve = int.MaxValue;
        foreach (var card in answering)
        {
            if (!skipped && card == action.Face)
            {
                skipped = true;
                continue;
            }

            reserve = Math.Min(reserve, (int)card.ManaValue);
        }

        return reserve == int.MaxValue ? 0 : reserve;
    }

    private static bool Answers(ResponseFeature response, List<DeckCard> victims)
    {
        var removal = DisruptionCards.Atomize;
        var wipe = DisruptionCards.Rout;
        return (response.Counter is { UnlessPays: null, LosesGame: false } counter && counter.Reaches(removal.Types, removal.Colors))
            || response.Grants.Any(g => (g.Answers(removal) || g.Answers(wipe)) && victims.Any(g.Covers));
    }

    /// <summary>The action costs no mana, or the untapped mana left after it
    /// is at least the reserve.</summary>
    private static bool LeavesReserve(PilotView view, EngineAction action, int reserve, int available)
    {
        var spent = action.Cost.Generic + action.Cost.Pips.Count
            + (action.SpendsSource && view.Mana.Sources.Any(s => s.Id == action.PermanentId) ? 1 : 0);
        if (reserve == 0 || spent == 0)
        {
            return true;
        }

        var added = action.ManaAdded.Count == 0 ? 0 : action.ManaAdded.Max(o => ManaPool.Parse(o).Total);
        return available - spent + added >= reserve;
    }

    /// <summary>The mana the pool holds plus the most each untapped source adds.</summary>
    private static int Available(PilotView view)
        => view.Mana.Pool.Total
        + view.Mana.Sources.Sum(s => s.Options.Select(o => ManaPool.Parse(o).Total).DefaultIfEmpty(0).Max());
}
