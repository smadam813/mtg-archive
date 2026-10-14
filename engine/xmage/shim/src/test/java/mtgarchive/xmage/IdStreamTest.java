package mtgarchive.xmage;

import mage.util.RandomUtil;
import org.junit.jupiter.api.Test;

import java.util.Random;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** Patch 0002: a playable calculation resets the object-ID stream when it ends. */
class IdStreamTest {

    @Test
    void theStreamDrawsTheIdsOfAPlainRandom() {
        long seed = 7;
        RandomUtil.setSeed(seed);
        Random plain = new Random(seed * 0x9E3779B97F4A7C15L + 0x632BE59BD9B4E019L);
        for (int i = 0; i < 5; i++) {
            long hi = (plain.nextLong() & 0xFFFFFFFFFFFF0FFFL) | 0x0000000000004000L;
            long lo = (plain.nextLong() & 0x3FFFFFFFFFFFFFFFL) | 0x8000000000000000L;
            assertEquals(new UUID(hi, lo), RandomUtil.newUUID());
        }
    }

    @Test
    void aResetBringsBackTheIdsDrawnSinceTheMark() {
        RandomUtil.setSeed(11);
        RandomUtil.newUUID();
        long mark = RandomUtil.markIdStream();
        UUID first = RandomUtil.newUUID();
        UUID second = RandomUtil.newUUID();
        assertNotEquals(first, second);
        RandomUtil.resetIdStream(mark);
        assertEquals(first, RandomUtil.newUUID());
        assertEquals(second, RandomUtil.newUUID());
    }

    @Test
    void aPlayableCalculationLeavesTheStreamWhereItWas() {
        LoadedDeck needles = DisruptionTest.needles();
        long[] before = new long[2];
        long[] after = new long[2];
        long[] copy = new long[2];
        boolean[] ran = new boolean[1];
        Lab.Hook hook = new Lab.Hook(seat -> InteractiveDisruptionTest.ourMainPhase(seat, 2), seat -> {
            InteractiveDisruptionTest.cheatOntoBattlefield(seat, "Mana Confluence", "Sol Ring", "Island", "Island", "Pithing Needle");
            copy[0] = RandomUtil.markIdStream();
            unwrapped(seat);
            copy[1] = RandomUtil.markIdStream();
            RandomUtil.resetIdStream(copy[0]);
            before[0] = RandomUtil.markIdStream();
            seat.me().getPlayable(seat.game(), true);
            after[0] = RandomUtil.markIdStream();
            before[1] = after[0];
            seat.me().getManaAvailable(seat.game());
            after[1] = RandomUtil.markIdStream();
            ran[0] = true;
        });
        Messages.GameOver over = Lab.play(InteractiveDisruptionTest.start(needles, 7, 3), needles, TestGames.pass(), hook);

        assertEquals("horizon", over.reason(), over.error());
        assertTrue(ran[0], "the hook never ran");
        assertNotEquals(copy[0], copy[1], "the calculation draws no object IDs here, so this test proves nothing");
        assertEquals(before[0], after[0], "getPlayable moved the object-ID stream");
        assertEquals(before[1], after[1], "getManaAvailable moved the object-ID stream");
    }

    /** The calculation of getPlayable without the reset, to show that it draws IDs in the state the test plays. */
    private static void unwrapped(Lab.Seat seat) {
        try {
            java.lang.reflect.Method m = mage.players.PlayerImpl.class.getDeclaredMethod("getPlayableCalc",
                    mage.game.Game.class, boolean.class, mage.constants.Zone.class, boolean.class);
            m.setAccessible(true);
            m.invoke(seat.me(), seat.game(), true, mage.constants.Zone.ALL, true);
        } catch (ReflectiveOperationException e) {
            throw new AssertionError(e);
        }
    }
}
