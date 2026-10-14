using MtgArchive.Simulation;

namespace MtgArchive.Tests;

/// <summary>
/// Category draw probability: P(≥ k of a caller-supplied card
/// set among cards seen by turn N), on the shared cards-seen convention
/// (opener = 7, turn N = 7 + N). The set carries no semantics; single-card
/// odds are the k = 1, one-card-set case of the same path.
/// </summary>
public sealed class CategoryDrawTests
{
    private static readonly Guid Land = Guid.NewGuid();
    private static readonly Guid Ramp = Guid.NewGuid();
    private static readonly Guid Filler = Guid.NewGuid();

    // 30 + 8 category members + 61 others: the fixture's 99-card deck
    // with its 38-card {Land, Ramp} category.
    private static readonly ResolvedDecklist Deck = new()
    {
        Entries = [new(Land, 30), new(Ramp, 8), new(Filler, 61)],
    };

    [Fact]
    public void Cards_seen_convention_is_seven_plus_turn_with_the_draw_and_one_less_on_the_play()
    {
        Assert.Equal(7, CardsSeen.Opener);
        Assert.Equal(CardsSeen.Opener, CardsSeen.ByTurn(0, GameRules.Commander));
        Assert.Equal(7 + 4, CardsSeen.ByTurn(4, GameRules.Commander));
        Assert.Throws<ArgumentOutOfRangeException>(() => CardsSeen.ByTurn(-1, GameRules.Commander));
        Assert.Equal([7, 7, 8, 10], new[] { 0, 1, 2, 4 }.Select(turn => CardsSeen.ByTurn(turn, GameRules.Brawl)));
    }

    // Opener probability uses exactly 7 cards seen: the value matches the
    // fixture's independently computed P(>=1 of 38 in 7 from 99).
    [Fact]
    public void Opener_uses_exactly_seven_cards_seen()
    {
        var landsAndRamp = new HashSet<Guid> { Land, Ramp };

        var opener = CategoryDraw.AtLeastByTurn(Deck, landsAndRamp, k: 1, turn: 0, GameRules.Commander);

        Assert.Equal(0.9706945754289187, opener, 1e-15);
    }

    [Fact]
    public void Turn_n_uses_seven_plus_n_cards_seen()
    {
        var landsAndRamp = new HashSet<Guid> { Land, Ramp };

        Assert.Equal(0.9827989899256697,
            CategoryDraw.AtLeastByTurn(Deck, landsAndRamp, k: 1, turn: 1, GameRules.Commander), 1e-15);
        Assert.Equal(0.9981154105164439,
            CategoryDraw.AtLeastByTurn(Deck, landsAndRamp, k: 1, turn: 5, GameRules.Commander), 1e-15);
    }

    // The category count is the sum of the member entries' quantities;
    // cards outside the decklist add nothing.
    [Fact]
    public void Category_count_sums_member_quantities()
    {
        var withStranger = new HashSet<Guid> { Land, Ramp, Guid.NewGuid() };

        Assert.Equal(
            CategoryDraw.AtLeastByTurn(Deck, new HashSet<Guid> { Land, Ramp }, 2, 3, GameRules.Commander),
            CategoryDraw.AtLeastByTurn(Deck, withStranger, 2, 3, GameRules.Commander));
        Assert.Equal(0, CategoryDraw.AtLeastByTurn(Deck, new HashSet<Guid> { Guid.NewGuid() }, 1, 3, GameRules.Commander));
    }

    // Single-card odds are k=1 with a one-card set — the same code path,
    // matching the raw tail over that card's quantity.
    [Fact]
    public void Single_card_odds_are_the_one_card_set_case()
    {
        Assert.Equal(
            Hypergeometric.TailAtLeast(1, 8, 99, CardsSeen.ByTurn(2, GameRules.Commander)),
            CategoryDraw.AtLeastByTurn(Deck, new HashSet<Guid> { Ramp }, 1, 2, GameRules.Commander));
    }
}
