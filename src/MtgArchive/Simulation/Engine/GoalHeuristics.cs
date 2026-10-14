namespace MtgArchive.Simulation.Engine;

/// <summary>The pilot's priority tiers, best first.</summary>
public enum ActionTier
{
    Goal,
    Tutor,
    LandDrop,
    Ramp,
    Draw,
    Other,
}

/// <summary>
/// The goal-directed reads the pilot and the goal-aware mulligan share.
/// Every function here reads the pilot view and the goal only, so the
/// policies stay deterministic and engine-neutral. Card shape (land, faces,
/// mana value, mana cost, produced colors) comes from <see cref="DeckCard"/>;
/// what a card does comes from its card feature profile.
/// </summary>
public static class GoalHeuristics
{
    /// <summary>A card that searches the library for a wanted goal piece in
    /// time for its leaf. The hand must pay for the search and then the goal
    /// piece by that leaf's deadline (<see cref="HandPayability"/>).</summary>
    public static bool IsPayableTutorFor(PilotView view, GoalPredicate goal, DeckCard card, IReadOnlyList<DeckCard> hand)
        => view.Features(card).Tutor is { } tutor
        && Finds(view, goal, card, tutor)
            .DistinctBy(f => (f.Leaf, f.Card.Name))
            .Any(f => HandPayability.CanPay(view, hand, card, tutor, f.Leaf.WantedFace(view, f.Card)!, f.Leaf.LastTurn));

    /// <summary>The command zone holds a card the goal still wants. A
    /// commander is castable from there, so a keep rule counts it as held.</summary>
    public static bool CommandZoneHoldsWanted(PilotView view, GoalPredicate goal)
        => view.CommandZone.Any(c => goal.Wants(view, c));

    /// <summary>The land produces a color a wanted goal piece in hand or in
    /// the command zone needs and no permanent on the battlefield produces.</summary>
    public static bool ProducesNeededColor(PilotView view, GoalPredicate goal, DeckCard land)
    {
        var produced = land.ProducedColors ?? [];
        return NeededColors(view, goal).Any(c => produced.Contains(c, StringComparer.Ordinal));
    }

    /// <summary>The tier of a card the pilot could play or keep. A card
    /// whose back face the goal wants is a goal card.</summary>
    public static ActionTier Classify(PilotView view, GoalPredicate goal, DeckCard card)
        => Classify(view, goal, card, goal.Wants(view, card));

    /// <summary>A cast is a goal action only on the face the goal wants.
    /// The pilot never casts a card on a face the goal does not want while
    /// it wants the other face: that cast would spend the goal card. Nor
    /// does it cast a card the goal does not want whose mana ability works
    /// from hand: in hand the card is mana.</summary>
    private static ActionTier? ClassifyCast(PilotView view, GoalPredicate goal, DeckCard face)
        => goal.WantedFace(view, face) is not { } wanted
            ? (IsHandMana(view, face) ? null : Classify(view, goal, face, wanted: false))
            : wanted == face ? ActionTier.Goal
            : null;

    /// <summary>A card with a mana ability that works from hand.</summary>
    private static bool IsHandMana(PilotView view, DeckCard card) => view.Features(card.Whole()).HandMana.Count > 0;

    /// <summary>The action is payable only with mana from a card in hand:
    /// the board, the pool, and life cannot pay it.</summary>
    public static bool NeedsHandMana(PilotView view, EngineAction action)
        => !GreedyPayment.CanPay(view.Mana, action.Cost, useHand: false);

    /// <summary>
    /// A land drop follows the cast rule: a goal action only on the face the
    /// goal wants, and never on one face while the goal wants the other.
    /// Otherwise an MDFC land plays its land face only with no other land
    /// in hand. A pathway plays its back face only when that face makes a
    /// needed color and the front face does not.
    /// </summary>
    private static ActionTier? ClassifyLand(PilotView view, GoalPredicate goal, DeckCard face)
    {
        var card = face.Whole();
        if (goal.WantedFace(view, card) is { } wanted)
        {
            return wanted == face ? ActionTier.Goal : null;
        }

        if (card.IsMdfcLand && view.Hand.Any(c => c.IsLand))
        {
            return null;
        }

        if (card.IsPathway)
        {
            var playBack = ProducesNeededColor(view, goal, card.AsBackFace()!) && !ProducesNeededColor(view, goal, card);
            var isBack = face != card;
            if (playBack != isBack)
            {
                return null;
            }
        }

        return Classify(view, goal, face, wanted: false);
    }

    private static ActionTier Classify(PilotView view, GoalPredicate goal, DeckCard card, bool wanted)
    {
        if (wanted)
        {
            return ActionTier.Goal;
        }

        var features = view.Features(card);
        if (FindsWanted(view, goal, card, features.Tutor))
        {
            return ActionTier.Tutor;
        }

        if (card.IsLand)
        {
            return ActionTier.LandDrop;
        }

        if (features.Develops)
        {
            return ActionTier.Ramp;
        }

        return features.Draws ? ActionTier.Draw : ActionTier.Other;
    }

    /// <summary>
    /// The tier of a legal action, or null for one the pilot never takes on
    /// its own. Payment taps mana abilities. An ability with no cost would
    /// loop. An activation that neither tutors, ramps, nor draws does
    /// nothing for the goal.
    /// </summary>
    public static ActionTier? Tier(PilotView view, GoalPredicate goal, EngineAction action)
    {
        switch (action.Kind)
        {
            case EngineActionKind.PlayLand:
                return ClassifyLand(view, goal, action.Face);

            case EngineActionKind.Cast:
            case EngineActionKind.CastCommander:
                return ClassifyCast(view, goal, action.Face);

            case EngineActionKind.Activate:
                if (action.ManaAdded.Count > 0 || IsFree(action))
                {
                    return null;
                }

                if (FindsWanted(view, goal, action.Face, action.Features.Tutor))
                {
                    return ActionTier.Tutor;
                }

                if (action.Features.Develops)
                {
                    return ActionTier.Ramp;
                }

                if (!action.Features.Draws)
                {
                    return null;
                }

                // Phase 3E: a land that taps or sacrifices itself to draw
                // trades a land's mana for a card (Fiery Islet on XMage, the
                // Obeka turn-4 diagnosis). It waits until the goal is met,
                // or until the goal turn has passed. The view answers "met":
                // the goal holds on the pilot view, so no pilot state is needed.
                return action.Face.HasLandFace && action.SpendsSource && view.Turn <= goal.LastTurn && !goal.HoldsNow(view)
                    ? null
                    : ActionTier.Draw;

            default:
                return null;
        }
    }

    /// <summary>An activation with no cost at all: no mana, no tap, no sacrifice.</summary>
    private static bool IsFree(EngineAction action) => ManaValue(action.Cost) == 0 && !action.SpendsSource;

    /// <summary>The mana value an action spends, for the order within a
    /// tier: the printed mana value of a cast face, the mana cost of an
    /// activation, zero for a land drop.</summary>
    public static decimal ManaValue(EngineAction action) => action.Kind switch
    {
        EngineActionKind.Cast or EngineActionKind.CastCommander => action.Face.ManaValue,
        EngineActionKind.Activate => ManaValue(action.Cost),
        _ => 0,
    };

    private static decimal ManaValue(ManaCost cost) => cost.Generic + cost.Pips.Count;

    /// <summary>
    /// The action makes a needed color unavailable that is available now.
    /// A needed color is a pip of a wanted goal piece, on its wanted face,
    /// or a pip of a tutor for one. Only cards in hand or in the command
    /// zone after the action count. Available is mana in the pool, an
    /// untapped source the payment leaves, or mana the action adds. The
    /// greedy payment decides what the payment taps. The read is the
    /// current view only.
    /// </summary>
    public static bool LosesNeededColor(PilotView view, GoalPredicate goal, EngineAction action)
    {
        var needed = ColorsToKeep(view, goal, PlayedFace(action)?.Whole());
        if (needed.Count == 0)
        {
            return false;
        }

        var now = Colors(view.Mana.Pool, view.Mana.Sources.SelectMany(s => s.Options));
        var atRisk = needed.Where(now.Contains).ToList();
        if (atRisk.Count == 0)
        {
            return false;
        }

        var after = ColorsAfter(view, action);
        return atRisk.Any(c => !after.Contains(c));
    }

    /// <summary>The face a land drop or a cast puts into play.</summary>
    private static DeckCard? PlayedFace(EngineAction action) => action.Kind == EngineActionKind.Activate ? null : action.Face;

    private static HashSet<string> ColorsToKeep(PilotView view, GoalPredicate goal, DeckCard? acting)
    {
        var cards = view.Hand.Concat(view.CommandZone).ToList();
        if (acting is not null)
        {
            cards.Remove(acting);
        }

        return cards
            .Select(c => goal.WantedFace(view, c) ?? (FindsWanted(view, goal, c, view.Features(c).Tutor) ? c : null))
            .OfType<DeckCard>()
            .SelectMany(c => PipColors(ManaCost.Parse(c.ManaCost)))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The colors available once the action resolves: the pool
    /// after the payment, the sources the payment leaves untapped, and the
    /// mana the action adds.</summary>
    private static HashSet<string> ColorsAfter(PilotView view, EngineAction action)
    {
        var casting = action.Kind == EngineActionKind.Cast ? action.Face : null;
        var payment = GreedyPayment.Solve(view.Mana, action.Cost, casting);

        var pool = new ManaPool(view.Mana.Pool);
        var used = new HashSet<string>(StringComparer.Ordinal);
        if (payment is not null)
        {
            foreach (var produced in payment.Taps.Select(t => t.Produces).Concat(payment.FromHand.Select(h => h.Produces)))
            {
                pool.Add(produced);
            }

            pool.Remove(payment.Spend);
            used.UnionWith(payment.Taps.Select(t => t.Source.Id));
        }

        if (action.SpendsSource && action.PermanentId is { } spent)
        {
            used.Add(spent);
        }

        var left = view.Mana.Sources.Where(s => !used.Contains(s.Id)).SelectMany(s => s.Options);
        return Colors(pool, left.Concat(action.ManaAdded));
    }

    private static HashSet<string> Colors(ManaPool pool, IEnumerable<string> mana)
    {
        var all = new ManaPool(pool);
        foreach (var m in mana)
        {
            all.Add(m);
        }

        return ManaPool.Symbols.Where(s => all[s] > 0).ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> PipColors(ManaCost cost)
        => cost.Pips.SelectMany(p => p.Options).Select(o => o.Trim('{', '}'));

    /// <summary>
    /// The cards ordered least useful first: the order a discard, a
    /// put-back, or a bottom takes them in, and the reverse of trigger
    /// order. The order is the tiers from <see cref="ActionTier.Other"/>
    /// up to <see cref="ActionTier.Goal"/>. Within a tier the spell that
    /// costs more goes first. Ties keep the input order.
    /// </summary>
    public static IReadOnlyList<DeckCard> LeastUsefulFirst(PilotView view, GoalPredicate goal, IEnumerable<DeckCard> cards)
        => cards.Select((card, index) => (card, index))
            .OrderByDescending(c => Classify(view, goal, c.card))
            .ThenByDescending(c => c.card.IsLand ? 0 : c.card.ManaValue)
            .ThenBy(c => c.index)
            .Select(c => c.card)
            .ToList();

    /// <summary>A search of the card finds a wanted goal piece in the
    /// library in time for its leaf (<see cref="Finds"/>).</summary>
    private static bool FindsWanted(PilotView view, GoalPredicate goal, DeckCard card, TutorFeature? tutor)
        => tutor is not null && Finds(view, goal, card, tutor).Any();

    /// <summary>
    /// Each library card the search of <paramref name="card"/> finds (from
    /// <see cref="PilotView.Finds"/>) and each leaf that wants it in time.
    /// A search to hand or to the battlefield arrives on the turn it
    /// resolves. A search to the top of the library reaches hand at the next
    /// draw, one turn later. A Saga chapter resolves its
    /// <see cref="TutorFeature.DelayTurns"/> after the Saga enters. The
    /// search is late when the leaf's deadline falls from this turn to the
    /// turn before it arrives. After the deadline the goal piece still
    /// counts for the curve.
    /// </summary>
    private static IEnumerable<(DeckCard Card, GoalPredicate Leaf)> Finds(PilotView view, GoalPredicate goal, DeckCard card, TutorFeature tutor)
        => from found in view.Finds.GetValueOrDefault(card.Name) ?? []
           from leaf in goal.LeavesWanting(view, found)
           let arrives = view.Turn + tutor.DelayTurns + (tutor.Destination == TutorDestination.LibraryTop ? 1 : 0)
           where leaf.LastTurn < view.Turn || leaf.LastTurn >= arrives
           select (found, leaf);

    private static IEnumerable<string> NeededColors(PilotView view, GoalPredicate goal)
    {
        var onBoard = view.Battlefield
            .SelectMany(p => p.Card.ProducedColors ?? [])
            .ToHashSet(StringComparer.Ordinal);
        return view.Hand.Concat(view.CommandZone)
            .Select(c => goal.WantedFace(view, c))
            .OfType<DeckCard>()
            .SelectMany(c => PipColors(ManaCost.Parse(c.ManaCost)))
            .Where(c => !onBoard.Contains(c))
            .Distinct(StringComparer.Ordinal);
    }
}
