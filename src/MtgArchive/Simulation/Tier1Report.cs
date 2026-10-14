using System.ComponentModel;
using MtgArchive.Api;

namespace MtgArchive.Simulation;

/// <summary>A caller-defined category: a name, its resolved card set, and
/// the k for its draw rows. The service never invents one.</summary>
public sealed record ReportCategory(string Name, IReadOnlySet<Guid> Cards, int K);

/// <summary>
/// The tier-1 report: exact closed-form results for one deck —
/// input echo, the assumptions block, and the three result tables
/// (category draws, mana sources, demand vs supply) plus the land-drop line
/// and the mulligan looks. A pure single-deck function with none of tier
/// 2's fields; the assumptions block stands where tier 2's floor label
/// stands. The format's game rules fix the cards seen by each turn, the
/// draw rule line, and the mulligan looks (ADR 0012).
/// </summary>
public sealed record Tier1Report(
    ReportInput Input,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<CategoryDrawRow> CategoryDraws,
    ManaSourceReport ManaSources,
    IReadOnlyList<DemandSupplyRow> DemandVsSupply,
    int LandCount,
    IReadOnlyList<LandDropRow> LandDrops,
    [property: Description("L: the most seven-card hands the mulligan can look at, from the format's free mulligans. 4 with one free mulligan, 3 with none.")]
    int MulliganLooks)
{
    /// <summary>The versions echo. The endpoint sets the format; tier 1
    /// consumes no other versioned input.</summary>
    public Api.VersionsBlock Versions { get; init; } = new();

    [System.ComponentModel.Description("The A- names that resolved only without the prefix, each with the card it resolved to; absent when there is none.")]
    public IReadOnlyList<NameRewrite>? NameRewrites { get; init; }

    /// <summary>The assumptions line of the draw rule, from the format's game rules.</summary>
    public static string DrawRuleLine(GameRules rules) => rules.StartingPlayerDraws
        ? "Turn-1 draw: cards seen by turn T are 7 + T (CR 103.8c, multiplayer)."
        : "On the play: no turn-1 draw, so cards seen by turn T are 7 + max(0, T − 1) (CR 103.8a).";

    /// <summary>The assumptions every format shares. The report puts the
    /// draw rule line before them.</summary>
    public static readonly IReadOnlyList<string> AssumptionLines =
    [
        "No mulligans: the London mulligan breaks the closed form.",
        "Cast at mana value: each spell is demanded on the turn equal to its mana value.",
        "Land drops assumed: casting probabilities are unconditional on hitting land drops.",
        "ETB-tapped ignored: a land is available on its drop turn.",
        "Pathways count for both colors: the player picks the face as the land is played.",
        "Phyrexian pips count zero.",
        "Opponent-dependent sources excluded: tier 1 has no opponents.",
        "Token for another player excluded: the card creates its mana token for another player.",
    ];

    public static Tier1Report Build(
        ResolvedDecklist decklist,
        IReadOnlyList<DeckCard> deck,
        IReadOnlyList<ReportCategory> categories,
        int maxTurn,
        GameRules? gameRules = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTurn, 1);
        var rules = gameRules ?? GameRules.Commander;
        var table = ManaSourceTable.Build(deck);
        var turns = Enumerable.Range(0, maxTurn + 1);

        var categoryDraws = categories
            .SelectMany(c => turns.Select(turn => new CategoryDrawRow(
                c.Name, c.K, turn, CategoryDraw.AtLeastByTurn(decklist, c.Cards, c.K, turn, rules))))
            .ToList();

        var byColor = ManaSourceTable.Colors
            .Where(table.InIdentity)
            .Select(color =>
            {
                var producing = table.Sources.Where(s => s.Colors.Contains(color)).ToList();
                int Of(ColorSpread spread) => producing.Where(s => s.Spread == spread).Sum(s => s.Quantity);
                return new ColorSupply(
                    color, producing.Sum(s => s.Quantity), Of(ColorSpread.Mono), Of(ColorSpread.Dual), Of(ColorSpread.ThreePlus));
            })
            .ToList();

        DemandSupplyRow Cell(PipDemandRow row, IReadOnlyList<string> colors, bool either, int pips)
        {
            var available = table.Available(colors, row.DemandTurn);
            var sources = available.Select(s => s.OracleId).ToHashSet();
            return new DemandSupplyRow(
                row.OracleId, row.Name, row.Quantity, row.IsCommander,
                colors, either, pips, row.DemandTurn,
                available.Sum(s => s.Quantity),
                CategoryDraw.AtLeastByTurn(decklist, sources, pips, row.DemandTurn, rules),
                row.Notes);
        }

        // A spell whose only colored symbols counted zero (Phyrexian,
        // generic-or-color) keeps a row so its notes reach the reader.
        var demandVsSupply = PipDemand.Extract(deck)
            .Where(row => row.DemandTurn <= maxTurn)
            .SelectMany(row => row.Pips
                .Select(p => Cell(row, [p.Key], either: false, p.Value))
                .Concat(row.Either.Select(e => Cell(row, e.Colors, either: true, e.Count)))
                .DefaultIfEmpty(row.Notes.Count > 0 ? Cell(row, [], either: false, pips: 0) : null)
                .OfType<DemandSupplyRow>())
            .OrderBy(r => r.Colors.Count == 0 ? int.MaxValue : Array.IndexOf(ManaSourceTable.Colors, r.Colors[0]))
            .ThenBy(r => r.Either)
            .ThenBy(r => r.Turn)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();

        var landCount = deck.Where(c => c.HasLandFace && !c.IsCommander).Sum(c => c.Quantity);
        var landDrops = turns.Skip(1)
            .Select(turn => new LandDropRow(
                turn, Hypergeometric.TailAtLeast(turn, landCount, decklist.DeckSize, CardsSeen.ByTurn(turn, rules))))
            .ToList();

        var input = new ReportInput(
            decklist.DeckSize,
            maxTurn,
            [.. deck.Where(c => c.IsCommander).Select(c => new CardEcho(c.OracleId, c.Name, 1))],
            [.. deck.Where(c => !c.IsCommander).Select(c => new CardEcho(c.OracleId, c.Name, c.Quantity))],
            [.. categories.Select(c => new CategoryEcho(c.Name, c.K, [.. c.Cards.Order()], c.Cards.Sum(decklist.QuantityOf)))]);

        return new Tier1Report(
            input,
            [DrawRuleLine(rules), .. AssumptionLines],
            categoryDraws,
            new ManaSourceReport(table.Sources, byColor, table.Excluded),
            demandVsSupply,
            landCount,
            landDrops,
            rules.MulliganLooks);
    }
}

/// <summary>The input echo: deck size, the turn horizon, and every card and
/// category as resolved.</summary>
public sealed record ReportInput(
    int DeckSize,
    int MaxTurn,
    IReadOnlyList<CardEcho> Commanders,
    IReadOnlyList<CardEcho> Cards,
    IReadOnlyList<CategoryEcho> Categories);

public sealed record CardEcho(Guid OracleId, string Name, int Quantity);

/// <summary><see cref="Count"/> is the category's card count in the deck —
/// the hypergeometric success count its rows use.</summary>
public sealed record CategoryEcho(string Name, int K, IReadOnlyList<Guid> Cards, int Count);

/// <summary>P(≥ k of the category among the cards seen by the turn), keyed by (category, k, turn).</summary>
public sealed record CategoryDrawRow(string Category, int K, int Turn, double Probability);

/// <summary>The per-color source table: every source with its color spread
/// and availability turn, and per-color counts split by spread. The counts
/// cover the deck's color identity and colorless only.
/// <see cref="Excluded"/> lists the sources the table leaves out.</summary>
public sealed record ManaSourceReport(
    IReadOnlyList<ManaSource> Sources,
    IReadOnlyList<ColorSupply> ByColor,
    IReadOnlyList<ExcludedSource> Excluded);

public sealed record ColorSupply(string Color, int Count, int Mono, int Dual, int ThreePlus);

/// <summary>
/// One demand cell joined to its supply. A spell demands <see cref="Pips"/>
/// of <see cref="Colors"/> on its demand turn: one color, or an "either X/Y"
/// hybrid line when <see cref="Either"/>. <see cref="Supply"/> counts the
/// sources available by that turn. The casting probability is P(≥ pips of
/// those sources among the cards seen by the turn), unconditional on land
/// drops. Spells past <c>max_turn</c> are omitted, so every row has a
/// land-drop line at its turn. A spell with only zero-counted pips keeps a
/// row with no colors, so its notes are reported.
/// </summary>
public sealed record DemandSupplyRow(
    Guid OracleId,
    string Name,
    int Quantity,
    bool IsCommander,
    IReadOnlyList<string> Colors,
    bool Either,
    int Pips,
    int Turn,
    int Supply,
    double CastingProbability,
    IReadOnlyList<string> Notes);

/// <summary>P(≥ turn lands among the cards seen by the turn). Casting
/// probabilities assume land drops; this line reports that assumption's odds.</summary>
public sealed record LandDropRow(int Turn, double Probability);
