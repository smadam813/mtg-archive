package mtgarchive.xmage;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.List;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;

/** The diagnostic switches (Obeka turn-4 diagnosis): off by default, and each one does one thing. */
class DiagnosticsTest {

    @AfterEach
    void clear() {
        System.clearProperty(Diagnostics.ETB);
        System.clearProperty(Diagnostics.PAY_LIFE);
    }

    /** A decider that plays a land whenever it can and does nothing else. */
    private static Messages.GameOver playLands(List<Messages.DeckEntry> cards) {
        LoadedDeck deck = TestGames.LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("Talrand, Sky Summoner"), cards));
        assertEquals(List.of(), deck.unimplemented());
        return TestGames.play(new Messages.StartGame("start_game", deck.deckId(), 11, 3, 40, true, true, 1, false, true), deck, m -> {
            if (m instanceof Messages.DecideAction d) {
                for (Messages.WireAction a : d.view().actions()) {
                    if (a.kind().equals("land")) {
                        return Messages.Reply.ofAction(m.seq(), a.id());
                    }
                }
            }
            return TestGames.pass().ask(m);
        });
    }

    private static long count(Messages.GameOver over, String prefix) {
        return over.log().stream().filter(e -> e.message().startsWith(prefix)).count();
    }

    @Test
    void theSwitchesAreOffByDefault() {
        Messages.GameOver over = playLands(List.of(new Messages.DeckEntry("Blood Crypt", 99)));

        assertEquals(0, count(over, "ETB "));
        assertEquals(0, count(over, "DIAG "));
    }

    @Test
    void theEtbSwitchLogsEachLandOnce() {
        System.setProperty(Diagnostics.ETB, "true");

        Messages.GameOver over = playLands(List.of(new Messages.DeckEntry("Island", 99)));

        assertEquals(3, count(over, "ETB Island untapped"), over.log().toString());
    }

    @Test
    void thePayLifeSwitchPaysForAShockland() {
        System.setProperty(Diagnostics.ETB, "true");
        System.setProperty(Diagnostics.PAY_LIFE, "true");

        Messages.GameOver over = playLands(List.of(new Messages.DeckEntry("Blood Crypt", 99)));

        assertEquals(3, count(over, "ETB Blood Crypt untapped"), over.log().toString());
        assertEquals(3, count(over, "DIAG pay-life Blood Crypt"));
        assertTrue(over.log().stream().anyMatch(e -> e.message().startsWith("END life=34")), "three shocks cost 6 life");
    }
}
