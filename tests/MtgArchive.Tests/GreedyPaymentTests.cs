using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// The greedy payment over an engine-neutral mana board. The pilot predicts
/// the engine's payment with it, so the same board must give the same
/// payment whoever built it.
/// </summary>
public sealed class GreedyPaymentTests
{
    private static readonly DeckCard SpiritGuide = TestDeckCards.Spell("Simian Spirit Guide", "{2}{R}", "Creature");

    private static ManaBoard Board(IReadOnlyList<BoardManaSource> sources, IReadOnlyList<HandManaOption>? hand = null, int life = 40)
        => new(new ManaPool(), life, sources, hand ?? []);

    [Fact]
    public void One_sol_ring_tap_pays_two_generic()
    {
        var ring = new BoardManaSource("p:0", ["{C}{C}"]);

        var payment = GreedyPayment.Solve(Board([ring]), ManaCost.Parse("{2}"));

        Assert.NotNull(payment);
        Assert.Equal([new BoardTap(ring, "{C}{C}")], payment.Taps);
        Assert.Equal(ManaPool.Parse("{C}{C}"), payment.Spend);
    }

    [Fact]
    public void A_pip_takes_a_mono_source_before_a_dual()
    {
        var dual = new BoardManaSource("p:0", ["{W}", "{U}"]);
        var island = new BoardManaSource("p:1", ["{U}"]);

        var payment = GreedyPayment.Solve(Board([dual, island]), ManaCost.Parse("{U}"));

        Assert.Equal([new BoardTap(island, "{U}")], payment!.Taps);
    }

    [Fact]
    public void A_card_in_hand_pays_only_when_the_board_cannot()
    {
        var swamp = new BoardManaSource("p:0", ["{B}"]);
        var guide = new HandManaOption(SpiritGuide, ["{R}"]);
        var board = Board([swamp], [guide]);

        Assert.Empty(GreedyPayment.Solve(board, ManaCost.Parse("{B}"))!.FromHand);
        Assert.Equal([new HandTap(guide, "{R}")], GreedyPayment.Solve(board, ManaCost.Parse("{B}{R}"))!.FromHand);
        Assert.False(GreedyPayment.CanPay(board, ManaCost.Parse("{R}"), useHand: false));
    }

    [Fact]
    public void The_card_being_cast_cannot_pay_for_itself()
    {
        var board = Board([], [new HandManaOption(SpiritGuide, ["{R}"])]);

        Assert.True(GreedyPayment.CanPay(board, ManaCost.Parse("{R}")));
        Assert.False(GreedyPayment.CanPay(board, ManaCost.Parse("{R}"), casting: SpiritGuide));

        var two = Board([], [new HandManaOption(SpiritGuide, ["{R}"]), new HandManaOption(SpiritGuide, ["{R}"])]);
        Assert.True(GreedyPayment.CanPay(two, ManaCost.Parse("{R}"), casting: SpiritGuide));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(2, false)]
    public void A_phyrexian_pip_pays_life_only_while_the_player_stays_alive(int life, bool payable)
    {
        var payment = GreedyPayment.Solve(Board([], life: life), ManaCost.Parse("{B/P}"));

        Assert.Equal(payable, payment is { Life: 2 });
    }
}
