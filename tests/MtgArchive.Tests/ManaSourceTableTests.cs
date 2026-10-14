using MtgArchive.Simulation;

namespace MtgArchive.Tests;

/// <summary>
/// The tier-1 mana-source table: full per-color counting with a
/// color-spread bucket, deck-relative fetch resolution at query time, and
/// availability turns with the supply-at-turn cutoff.
/// </summary>
public sealed class ManaSourceTableTests
{
    private static DeckCard Land(string name, string[] produced, string[]? subtypes = null, bool basic = false)
        => new()
        {
            OracleId = Guid.NewGuid(),
            Name = name,
            Quantity = 1,
            IsLand = true,
            IsBasic = basic,
            CardTypes = ["Land"],
            Supertypes = basic ? ["Basic"] : [],
            Subtypes = subtypes ?? [],
            ProducedColors = produced,
        };

    private static DeckCard Fetch(string[] types, bool basicOnly = false) => new()
    {
        OracleId = Guid.NewGuid(),
        Name = "Fetch",
        Quantity = 1,
        IsLand = true,
        FetchLandTypes = types,
        FetchBasicOnly = basicOnly,
    };

    private static DeckCard Rock(int manaValue) => new()
    {
        OracleId = Guid.NewGuid(),
        Name = "Rock",
        Quantity = 1,
        ManaValue = manaValue,
        ProducedColors = ["C"],
    };

    [Fact]
    public void Dual_land_counts_once_toward_each_color_and_is_bucketed_dual()
    {
        var table = ManaSourceTable.Build([Land("Tundra", ["W", "U"], ["Plains", "Island"])]);

        var source = Assert.Single(table.Sources);
        Assert.Equal(["W", "U"], source.Colors);
        Assert.Equal(ColorSpread.Dual, source.Spread);
        Assert.Equal(1, table.Count("W"));
        Assert.Equal(1, table.Count("U"));
        Assert.Equal(0, table.Count("B"));
    }

    [Fact]
    public void Spread_buckets_are_mono_dual_and_three_plus()
    {
        var table = ManaSourceTable.Build(
        [
            Land("Plains", ["W"]),
            Land("Sol Ring", ["C"]),
            Land("Tundra", ["W", "U"]),
            Land("Arcane Sanctum", ["W", "U", "B"]),
            Land("Command Tower", ["W", "U", "B", "R", "G"]),
        ]);

        Assert.Equal(
            [ColorSpread.Mono, ColorSpread.Mono, ColorSpread.Dual, ColorSpread.ThreePlus, ColorSpread.ThreePlus],
            table.Sources.Select(s => s.Spread));
    }

    [Fact]
    public void Two_mana_rock_is_available_from_turn_three()
    {
        var table = ManaSourceTable.Build([Rock(2)]);

        Assert.Equal(3, Assert.Single(table.Sources).AvailabilityTurn);
    }

    [Fact]
    public void Lands_are_available_on_their_drop_turn()
    {
        var table = ManaSourceTable.Build([Land("Plains", ["W"])]);

        Assert.Equal(1, Assert.Single(table.Sources).AvailabilityTurn);
    }

    [Fact]
    public void Supply_at_turn_excludes_sources_with_later_availability()
    {
        var plains = Land("Plains", ["W"]);
        var table = ManaSourceTable.Build([plains with { Quantity = 10 }, Rock(2), Rock(0)]);

        Assert.Equal(10, table.Supply("W", 1));
        Assert.Equal(1, table.Supply("C", 1));
        Assert.Equal(1, table.Supply("C", 2));
        Assert.Equal(2, table.Supply("C", 3));
    }

    [Fact]
    public void Fetch_colors_are_the_union_over_matching_deck_lands_and_change_with_the_deck()
    {
        var fetch = Fetch(["Forest", "Plains"]);
        var forest = Land("Forest", ["G"], ["Forest"], basic: true);
        var plains = Land("Plains", ["W"], ["Plains"], basic: true);
        var breeding = Land("Breeding Pool", ["G", "U"], ["Forest", "Island"]);
        var island = Land("Island", ["U"], ["Island"], basic: true);

        var gw = ManaSourceTable.Build([fetch, forest, plains, island]);
        var gu = ManaSourceTable.Build([fetch, breeding, island]);

        var inGw = gw.Sources.Single(s => s.OracleId == fetch.OracleId);
        var inGu = gu.Sources.Single(s => s.OracleId == fetch.OracleId);
        Assert.Equal(["W", "G"], inGw.Colors);
        Assert.Equal(["U", "G"], inGu.Colors);
        Assert.Equal(ColorSpread.Dual, inGw.Spread);
        Assert.Equal(ColorSpread.Dual, inGu.Spread);
    }

    [Fact]
    public void Fetch_with_no_matching_deck_lands_contributes_nothing()
    {
        var table = ManaSourceTable.Build([Fetch(["Forest"]), Land("Island", ["U"], ["Island"])]);

        Assert.Equal("Island", Assert.Single(table.Sources).Name);
    }

    [Fact]
    public void Basic_only_fetch_skips_nonbasic_typed_lands()
    {
        var breeding = Land("Breeding Pool", ["G", "U"], ["Forest", "Island"]);
        var forest = Land("Forest", ["G"], ["Forest"], basic: true);

        var table = ManaSourceTable.Build([Fetch([], basicOnly: true), breeding, forest]);

        Assert.Equal(["G"], table.Sources.Single(s => s.Name == "Fetch").Colors);
    }

    [Fact]
    public void Commander_is_never_a_source()
    {
        var table = ManaSourceTable.Build([Rock(1) with { IsCommander = true }]);

        Assert.Empty(table.Sources);
    }

    // An identity-bound source makes only the colors of the deck's color identity.
    private static DeckCard Commander(params string[] identity) => new()
    {
        OracleId = Guid.NewGuid(),
        Name = "Commander",
        Quantity = 1,
        IsCommander = true,
        ColorIdentity = identity,
    };

    private static DeckCard CommandTower() => Land("Command Tower", ["W", "U", "B", "R", "G"]) with { IdentityBound = true };

    [Fact]
    public void Identity_bound_source_is_clipped_to_the_commanders_identity()
    {
        var esper = ManaSourceTable.Build([Commander("W", "U", "B"), CommandTower()]);
        var azorius = ManaSourceTable.Build([Commander("W", "U"), CommandTower()]);

        Assert.Equal(["W", "U", "B"], Assert.Single(esper.Sources).Colors);
        Assert.Equal(ColorSpread.ThreePlus, esper.Sources[0].Spread);
        Assert.Equal(["W", "U"], Assert.Single(azorius.Sources).Colors);
        Assert.Equal(ColorSpread.Dual, azorius.Sources[0].Spread);
        Assert.Equal(0, esper.Count("R"));
    }

    [Fact]
    public void Partner_commanders_union_their_identities()
    {
        var table = ManaSourceTable.Build([Commander("W"), Commander("G"), CommandTower()]);

        Assert.Equal(["W", "G"], Assert.Single(table.Sources).Colors);
        Assert.Equal(["W", "G"], table.Identity);
    }

    [Fact]
    public void Without_a_commander_the_identity_is_the_union_of_the_cards()
    {
        var table = ManaSourceTable.Build(
            [CommandTower(), Land("Island", ["U"], ["Island"], basic: true) with { ColorIdentity = ["U"] }]);

        Assert.Equal(["U"], table.Identity);
        Assert.Equal(["U"], table.Sources.Single(s => s.Name == "Command Tower").Colors);
    }

    [Fact]
    public void Identity_bound_source_under_a_colorless_commander_makes_nothing()
    {
        var table = ManaSourceTable.Build([Commander(), CommandTower()]);

        Assert.Empty(table.Sources);
    }

    [Fact]
    public void Unbound_five_color_source_keeps_every_color()
    {
        var confluence = Land("Mana Confluence", ["W", "U", "B", "R", "G"]);

        var table = ManaSourceTable.Build([Commander("W", "U"), confluence]);

        Assert.Equal(["W", "U", "B", "R", "G"], Assert.Single(table.Sources).Colors);
    }

    [Fact]
    public void Clipping_keeps_colorless()
    {
        var gate = Land("Gond Gate", ["W", "U", "B", "R", "G", "C"]) with { IdentityBound = true };

        var table = ManaSourceTable.Build([Commander("R"), gate]);

        Assert.Equal(["R", "C"], Assert.Single(table.Sources).Colors);
        Assert.Equal(ColorSpread.Mono, table.Sources[0].Spread);
    }

    // The table does not count an opponent-dependent source; it lists the source as excluded.
    [Fact]
    public void Opponent_dependent_source_is_excluded_and_listed()
    {
        var tithe = Rock(4) with { Name = "Smothering Tithe", Quantity = 2, ProducedColors = ["W", "U", "B", "R", "G"], OpponentDependent = true };

        var table = ManaSourceTable.Build([Commander("W", "U", "B", "R", "G"), tithe, Rock(1)]);

        Assert.Equal(["Rock"], table.Sources.Select(s => s.Name));
        Assert.Equal(0, table.Count("W"));
        Assert.Equal(
            new ExcludedSource(tithe.OracleId, "Smothering Tithe", 2, ExclusionReason.OpponentDependent),
            Assert.Single(table.Excluded));
    }

    // An opponent-dependent fetch is excluded only where it would
    // be a source: a deck with no land it can find lists nothing.
    [Fact]
    public void Opponent_dependent_fetch_is_listed_only_when_it_finds_a_land()
    {
        var knight = Rock(2) with { Name = "Knight of the White Orchid", ProducedColors = null, FetchLandTypes = ["Plains"], OpponentDependent = true };
        var plains = Land("Plains", ["W"], ["Plains"], basic: true);
        var island = Land("Island", ["U"], ["Island"], basic: true);

        Assert.Empty(ManaSourceTable.Build([Commander("W", "U"), knight, island]).Excluded);
        Assert.Equal(
            new ExcludedSource(knight.OracleId, "Knight of the White Orchid", 1, ExclusionReason.OpponentDependent),
            Assert.Single(ManaSourceTable.Build([Commander("W", "U"), knight, plains, island]).Excluded));
    }

    // A card that gives its only token to another player is excluded with its own reason.
    [Fact]
    public void Token_for_another_player_source_is_excluded_with_its_own_reason()
    {
        var offer = Rock(1) with { Name = "An Offer You Can't Refuse", ProducedColors = ["W", "U", "B", "R", "G"], TokenToAnotherPlayer = true };

        var table = ManaSourceTable.Build([Commander("W", "U", "B", "R", "G"), offer, Rock(1)]);

        Assert.Equal(["Rock"], table.Sources.Select(s => s.Name));
        Assert.Equal(0, table.Count("W"));
        Assert.Equal(
            new ExcludedSource(offer.OracleId, "An Offer You Can't Refuse", 1, ExclusionReason.TokenToAnotherPlayer),
            Assert.Single(table.Excluded));
    }

    [Fact]
    public void Opponent_dependent_commander_is_not_listed_as_excluded()
    {
        var table = ManaSourceTable.Build([Commander("R") with { ProducedColors = ["R"], OpponentDependent = true }]);

        Assert.Empty(table.Excluded);
    }
}
