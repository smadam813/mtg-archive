package mtgarchive.xmage;

import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** A card XMage lacks plays as a blank card: it keeps its library slot and is never cast (roadmap carried-forward item 1). */
class BlankCardTest {

    private static final String MISSING = "No Such Card In Xmage";

    private static LoadedDeck deck() {
        Engine.warmUp();
        return TestGames.LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("Talrand, Sky Summoner"),
                List.of(new Messages.DeckEntry("Island", 69), new Messages.DeckEntry(MISSING, 30))));
    }

    @Test
    void aCardXmageLacksIsUnimplementedAndPlaysAsBlanks() {
        LoadedDeck d = deck();

        assertEquals(List.of(MISSING), d.unimplemented());
        assertEquals(List.of(new Messages.DeckEntry(MISSING, 30)), d.blanks());
        assertTrue(d.isBlank(MISSING));
        assertFalse(d.isBlank("Island"));
    }

    @Test
    void aCommanderXmageLacksGetsNoBlank() {
        Engine.warmUp();
        LoadedDeck d = TestGames.LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("No Such Commander In Xmage"),
                List.of(new Messages.DeckEntry("Island", 99))));

        assertEquals(List.of("No Such Commander In Xmage"), d.unimplemented());
        assertTrue(d.blanks().isEmpty());
    }

    @Test
    void blanksKeepTheLibrarySizeAndAreNeverListed() {
        LoadedDeck d = deck();
        TestGames.Recorder r = new TestGames.Recorder(TestGames.first());
        Messages.GameOver over = TestGames.play(
                new Messages.StartGame("start_game", d.deckId(), 7, 10, 40, true, true, 1, false, false), d, r);

        assertEquals("horizon", over.reason(), over.error());
        assertEquals(91, r.all(Messages.EventEndOfTurn.class).get(0).view().libraryCount()); // 99 less the seven and the turn-1 draw
        assertTrue(r.messages.stream().anyMatch(m -> TestGames.viewOf(m).hand().contains(MISSING)), "no blank was drawn");
        for (Messages.DecideAction a : r.all(Messages.DecideAction.class)) {
            assertTrue(a.view().actions().stream().noneMatch(x -> x.card().equals(MISSING)), "a blank was listed");
        }
    }

    @Test
    void aDeckWithBlanksPlaysTheSameGameForTheSameSeed() {
        LoadedDeck d = deck();
        Messages.StartGame start = new Messages.StartGame("start_game", d.deckId(), 9, 5, 40, true, true, 1, false, false);

        assertEquals(TestGames.play(start, d, TestGames.first()).digest(), TestGames.play(start, d, TestGames.first()).digest());
    }
}
