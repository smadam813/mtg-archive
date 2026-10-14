namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The goal-aware mulligan's mana check: whether a hand can pay for a
/// tutor and then the goal piece it finds by the deadline, the last turn of
/// the leaf that wants the piece. The check reads the hand only. Draws
/// after the keep do not count.
/// </summary>
/// <remarks>
/// <para>The check plays one or two turns on a scratch mana board that
/// holds the hand and no other card. Turn t has min(t, lands in hand) lands
/// in play, any of them: one land drop per turn. Two plans count, each on
/// the latest turns, which have the most lands:</para>
/// <list type="bullet">
/// <item>Same turn: the tutor, then the goal piece, on the deadline turn.
/// A tutor to the top of the library cannot use this plan.</item>
/// <item>Split: the tutor on the turn before the deadline, the goal piece
/// on the deadline turn.</item>
/// </list>
/// <para>A tutor to the battlefield pays its own cost only. A goal piece
/// that is a land costs no mana, but it takes the land drop of its turn.</para>
/// <para>Free mana adds to the lands. It comes from a ritual (a spell that
/// adds mana), a mana rock the hand can cast, and a card whose mana ability
/// works from hand. Before the costs of a turn, the check casts the payable
/// ritual or rock that adds the most net mana. It repeats this zero or more
/// times and tries each count. The check spends a ritual or a card exiled
/// for mana once, and a rock stays.</para>
/// <para>After the split plan pays for the tutor, the check casts rocks with
/// the mana left. The rocks stay for the deadline turn.</para>
/// <para>Turns before the plan's turns do not count. This is the simpler
/// rule for deadlines after turn 1. On turn 1 the rule is one land plus the
/// free mana in hand.</para>
/// <para>A fetch land adds any color of the library lands it can find (the
/// view's finds). It adds no mana when the land it finds enters tapped.</para>
/// <para>The scratch board's life total is the view's life. At the
/// mulligan that is the format's start life, a fresh game's (ADR 0012).</para>
/// <para>A Saga chapter tutor never pays: its search waits for lore
/// counters, which the plans do not model. A Saga land taps for its own
/// produced mana.</para>
/// </remarks>
public static class HandPayability
{
    public static bool CanPay(PilotView view, IReadOnlyList<DeckCard> hand, DeckCard tutor, TutorFeature feature, DeckCard goalFace, int deadline)
    {
        if (feature.Via == TutorVia.SagaChapter)
        {
            return false;
        }

        var tutorCosts = TutorCosts(tutor, feature);
        var toBattlefield = feature.Destination == TutorDestination.Battlefield;
        var goalIsLand = !toBattlefield && goalFace.IsLand;
        IReadOnlyList<ManaCost> goalCosts = toBattlefield || goalIsLand ? [] : [ManaCost.Parse(goalFace.ManaCost)];
        var rest = hand.ToList();
        rest.Remove(tutor);
        var lands = rest.Where(c => c.HasLandFace).ToList();
        var others = rest.Where(c => !c.HasLandFace).ToList();
        var free = others.Count(c => FreeManaOf(view, c) is { Net: > 0 });

        var plans = new List<Plan>();
        if (feature.Destination != TutorDestination.LibraryTop)
        {
            var landsInPlay = Math.Min(goalIsLand ? deadline - 1 : deadline, lands.Count);
            plans.Add(new Plan(landsInPlay, [.. tutorCosts, .. goalCosts], NextTurnCosts: null, NextTurnDrop: false));
        }

        if (!toBattlefield && deadline >= 2)
        {
            plans.Add(new Plan(Math.Min(deadline - 1, lands.Count), tutorCosts, goalCosts, NextTurnDrop: !goalIsLand && lands.Count >= deadline));
        }

        foreach (var plan in plans)
        {
            foreach (var inPlay in Choose(lands, plan.LandsInPlay))
            {
                var left = lands.ToList();
                inPlay.ForEach(l => left.Remove(l));
                DeckCard?[] drops = plan.NextTurnDrop ? [.. left.DistinctBy(l => l.Name)] : [null];
                foreach (var drop in drops)
                {
                    for (var castsFirst = 0; castsFirst <= free; castsFirst++)
                    {
                        for (var castsNext = 0; castsNext <= (plan.NextTurnCosts is null ? 0 : free - castsFirst); castsNext++)
                        {
                            if (Plays(view, others, plan, inPlay, drop, castsFirst, castsNext))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>A plan: the lands in play on its first turn, the costs of that
    /// turn, and the costs of the next turn, if any. The next turn has one
    /// more land when <see cref="NextTurnDrop"/> is true.</summary>
    private sealed record Plan(int LandsInPlay, IReadOnlyList<ManaCost> Costs, IReadOnlyList<ManaCost>? NextTurnCosts, bool NextTurnDrop);

    /// <summary>The costs that put a tutor's search on the stack: a cast, an
    /// activation from hand, or a cast and then an activation. A land
    /// tutor is not cast.</summary>
    private static IReadOnlyList<ManaCost> TutorCosts(DeckCard tutor, TutorFeature feature)
    {
        var activation = ManaCost.Parse(feature.ActivationCost);
        return feature.Via == TutorVia.ActivateFromHand || (feature.Via == TutorVia.ActivateOnBattlefield && tutor.IsLand) ? [activation]
            : feature.Via == TutorVia.ActivateOnBattlefield ? [ManaCost.Parse(tutor.ManaCost), activation]
            : [ManaCost.Parse(tutor.ManaCost)];
    }

    /// <summary>Plays the plan with the <paramref name="inPlay"/> lands, then
    /// <paramref name="drop"/> on the next turn. Before the costs of each
    /// turn, it casts <paramref name="castsFirst"/> and then
    /// <paramref name="castsNext"/> free-mana cards.</summary>
    private static bool Plays(PilotView view, IReadOnlyList<DeckCard> others, Plan plan, IReadOnlyList<DeckCard> inPlay, DeckCard? drop, int castsFirst, int castsNext)
    {
        var scratch = new Scratch(view, others);
        inPlay.ToList().ForEach(land => scratch.Add(LandMana(view, land)));
        if (!Pays(scratch, plan.Costs, castsFirst))
        {
            return false;
        }

        if (plan.NextTurnCosts is null)
        {
            return true;
        }

        while (CastFreeMana(scratch, rocksOnly: true))
        {
        }

        scratch.NextTurn();
        if (drop is not null)
        {
            scratch.Add(LandMana(view, drop));
        }

        return Pays(scratch, plan.NextTurnCosts, castsNext);
    }

    private static bool Pays(Scratch scratch, IEnumerable<ManaCost> costs, int freeMana)
    {
        for (var i = 0; i < freeMana; i++)
        {
            if (!CastFreeMana(scratch, rocksOnly: false))
            {
                return false;
            }
        }

        foreach (var cost in costs)
        {
            if (GreedyPayment.Solve(scratch.Board(), cost) is not { } payment)
            {
                return false;
            }

            scratch.Spend(payment);
        }

        return true;
    }

    /// <summary>A card in hand that adds mana once cast: a ritual adds
    /// <see cref="Ritual"/> to the pool, a rock taps for <see cref="Added"/>.</summary>
    private sealed record FreeMana(DeckCard Card, ManaCost Cost, int Added, string? Ritual, IReadOnlyList<string> Taps)
    {
        public bool IsRock => Ritual is null;

        public int Net => Added - Cost.Generic - Cost.Pips.Count;
    }

    /// <summary>
    /// Casts the payable free-mana card in hand with the highest net mana.
    /// With <paramref name="rocksOnly"/>, it casts a rock the board can pay
    /// for, whatever its net: the rock stays for a later turn. Otherwise it
    /// casts a ritual or a rock that adds more than it costs.
    /// </summary>
    private static bool CastFreeMana(Scratch scratch, bool rocksOnly)
    {
        foreach (var free in scratch.Hand.Select(c => FreeManaOf(scratch.View, c)).OfType<FreeMana>()
                     .Where(f => rocksOnly ? f.IsRock : f.Net > 0)
                     .OrderByDescending(f => f.Net))
        {
            var board = scratch.Board();
            if (rocksOnly && !GreedyPayment.CanPay(board, free.Cost, free.Card, useHand: false)
                || GreedyPayment.Solve(board, free.Cost, free.Card) is not { } payment)
            {
                continue;
            }

            scratch.Spend(payment);
            scratch.Hand.Remove(free.Card);
            if (free.IsRock)
            {
                scratch.Add(free.Taps);
            }
            else
            {
                scratch.Pool.Add(free.Ritual!);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// The card as free mana: a ritual's spell mana, or a rock's tap mana,
    /// with the most mana that one tap adds. Null for any other card.
    /// </summary>
    private static FreeMana? FreeManaOf(PilotView view, DeckCard card)
    {
        var features = view.Features(card);
        var cost = ManaCost.Parse(card.ManaCost);
        if (!card.IsPermanent)
        {
            var ritual = features.SpellMana ?? string.Empty;
            return ritual.Length > 0 ? new FreeMana(card, cost, ManaPool.Parse(ritual).Total, ritual, []) : null;
        }

        var taps = features.TapMana;
        return taps.Count > 0 ? new FreeMana(card, cost, taps.Max(t => ManaPool.Parse(t).Total), Ritual: null, taps) : null;
    }

    /// <summary>The mana one tap of a land from hand adds, on its land face.
    /// A fetch land adds any color of the library lands it can find. It adds
    /// no mana when the land it finds enters tapped.</summary>
    private static IReadOnlyList<string> LandMana(PilotView view, DeckCard card)
    {
        var face = card.IsLand ? card : card.AsBackFace()!;
        var features = view.Features(face);
        if (features.Tutor is not { Destination: TutorDestination.Battlefield, Via: not TutorVia.SagaChapter } fetch)
        {
            return features.TapMana;
        }

        return fetch.EntersTapped
            ? []
            : [.. (view.Finds.GetValueOrDefault(face.Name) ?? [])
                .Where(c => c.IsLand)
                .SelectMany(c => c.ProducedColors ?? [])
                .Distinct(StringComparer.Ordinal)
                .Select(c => $"{{{c}}}")];
    }

    /// <summary>Every choice of <paramref name="count"/> lands, once per
    /// distinct set of names.</summary>
    private static IEnumerable<List<DeckCard>> Choose(IReadOnlyList<DeckCard> lands, int count)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return Subsets(lands, 0, count).Where(s => seen.Add(string.Join('\n', s.Select(l => l.Name).Order(StringComparer.Ordinal))));
    }

    private static IEnumerable<List<DeckCard>> Subsets(IReadOnlyList<DeckCard> items, int start, int count)
    {
        if (count == 0)
        {
            yield return [];
            yield break;
        }

        for (var i = start; i <= items.Count - count; i++)
        {
            foreach (var tail in Subsets(items, i + 1, count - 1))
            {
                yield return [items[i], .. tail];
            }
        }
    }

    /// <summary>
    /// The scratch game of one plan: the hand cards, the mana sources in
    /// play order with their tapped state, and the pool. It spends a payment
    /// the way the engine spends one: each tap and each exiled card
    /// adds its mana, then the spend leaves the pool. It pays no life.
    /// </summary>
    private sealed class Scratch(PilotView view, IEnumerable<DeckCard> hand)
    {
        private const string Prefix = "s:";

        private readonly List<(IReadOnlyList<string> Options, bool Tapped)> _sources = [];

        public PilotView View { get; } = view;

        public List<DeckCard> Hand { get; } = [.. hand];

        public ManaPool Pool { get; } = new();

        /// <summary>A permanent enters untapped. One with no mana still
        /// takes its place in play order.</summary>
        public void Add(IReadOnlyList<string> options) => _sources.Add((options, false));

        /// <summary>The next turn: the pool empties and every source untaps.</summary>
        public void NextTurn()
        {
            Pool.Clear();
            for (var i = 0; i < _sources.Count; i++)
            {
                _sources[i] = (_sources[i].Options, false);
            }
        }

        public ManaBoard Board()
            => new(
                new ManaPool(Pool),
                View.Life,
                [.. _sources.Select((s, index) => (s, index))
                    .Where(x => !x.s.Tapped && x.s.Options.Count > 0)
                    .Select(x => new BoardManaSource(Prefix + x.index, x.s.Options))],
                [.. Hand.Select(c => (Card: c, View.Features(c).HandMana)).Where(h => h.HandMana.Count > 0).Select(h => new HandManaOption(h.Card, h.HandMana))]);

        public void Spend(BoardPayment payment)
        {
            foreach (var tap in payment.Taps)
            {
                var index = int.Parse(tap.Source.Id.AsSpan(Prefix.Length), System.Globalization.CultureInfo.InvariantCulture);
                _sources[index] = (_sources[index].Options, true);
                Pool.Add(tap.Produces);
            }

            foreach (var use in payment.FromHand)
            {
                Hand.Remove(use.Source.Card);
                Pool.Add(use.Produces);
            }

            Pool.Remove(payment.Spend);
        }
    }
}
