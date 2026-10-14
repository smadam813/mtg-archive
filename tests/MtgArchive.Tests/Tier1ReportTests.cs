using System.Text.Json;
using MtgArchive.Api;
using MtgArchive.Simulation;

namespace MtgArchive.Tests;

/// <summary>
/// The tier-1 report as a pure single-deck function: per-color
/// casting probabilities over the turn-available source set, the land-drop
/// line, the demand-vs-supply join, and the report envelope — input echo,
/// the fixed assumptions block, three result tables, and none of tier 2's
/// floor / CI / seed / legality fields.
/// </summary>
public sealed class Tier1ReportTests
{
    private static readonly string[] AssumptionLines =
    [
        "turn-1 draw",
        "no mulligans",
        "cast at mana value",
        "land drops assumed",
        "ETB-tapped ignored",
        "Pathways count for both colors",
        "Phyrexian pips count zero",
        "Opponent-dependent sources excluded",
        "Token for another player excluded",
    ];

    private static DeckCard Land(string name, int quantity, params string[] produced) => new()
    {
        OracleId = Guid.NewGuid(),
        Name = name,
        Quantity = quantity,
        IsLand = true,
        ProducedColors = produced,
    };

    private static DeckCard Spell(string name, string manaCost, int manaValue, int quantity = 1) => new()
    {
        OracleId = Guid.NewGuid(),
        Name = name,
        Quantity = quantity,
        ManaCost = manaCost,
        ManaValue = manaValue,
    };

    private static DeckCard Rock(string name, int manaValue, params string[] produced)
        => Spell(name, $"{{{manaValue}}}", manaValue) with { ProducedColors = produced };

    private static ResolvedDecklist DecklistOf(IReadOnlyList<DeckCard> deck) => new()
    {
        Commanders = [.. deck.Where(c => c.IsCommander).Select(c => c.OracleId)],
        Entries = [.. deck.Where(c => !c.IsCommander).Select(c => new ResolvedEntry(c.OracleId, c.Quantity))],
    };

    // An opponent-dependent source changes no count, supply or
    // casting probability; the report lists it as excluded.
    [Fact]
    public void Opponent_dependent_source_counts_as_no_source()
    {
        var plains = Land("Plains", 20, "W");
        var bear = Spell("Bear", "{1}{W}", 2);
        var tithe = Rock("Smothering Tithe", 4, "W", "U", "B", "R", "G") with { OpponentDependent = true };
        List<DeckCard> flagged = [plains, bear, tithe];
        List<DeckCard> inert = [plains, bear, tithe with { OpponentDependent = false, ProducedColors = null }];

        var report = Tier1Report.Build(DecklistOf(flagged), flagged, [], maxTurn: 6);
        var baseline = Tier1Report.Build(DecklistOf(inert), inert, [], maxTurn: 6);

        Assert.Equal(["Plains"], report.ManaSources.Sources.Select(s => s.Name));
        Assert.Equal(baseline.ManaSources.ByColor, report.ManaSources.ByColor);
        Assert.Equal(
            baseline.DemandVsSupply.Select(r => (r.Name, r.Turn, r.Supply, r.CastingProbability)),
            report.DemandVsSupply.Select(r => (r.Name, r.Turn, r.Supply, r.CastingProbability)));
        Assert.Equal(
            new ExcludedSource(tithe.OracleId, "Smothering Tithe", 1, ExclusionReason.OpponentDependent),
            Assert.Single(report.ManaSources.Excluded));
        Assert.Empty(baseline.ManaSources.Excluded);
    }

    // Casting probability for (color C, k pips, turn T) is the category draw
    // probability over the C-sources with availability ≤ T — the same
    // hypergeometric tail over that set's count, 7 + T cards seen.
    [Fact]
    public void Casting_probability_is_category_draw_over_the_turn_available_source_set()
    {
        var plains = Land("Plains", 20, "W");
        var signet = Rock("Signet", 2, "W", "U");
        var deck = new List<DeckCard> { plains, signet, Spell("Wrath", "{2}{W}{W}", 4), Spell("Filler", "{3}", 3, 60) };
        var decklist = DecklistOf(deck);

        var report = Tier1Report.Build(decklist, deck, [], maxTurn: 6);

        var cell = report.DemandVsSupply.Single(r => r.Name == "Wrath");
        Assert.Equal(["W"], cell.Colors);
        Assert.Equal(2, cell.Pips);
        Assert.Equal(4, cell.Turn);
        Assert.Equal(21, cell.Supply);
        var sources = new HashSet<Guid> { plains.OracleId, signet.OracleId };
        Assert.Equal(CategoryDraw.AtLeastByTurn(decklist, sources, 2, 4, GameRules.Commander), cell.CastingProbability);
        Assert.Equal(Hypergeometric.TailAtLeast(2, 21, decklist.DeckSize, CardsSeen.ByTurn(4, GameRules.Commander)), cell.CastingProbability);
    }

    [Fact]
    public void Source_with_availability_turn_after_the_cell_turn_is_excluded()
    {
        var plains = Land("Plains", 20, "W");
        // Availability turn 3 (mana value 2 + 1): outside a turn-2 cell.
        var signet = Rock("Signet", 2, "W");
        var deck = new List<DeckCard> { plains, signet, Spell("Knight", "{W}{W}", 2), Spell("Filler", "{3}", 3, 60) };
        var decklist = DecklistOf(deck);

        var report = Tier1Report.Build(decklist, deck, [], maxTurn: 6);

        var cell = report.DemandVsSupply.Single(r => r.Name == "Knight");
        Assert.Equal(20, cell.Supply);
        Assert.Equal(
            CategoryDraw.AtLeastByTurn(decklist, new HashSet<Guid> { plains.OracleId }, 2, 2, GameRules.Commander),
            cell.CastingProbability);
        Assert.NotEqual(
            CategoryDraw.AtLeastByTurn(decklist, new HashSet<Guid> { plains.OracleId, signet.OracleId }, 2, 2, GameRules.Commander),
            cell.CastingProbability);
    }

    [Fact]
    public void Land_drop_line_is_present_by_default_over_the_deck_land_count()
    {
        var deck = new List<DeckCard> { Land("Forest", 36, "G"), Land("Wastes", 2, "C"), Spell("Filler", "{3}", 3, 61) };
        var decklist = DecklistOf(deck);

        var report = Tier1Report.Build(decklist, deck, [], maxTurn: 5);

        Assert.Equal(38, report.LandCount);
        Assert.Equal([1, 2, 3, 4, 5], report.LandDrops.Select(l => l.Turn));
        Assert.All(report.LandDrops, l => Assert.Equal(
            Hypergeometric.TailAtLeast(l.Turn, 38, 99, CardsSeen.ByTurn(l.Turn, GameRules.Commander)), l.Probability));
    }

    [Fact]
    public void Hybrid_pips_are_either_rows_over_the_union_of_both_colors_sources()
    {
        var plains = Land("Plains", 10, "W");
        var island = Land("Island", 10, "U");
        var tundra = Land("Tundra", 4, "W", "U");
        var deck = new List<DeckCard> { plains, island, tundra, Spell("Hybrid", "{W/U}{W/U}", 2), Spell("Filler", "{3}", 3, 75) };
        var decklist = DecklistOf(deck);

        var report = Tier1Report.Build(decklist, deck, [], maxTurn: 4);

        var cell = Assert.Single(report.DemandVsSupply);
        Assert.True(cell.Either);
        Assert.Equal(["W", "U"], cell.Colors);
        Assert.Equal(2, cell.Pips);
        Assert.Equal(24, cell.Supply);
        Assert.Equal(
            CategoryDraw.AtLeastByTurn(decklist, new HashSet<Guid> { plains.OracleId, island.OracleId, tundra.OracleId }, 2, 2, GameRules.Commander),
            cell.CastingProbability);
    }

    [Fact]
    public void Commander_demand_row_is_flagged_and_phyrexian_note_rides_along()
    {
        var deck = new List<DeckCard>
        {
            Land("Forest", 30, "G"),
            Spell("Commander", "{1}{G}{G/P}", 3) with { IsCommander = true },
            Spell("Filler", "{3}", 3, 69),
        };

        var report = Tier1Report.Build(DecklistOf(deck), deck, [], maxTurn: 4);

        var cell = Assert.Single(report.DemandVsSupply);
        Assert.True(cell.IsCommander);
        Assert.Equal(1, cell.Pips);
        Assert.Contains("{G/P}", Assert.Single(cell.Notes));
    }

    [Fact]
    public void Category_draws_cover_every_caller_category_by_k_and_turn()
    {
        var forest = Land("Forest", 30, "G");
        var ramp = Spell("Ramp", "{1}{G}", 2, 8);
        var deck = new List<DeckCard> { forest, ramp, Spell("Filler", "{3}", 3, 61) };
        var decklist = DecklistOf(deck);
        var categories = new List<ReportCategory>
        {
            new("lands and ramp", new HashSet<Guid> { forest.OracleId, ramp.OracleId }, K: 1),
            new("ramp", new HashSet<Guid> { ramp.OracleId }, K: 2),
        };

        var report = Tier1Report.Build(decklist, deck, categories, maxTurn: 3);

        Assert.Equal(8, report.CategoryDraws.Count);
        Assert.Equal([0, 1, 2, 3], report.CategoryDraws.Where(c => c.Category == "ramp").Select(c => c.Turn));
        var opener = report.CategoryDraws.Single(c => c.Category == "lands and ramp" && c.Turn == 0);
        Assert.Equal(1, opener.K);
        Assert.Equal(0.9706945754289187, opener.Probability, 1e-15);
        var rampTurn3 = report.CategoryDraws.Single(c => c.Category == "ramp" && c.Turn == 3);
        Assert.Equal(2, rampTurn3.K);
        Assert.Equal(CategoryDraw.AtLeastByTurn(decklist, categories[1].Cards, 2, 3, GameRules.Commander), rampTurn3.Probability);
        Assert.Equal(2, report.Input.Categories.Count);
        Assert.Equal(38, report.Input.Categories[0].Count);
    }

    [Fact]
    public void Source_table_carries_counts_spread_and_availability_per_color()
    {
        var deck = new List<DeckCard>
        {
            Spell("Esper Commander", "{W}{U}{B}", 3) with { IsCommander = true, ColorIdentity = ["W", "U", "B"] },
            Land("Plains", 10, "W"),
            Land("Tundra", 4, "W", "U"),
            Land("Command Tower", 1, "W", "U", "B", "R", "G") with { IdentityBound = true },
            Rock("Signet", 2, "W", "U"),
        };

        var report = Tier1Report.Build(DecklistOf(deck), deck, [], maxTurn: 4);

        Assert.Equal(4, report.ManaSources.Sources.Count);
        Assert.Equal(3, report.ManaSources.Sources.Single(s => s.Name == "Signet").AvailabilityTurn);
        var white = report.ManaSources.ByColor.Single(c => c.Color == "W");
        Assert.Equal(16, white.Count);
        Assert.Equal(10, white.Mono);
        Assert.Equal(5, white.Dual);
        Assert.Equal(1, white.ThreePlus);
        Assert.Equal(["W", "U", "B", "C"], report.ManaSources.ByColor.Select(c => c.Color));
    }

    // Property: for every deck, by_color counts no color outside
    // the deck's color identity. An identity-bound source makes no such color.
    [Fact]
    public void By_color_never_counts_a_color_outside_the_identity()
    {
        string[] wubrg = ["W", "U", "B", "R", "G"];
        var random = new Random(239);
        string[] Subset(int max) => [.. wubrg.Where(_ => random.Next(max) == 0)];

        for (var i = 0; i < 500; i++)
        {
            var commanders = Enumerable.Range(0, random.Next(3))
                .Select(n => Spell($"Commander {n}", "{3}", 3) with { IsCommander = true, ColorIdentity = Subset(2) })
                .ToList();
            var cards = Enumerable.Range(0, random.Next(1, 12))
                .Select(n => (random.Next(2) == 0 ? Land($"Land {n}", random.Next(1, 10)) : Rock($"Rock {n}", random.Next(5))) with
                {
                    ProducedColors = random.Next(4) == 0 ? [.. wubrg, "C"] : [.. Subset(3), .. random.Next(3) == 0 ? new[] { "C" } : []],
                    IdentityBound = random.Next(2) == 0,
                    ColorIdentity = Subset(3),
                })
                .ToList();
            List<DeckCard> deck = [.. commanders, .. cards];
            var identity = (commanders.Count > 0 ? commanders : cards).SelectMany(c => c.ColorIdentity).ToHashSet();
            bool InIdentity(string color) => color == "C" || identity.Contains(color);

            var report = Tier1Report.Build(DecklistOf(deck), deck, [], maxTurn: 3);

            Assert.All(report.ManaSources.ByColor, row => Assert.True(InIdentity(row.Color) || row.Count == 0, row.Color));
            Assert.All(
                report.ManaSources.Sources.Where(s => cards.Single(c => c.OracleId == s.OracleId).IdentityBound),
                source => Assert.All(source.Colors, color => Assert.True(InIdentity(color), color)));
        }
    }

    [Fact]
    public void Report_has_the_echo_the_assumptions_and_the_three_tables_and_no_tier2_fields()
    {
        var forest = Land("Forest", 36, "G");
        var deck = new List<DeckCard>
        {
            forest,
            Spell("Commander", "{2}{G}{G}", 4) with { IsCommander = true },
            Spell("Bear", "{1}{G}", 2, 63),
        };
        var decklist = DecklistOf(deck);

        var report = Tier1Report.Build(decklist, deck, [new("lands", new HashSet<Guid> { forest.OracleId }, 1)], maxTurn: 10);
        var json = JsonSerializer.SerializeToElement(report, WireJson.Options);

        Assert.Equal(99, json.GetProperty("input").GetProperty("deck_size").GetInt32());
        Assert.Equal(10, json.GetProperty("input").GetProperty("max_turn").GetInt32());
        Assert.Single(json.GetProperty("input").GetProperty("commanders").EnumerateArray());
        var assumptions = json.GetProperty("assumptions").EnumerateArray().Select(a => a.GetString()!).ToList();
        Assert.Equal(AssumptionLines.Length, assumptions.Count);
        foreach (var line in AssumptionLines)
        {
            Assert.Contains(assumptions, a => a.Contains(line, StringComparison.OrdinalIgnoreCase));
        }

        Assert.NotEmpty(json.GetProperty("category_draws").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("mana_sources").GetProperty("sources").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("demand_vs_supply").EnumerateArray());
        Assert.Equal(10, json.GetProperty("land_drops").GetArrayLength());

        Assert.Equal("{}", json.GetProperty("versions").GetRawText());
        foreach (var key in PropertyNames(json))
        {
            Assert.DoesNotMatch("confidence|^ci$|_ci$|seed|floor|legal", key);
        }
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };

    // A spell whose only colored symbols count zero still gets a row, so the
    // Phyrexian note is reported rather than lost with the empty pip set.
    [Fact]
    public void Phyrexian_only_spell_keeps_a_row_for_its_note()
    {
        var deck = new List<DeckCard> { Land("Island", 30, "U"), Spell("Gitaxian", "{U/P}", 1), Spell("Filler", "{3}", 3, 60) };

        var report = Tier1Report.Build(DecklistOf(deck), deck, [], maxTurn: 4);

        var cell = Assert.Single(report.DemandVsSupply);
        Assert.Empty(cell.Colors);
        Assert.Equal(0, cell.Pips);
        Assert.Equal(1, cell.CastingProbability);
        Assert.Contains("{U/P}", Assert.Single(cell.Notes));
    }

    // The demand table shares the land-drop line's horizon: a spell demanded
    // after max_turn has no denominator row, so it is omitted.
    [Fact]
    public void Spells_demanded_after_max_turn_are_omitted()
    {
        var deck = new List<DeckCard> { Land("Island", 30, "U"), Spell("Titan", "{4}{U}{U}", 6), Spell("Bear", "{1}{U}", 2) };

        var report = Tier1Report.Build(DecklistOf(deck), deck, [], maxTurn: 5);

        Assert.Equal("Bear", Assert.Single(report.DemandVsSupply).Name);
    }

    [Fact]
    public void Commander_echo_follows_the_loaded_deck_not_the_request_order()
    {
        var deck = new List<DeckCard>
        {
            Spell("Akiri", "{1}{R}{W}", 3) with { IsCommander = true },
            Spell("Bruse", "{2}{R}{W}", 4) with { IsCommander = true },
            Land("Plains", 20, "W"),
        };
        var decklist = DecklistOf(deck) with { Commanders = [deck[1].OracleId, deck[0].OracleId] };

        var report = Tier1Report.Build(decklist, deck, [], maxTurn: 2);

        Assert.Equal(["Akiri", "Bruse"], report.Input.Commanders.Select(c => c.Name));
    }

    [Fact]
    public void Partial_list_without_commander_produces_a_complete_report()
    {
        var deck = new List<DeckCard> { Land("Island", 12, "U"), Spell("Counter", "{U}{U}", 2, 8) };
        var decklist = DecklistOf(deck);

        var report = Tier1Report.Build(decklist, deck, [], maxTurn: 10);

        Assert.Equal(20, report.Input.DeckSize);
        Assert.Empty(report.Input.Commanders);
        Assert.Equal(AssumptionLines.Length, report.Assumptions.Count);
        Assert.Empty(report.CategoryDraws);
        Assert.Single(report.ManaSources.Sources);
        Assert.Single(report.DemandVsSupply);
        Assert.Equal(10, report.LandDrops.Count);
        Assert.InRange(report.LandDrops[0].Probability, 0.99, 1);
    }

    [Fact]
    public void Empty_deck_produces_a_report_of_zeros()
    {
        var report = Tier1Report.Build(new ResolvedDecklist(), [], [new("x", new HashSet<Guid>(), 1)], maxTurn: 2);

        Assert.Equal(0, report.Input.DeckSize);
        Assert.All(report.CategoryDraws, c => Assert.Equal(0, c.Probability));
        Assert.All(report.LandDrops, l => Assert.Equal(0, l.Probability));
    }

    [Fact]
    public void Max_turn_below_one_throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Tier1Report.Build(new ResolvedDecklist(), [], [], maxTurn: 0));
}
