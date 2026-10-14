package mtgarchive.xmage;

import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

class MulliganTest {

    /** Rejects the first {@code rejections} hands, then keeps; answers the rest like PASS. */
    private static TestGames.Recorder rejecting(int rejections) {
        AtomicInteger keeps = new AtomicInteger();
        return new TestGames.Recorder(m -> m instanceof Messages.DecideKeep
                ? Messages.Reply.ofKeep(m.seq(), keeps.incrementAndGet() > rejections)
                : TestGames.pass().ask(m));
    }

    @Test
    void keepIsAskedOnTheSevenBeforeAnyBottom() {
        TestGames.Recorder r = rejecting(2);
        Messages.GameOver over = TestGames.play(11, 1, r);

        List<Messages.DecideKeep> keeps = r.all(Messages.DecideKeep.class);
        assertEquals(List.of(7, 7, 7), keeps.stream().map(k -> k.keep().hand().size()).toList());
        assertEquals(List.of(0, 1, 2), keeps.stream().map(k -> k.keep().mulligansTaken()).toList());
        assertEquals(List.of(7, 7, 6), keeps.stream().map(k -> k.keep().keepSize()).toList());

        Messages.DecideCount bottom = r.all(Messages.DecideCount.class).get(0);
        assertEquals("bottom", bottom.kind());
        assertEquals(1, bottom.count());
        assertEquals(7, bottom.view().hand().size());
        assertTrue(r.messages.indexOf(bottom) > r.messages.indexOf(keeps.get(2)));

        Messages.View end = r.all(Messages.EventEndOfTurn.class).get(0).view();
        assertEquals(7, end.hand().size()); // six kept and the turn-1 draw
        assertEquals(92, end.libraryCount());
        assertEquals("horizon", over.reason());
    }

    @Test
    void aRejectedHandGetsNoBottomDecision() {
        TestGames.Recorder r = rejecting(3);
        TestGames.play(11, 1, r);

        assertEquals(List.of(7, 7, 6, 5), r.all(Messages.DecideKeep.class).stream().map(k -> k.keep().keepSize()).toList());
        List<Messages.DecideCount> bottoms = r.all(Messages.DecideCount.class).stream()
                .filter(d -> d.kind().equals("bottom")).toList();
        assertEquals(1, bottoms.size());
        assertEquals(2, bottoms.get(0).count());
    }

    @Test
    void withNoFreeMulliganTheFirstMulliganCosts() {
        TestGames.Recorder r = rejecting(1);
        TestGames.play(new Messages.StartGame("start_game", TestGames.obeka().deckId(), 11, 1, 40, true, true, 0, false, false),
                TestGames.obeka(), r);

        assertEquals(List.of(7, 6), r.all(Messages.DecideKeep.class).stream().map(k -> k.keep().keepSize()).toList());
        assertEquals(1, r.all(Messages.DecideCount.class).get(0).count());
    }
}
