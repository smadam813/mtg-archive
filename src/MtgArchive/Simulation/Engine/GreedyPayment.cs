namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The greedy mana payment over a <see cref="ManaBoard"/>. The pilot
/// predicts the engine's payment with it (the colors an action leaves).
/// A card in hand pays only when the board and life cannot pay, because
/// the activation exiles the card.
/// </summary>
public static class GreedyPayment
{
    /// <summary>Whether any payment exists. <paramref name="casting"/> is
    /// the card the cost is for, when it is cast from hand. With
    /// <paramref name="useHand"/> false, only the board, the pool, and life
    /// count.</summary>
    public static bool CanPay(ManaBoard board, ManaCost cost, DeckCard? casting = null, bool useHand = true)
        => (useHand ? Solve(board, cost, casting) : SolveFrom(board, cost, [])) is not null;

    /// <summary>
    /// Pips take the pool first. Then a maximum bipartite matching assigns
    /// source units (a Sol Ring is two colorless units), mono-color units
    /// first. An unmatched Phyrexian pip costs 2 life while the player stays
    /// alive; an unmatched generic-or-color pip adds to generic. Generic
    /// takes leftover units of tapped sources, then the pool colorless
    /// first, then untapped sources. Mana a tap produces beyond the spend
    /// stays in the pool. Only when that fails does the solver add the hand
    /// sources, after every permanent. <paramref name="casting"/> cannot pay
    /// for itself. Null when the cost is unpayable.
    /// </summary>
    public static BoardPayment? Solve(ManaBoard board, ManaCost cost, DeckCard? casting = null)
        => SolveFrom(board, cost, [])
        ?? (HandSourcesFor(board, casting) is { Count: > 0 } hand ? SolveFrom(board, cost, hand) : null);

    /// <summary>The hand options without the first one of the card on its
    /// way to the stack.</summary>
    private static List<HandManaOption> HandSourcesFor(ManaBoard board, DeckCard? casting)
    {
        var hand = board.HandSources.ToList();
        if (casting is not null && hand.FindIndex(h => h.Card == casting.Whole()) is var index and >= 0)
        {
            hand.RemoveAt(index);
        }

        return hand;
    }

    private static BoardPayment? SolveFrom(ManaBoard board, ManaCost cost, IReadOnlyList<HandManaOption> hand)
    {
        var pool = new ManaPool(board.Pool);
        var spend = new ManaPool();
        var life = 0;
        var generic = cost.Generic;

        var unpaid = new List<ManaPip>();
        foreach (var pip in cost.Pips)
        {
            var fromPool = pip.Options.FirstOrDefault(o => pool[o] > 0);
            if (fromPool is not null)
            {
                pool.RemoveSymbol(fromPool);
                spend.AddSymbol(fromPool);
            }
            else
            {
                unpaid.Add(pip);
            }
        }

        var units = board.Sources.Select(s => new Supply(s, null, s.Options))
            .Concat(hand.Select(h => new Supply(null, h, h.Options)))
            .SelectMany(Units)
            .OrderBy(u => u.Source.InHand ? 1 : 0)
            .ThenBy(u => u.Source.IsMono ? 0 : 1)
            .ThenBy(u => u.Options.Count)
            .ToList();
        var matched = Match(unpaid, units);

        var taps = new Dictionary<Supply, string>();
        var usedUnits = new HashSet<Unit>();
        for (var i = 0; i < unpaid.Count; i++)
        {
            var pip = unpaid[i];
            if (matched[i] is { } unit)
            {
                usedUnits.Add(unit);
                var symbol = unit.Options.First(pip.Options.Contains);
                taps[unit.Source] = unit.ProducesFor(symbol);
                spend.AddSymbol(symbol);
            }
            else if (pip.Phyrexian && board.Life > life + 2)
            {
                life += 2;
            }
            else if (pip.GenericAlternative is { } alternative)
            {
                generic += alternative;
            }
            else
            {
                return null;
            }
        }

        // Leftover units of sources already tapped, so nothing floats needlessly.
        foreach (var unit in units.Where(u => !usedUnits.Contains(u) && taps.ContainsKey(u.Source)
                     && u.Produces == taps[u.Source]))
        {
            if (generic == 0)
            {
                break;
            }

            usedUnits.Add(unit);
            spend.AddSymbol(unit.Options[0]);
            generic--;
        }

        foreach (var symbol in ManaPool.Symbols.OrderBy(s => s == "C" ? 0 : 1))
        {
            while (generic > 0 && pool[symbol] > 0)
            {
                pool.RemoveSymbol(symbol);
                spend.AddSymbol(symbol);
                generic--;
            }
        }

        foreach (var source in units.Select(u => u.Source).Distinct().Where(s => !taps.ContainsKey(s)))
        {
            if (generic == 0)
            {
                break;
            }

            var produces = source.Options[0];
            taps[source] = produces;
            foreach (var symbol in ManaSymbols.Parse(produces))
            {
                if (generic == 0)
                {
                    break;
                }

                spend.AddSymbol(symbol.Colors[0]);
                generic--;
            }
        }

        if (generic > 0)
        {
            return null;
        }

        return new BoardPayment(
            [.. taps.Where(t => t.Key.Board is not null).Select(t => new BoardTap(t.Key.Board!, t.Value))],
            [.. taps.Where(t => t.Key.Hand is not null).Select(t => new HandTap(t.Key.Hand!, t.Value))],
            spend,
            life);
    }

    /// <summary>One source the solver may draw on: a permanent to tap, or a
    /// card in hand to exile. A class, so two copies of a card in hand stay
    /// two sources.</summary>
    private sealed class Supply(BoardManaSource? board, HandManaOption? hand, IReadOnlyList<string> options)
    {
        public BoardManaSource? Board { get; } = board;

        public HandManaOption? Hand { get; } = hand;

        public IReadOnlyList<string> Options { get; } = options;

        public bool InHand => Hand is not null;

        public bool IsMono => Options.Count == 1 && ManaPool.Parse(Options[0]).Total == 1;
    }

    /// <summary>One mana of one tap. A dual land is one unit with two
    /// options, and produces whichever one the pip takes; a Sol Ring is two
    /// units, each colorless, under one <see cref="Produces"/> string.</summary>
    private sealed record Unit(Supply Source, string Produces, IReadOnlyList<string> Options)
    {
        public string ProducesFor(string symbol) => Options.Count > 1 ? $"{{{symbol}}}" : Produces;
    }

    private static IEnumerable<Unit> Units(Supply source)
    {
        var strings = source.Options.Select(o => (Text: o, Pool: ManaPool.Parse(o))).ToList();
        if (strings.All(s => s.Pool.Total == 1))
        {
            // Every option is one mana: one unit whose options are the colors.
            yield return new Unit(source, source.Options[0], strings.Select(s => ManaPool.Symbols.First(c => s.Pool[c] == 1)).ToList());
            yield break;
        }

        // A multi-mana option: take the first as the yield, one unit per symbol.
        var (text, pool) = strings.OrderByDescending(s => s.Pool.Total).First();
        foreach (var symbol in ManaPool.Symbols)
        {
            for (var i = 0; i < pool[symbol]; i++)
            {
                yield return new Unit(source, text, [symbol]);
            }
        }
    }

    /// <summary>Augmenting-path matching of pips to units.</summary>
    private static Unit?[] Match(IReadOnlyList<ManaPip> pips, List<Unit> units)
    {
        var unitOfPip = new Unit?[pips.Count];
        var pipOfUnit = new Dictionary<Unit, int>();

        bool Assign(int pip, HashSet<Unit> visited)
        {
            foreach (var unit in units)
            {
                if (!unit.Options.Any(pips[pip].Options.Contains) || !visited.Add(unit))
                {
                    continue;
                }

                if (!pipOfUnit.TryGetValue(unit, out var holder) || Assign(holder, visited))
                {
                    pipOfUnit[unit] = pip;
                    unitOfPip[pip] = unit;
                    return true;
                }
            }

            return false;
        }

        for (var i = 0; i < pips.Count; i++)
        {
            Assign(i, []);
        }

        return unitOfPip;
    }
}
