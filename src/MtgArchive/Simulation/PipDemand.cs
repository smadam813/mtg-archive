namespace MtgArchive.Simulation;

/// <summary>An "either X/Y" hybrid demand line — its own row shape, never fractional counts.</summary>
public sealed record EitherPips(IReadOnlyList<string> Colors, int Count);

/// <summary>
/// One spell's pip demand: per-color colored-pip counts, hybrid lines, and
/// the demand turn (mana value exactly — the cast-on-curve assumption).
/// <see cref="Notes"/> carries what was counted as zero: Phyrexian pips
/// and generic-or-color ({2/W}) pips.
/// </summary>
public sealed record PipDemandRow(
    Guid OracleId,
    string Name,
    int Quantity,
    bool IsCommander,
    int DemandTurn,
    IReadOnlyDictionary<string, int> Pips,
    IReadOnlyList<EitherPips> Either,
    IReadOnlyList<string> Notes);

/// <summary>
/// Pip demand extraction from the front-face mana cost of every
/// nonland deck card, commander included and flagged. An MDFC land counts
/// as a land, so it has no row. X and generic symbols
/// add nothing beyond printed colored pips.
/// </summary>
public static class PipDemand
{
    public static IReadOnlyList<PipDemandRow> Extract(IReadOnlyList<DeckCard> deck)
        => [.. deck.Where(c => !c.HasLandFace).Select(Row)];

    public static PipDemandRow Row(DeckCard card)
    {
        var pips = new Dictionary<string, int>();
        var either = new Dictionary<string, int>();
        var notes = new List<string>();
        foreach (var symbol in ManaSymbols.Parse(card.ManaCost))
        {
            switch (symbol.Kind)
            {
                case ManaSymbolKind.Phyrexian:
                    notes.Add($"Phyrexian pip {{{symbol.Text}}} counted as 0.");
                    break;

                case ManaSymbolKind.Hybrid:
                    either[symbol.Text] = either.GetValueOrDefault(symbol.Text) + 1;
                    break;

                case ManaSymbolKind.GenericHybrid:
                    notes.Add($"Generic-or-color pip {{{symbol.Text}}} counted as 0.");
                    break;

                case ManaSymbolKind.Color:
                    pips[symbol.Text] = pips.GetValueOrDefault(symbol.Text) + 1;
                    break;
            }
        }

        return new PipDemandRow(
            card.OracleId,
            card.Name,
            card.Quantity,
            card.IsCommander,
            (int)card.ManaValue,
            pips,
            [.. either.Select(e => new EitherPips(e.Key.Split('/'), e.Value))],
            [.. notes.Distinct()]);
    }
}
