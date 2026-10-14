using MtgArchive.Simulation;

namespace MtgArchive.Tests;

/// <summary>
/// Land faces of modal double-faced cards: tier 1 counts an
/// MDFC land as a land and a pathway for both colors.
/// </summary>
public sealed class MdfcLandTests
{
    private const string ShatterskullName = "Shatterskull Smashing // Shatterskull, the Hammer Pass";

    private const string PathwayName = "Brightclimb Pathway // Grimclimb Pathway";

    private static readonly DeckCard ShatterskullFront = TestDeckCards.Spell(ShatterskullName, "{X}{R}{R}");

    private static readonly DeckCard Shatterskull = ShatterskullFront with
    {
        FrontFaceName = "Shatterskull Smashing",
        BackFace = NonbasicLand("Shatterskull, the Hammer Pass", "R") with { OracleId = ShatterskullFront.OracleId },
    };

    private static readonly DeckCard PathwayFront = NonbasicLand(PathwayName, "W");

    private static readonly DeckCard Pathway = PathwayFront with
    {
        FrontFaceName = "Brightclimb Pathway",
        BackFace = NonbasicLand("Grimclimb Pathway", "B") with { OracleId = PathwayFront.OracleId },
    };

    private static DeckCard NonbasicLand(string name, string color)
        => TestDeckCards.Land(name, color) with { IsBasic = false, Supertypes = [] };

    [Fact]
    public void Tier_1_counts_an_MDFC_land_as_a_land_and_a_pathway_for_both_colors()
    {
        var commander = TestDeckCards.Commander("Commander", "{R}");
        var forests = TestDeckCards.Land("Forest", "G", quantity: 97);
        DeckCard[] deck = [commander, Shatterskull, Pathway, forests];
        var decklist = new ResolvedDecklist
        {
            Commanders = [commander.OracleId],
            Entries = [new(Shatterskull.OracleId, 1), new(Pathway.OracleId, 1), new(forests.OracleId, 97)],
        };

        var report = Tier1Report.Build(decklist, deck, [], maxTurn: 3);

        Assert.Equal(99, report.LandCount);
        Assert.DoesNotContain(PipDemand.Extract(deck), row => row.Name == ShatterskullName);
        var shatterskull = Assert.Single(report.ManaSources.Sources, s => s.Name == ShatterskullName);
        Assert.Equal(["R"], shatterskull.Colors);
        Assert.Equal(1, shatterskull.AvailabilityTurn);
        Assert.Equal(["W", "B"], Assert.Single(report.ManaSources.Sources, s => s.Name == PathwayName).Colors);
        Assert.Contains(report.Assumptions, line => line.StartsWith("Pathways count for both colors", StringComparison.Ordinal));
    }
}
