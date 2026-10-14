package mtgarchive.xmage;

import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Finds on every decision view (roadmap carried-forward item 7): the pilot ranks a discard, a bottom
 * and a pick by what each tutor can find, as it ranks an action.
 */
class FindsTest {

    /** Tutors of the done-query 2 deck whose search XMage runs as a SearchEffect. */
    private static final Set<String> TUTORS = Set.of("Demonic Tutor", "Vampiric Tutor", "Mystical Tutor");

    private static final List<TestGames.Recorder> GAMES = new ArrayList<>();

    @BeforeAll
    static void play() {
        for (long seed = 1; seed <= 8; seed++) {
            TestGames.Recorder r = new TestGames.Recorder(TestGames.first());
            TestGames.play(seed, 10, r);
            GAMES.add(r);
        }
    }

    @Test
    void everyDecisionViewHasFindsForTheTutorsInHand() {
        int outsideActions = 0;
        for (TestGames.Recorder g : GAMES) {
            for (Messages.InGame m : g.messages) {
                if (m instanceof Messages.EventEndOfTurn || m instanceof Messages.EventResolved) {
                    continue;
                }
                Messages.View v = TestGames.viewOf(m);
                for (String card : v.hand()) {
                    if (TUTORS.contains(card)) {
                        assertTrue(v.finds().containsKey(card), card + " in hand has no finds in " + m);
                        if (m instanceof Messages.DecideCount || m instanceof Messages.DecidePick) {
                            outsideActions++;
                        }
                    }
                }
            }
        }
        assertTrue(outsideActions > 0, "no pick, bottom or discard view held a tutor");
    }

    @Test
    void aTutorPickHasFindsForEachOfferedTutor() {
        int checked = 0;
        for (TestGames.Recorder g : GAMES) {
            for (Messages.DecidePick p : g.all(Messages.DecidePick.class)) {
                if (!p.pick().purpose().equals("tutor")) {
                    continue;
                }
                for (Messages.PickOption o : p.pick().options()) {
                    if (o.card() != null && TUTORS.contains(o.card())) {
                        assertTrue(p.view().finds().containsKey(o.card()), o.card() + " offered without finds");
                        checked++;
                    }
                }
            }
        }
        assertTrue(checked > 0, "no tutor pick offered a tutor");
    }

    @Test
    void theCommandersSearchHasFinds() {
        Engine.warmUp();
        LoadedDeck sidisi = TestGames.LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("Sidisi, Undead Vizier"),
                List.of(new Messages.DeckEntry("Swamp", 99))));
        TestGames.Recorder r = new TestGames.Recorder(TestGames.pass());
        TestGames.play(new Messages.StartGame("start_game", sidisi.deckId(), 7, 1, 40, true, true, 1, false, false), sidisi, r);

        Messages.View v = r.messages.stream()
                .filter(m -> !(m instanceof Messages.EventEndOfTurn || m instanceof Messages.EventResolved))
                .map(TestGames::viewOf)
                .filter(view -> view.commandZone().contains("Sidisi, Undead Vizier"))
                .findFirst().orElseThrow();
        assertEquals(List.of("Swamp"), v.finds().get("Sidisi, Undead Vizier"));
    }

    /**
     * Controller ruling: a search pick's finds sources are the offered options, never the whole
     * library (ADR 0003). Demonic Tutor (any-card search) sits unmatched in a creature-only search's
     * library: while it stays there, unpicked, it must not appear as a finds key.
     */
    @Test
    void aTutorNotOfferedByAnothersSearchIsNotAFindsSource() {
        Engine.warmUp();
        LoadedDeck deck = TestGames.LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("Isamaru, Hound of Konda"),
                List.of(new Messages.DeckEntry("Forest", 60), new Messages.DeckEntry("Plains", 36),
                        new Messages.DeckEntry("Worldly Tutor", 1), new Messages.DeckEntry("Grizzly Bears", 1),
                        new Messages.DeckEntry("Demonic Tutor", 1))));
        int checked = 0;
        for (long seed = 1; seed <= 16; seed++) {
            TestGames.Recorder r = new TestGames.Recorder(TestGames.first());
            TestGames.play(new Messages.StartGame("start_game", deck.deckId(), seed, 10, 40, true, true, 1, false, false), deck, r);
            for (Messages.DecidePick p : r.all(Messages.DecidePick.class)) {
                if (!p.pick().purpose().equals("tutor") || p.view().hand().contains("Demonic Tutor")) {
                    continue;
                }
                checked++;
                assertFalse(p.view().finds().containsKey("Demonic Tutor"),
                        "Demonic Tutor, unmatched and still in library, appeared as a finds source: " + p);
            }
        }
        assertTrue(checked > 0, "no tutor pick left Demonic Tutor in the library in sixteen seeds");
    }

    @Test
    void eventsCarryNoFinds() {
        for (TestGames.Recorder g : GAMES) {
            g.all(Messages.EventEndOfTurn.class).forEach(e -> assertTrue(e.view().finds().isEmpty()));
            g.all(Messages.EventResolved.class).forEach(e -> assertTrue(e.view().finds().isEmpty()));
        }
    }
}
