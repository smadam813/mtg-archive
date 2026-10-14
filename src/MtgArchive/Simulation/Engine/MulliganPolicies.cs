namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The default keep rule: the land window. Keep a seven with 2–5 lands, a
/// six with 2–4, and any hand of five or fewer. The rule counts the lands
/// in the seven as dealt, and an MDFC land counts as a land. The bottom
/// puts lands above the window under first, then the most expensive spells.
/// </summary>
public sealed class LandWindowMulligan : IMulliganPolicy
{
    public const int MinLands = 2;

    public string Name => "land-window";

    /// <summary>The most lands the window allows in a hand of
    /// <paramref name="keepSize"/> cards: 5 at seven, 4 at six, then two
    /// fewer than the hand.</summary>
    public static int MaxLands(int keepSize) => keepSize switch
    {
        >= 7 => 5,
        6 => 4,
        _ => Math.Max(MinLands, keepSize - MinLands),
    };

    /// <summary>The lands in <paramref name="hand"/> are within the window
    /// for a keep of <paramref name="keepSize"/> cards.</summary>
    public static bool InWindow(IReadOnlyList<DeckCard> hand, int keepSize)
    {
        var lands = hand.Count(c => c.HasLandFace);
        return lands >= MinLands && lands <= MaxLands(keepSize);
    }

    /// <summary>The lands a bottom puts under first: those above the window
    /// of the hand that stays.</summary>
    public static IReadOnlyList<DeckCard> LandsAboveWindow(BottomChoice choice)
    {
        var lands = choice.Hand.Where(c => c.HasLandFace).ToList();
        return lands.Take(Math.Max(0, lands.Count - MaxLands(choice.Hand.Count - choice.Count))).ToList();
    }

    public bool KeepHand(PilotView view, MulliganChoice choice)
        => choice.KeepSize <= 5 || InWindow(choice.Hand, choice.KeepSize);

    public IReadOnlyList<DeckCard> ChooseBottom(PilotView view, BottomChoice choice)
    {
        var excess = LandsAboveWindow(choice);
        var lands = choice.Hand.Where(c => c.HasLandFace).Skip(excess.Count);
        var spells = choice.Hand.Where(c => !c.HasLandFace).OrderByDescending(c => c.ManaValue);
        return [.. excess.Concat(spells).Concat(lands).Take(choice.Count)];
    }
}

/// <summary>
/// The goal-aware keep rule: the second mulligan policy, selectable per
/// run and not the default. It keeps a seven or a six when the lands are
/// in the window and the hand holds a goal piece or a tutor for one. It
/// keeps any hand of five or fewer.
/// </summary>
/// <remarks>
/// <para>A tutor counts only when the hand can pay for it and then the goal
/// piece by the deadline of a leaf the tutor finds a card for. The hand
/// pays from its land drops and free mana alone (<see cref="HandPayability"/>).
/// The check plays the deadline turn, or the tutor on the turn before and
/// the goal piece on the deadline turn. Turn t has min(t, lands in hand)
/// lands, and earlier turns do not count. A six pays from the cards the
/// bottom leaves.</para>
/// <para>A goal piece in hand needs no mana check. A goal piece in the
/// command zone counts as held: a commander is never dealt, so a commander
/// goal keeps by the land window alone.</para>
/// <para>The bottom puts lands above the window under first, then the least
/// useful cards in the pilot's order.</para>
/// </remarks>
public sealed class GoalAwareMulligan(GoalPredicate goal) : IMulliganPolicy, IGoalDirected
{
    public const string PolicyName = "goal-aware";

    public GoalPredicate Goal { get; } = goal;

    public string Name => PolicyName;

    public bool KeepHand(PilotView view, MulliganChoice choice)
    {
        if (choice.KeepSize <= 5)
        {
            return true;
        }

        if (!LandWindowMulligan.InWindow(choice.Hand, choice.KeepSize))
        {
            return false;
        }

        if (GoalHeuristics.CommandZoneHoldsWanted(view, Goal) || choice.Hand.Any(c => Goal.Wants(view, c)))
        {
            return true;
        }

        var kept = Kept(view, choice);
        return kept.Any(c => GoalHeuristics.IsPayableTutorFor(view, Goal, c, kept));
    }

    public IReadOnlyList<DeckCard> ChooseBottom(PilotView view, BottomChoice choice)
    {
        var excess = LandWindowMulligan.LandsAboveWindow(choice);
        var rest = choice.Hand.ToList();
        foreach (var land in excess)
        {
            rest.Remove(land);
        }

        return [.. excess.Concat(GoalHeuristics.LeastUsefulFirst(view, Goal, rest)).Take(choice.Count)];
    }

    /// <summary>The cards a keep leaves in hand after this policy's bottom.</summary>
    private List<DeckCard> Kept(PilotView view, MulliganChoice choice)
    {
        var kept = choice.Hand.ToList();
        foreach (var card in ChooseBottom(view, new BottomChoice(choice.Hand, choice.Hand.Count - choice.KeepSize)))
        {
            kept.Remove(card);
        }

        return kept;
    }
}
