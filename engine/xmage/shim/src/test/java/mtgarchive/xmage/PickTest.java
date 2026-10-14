package mtgarchive.xmage;

import com.networknt.schema.JsonSchema;
import com.networknt.schema.ValidationMessage;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Picks in the games of ScriptedResponder FIRST, seeds 1 to 16. Since a spirit guide pays from
 * hand, seeds 1 to 8 hold no look that shows a card it does not offer; seed 16 does.
 */
class PickTest {

    /** Every message of each game, in order. */
    private static final List<List<Messages.InGame>> GAMES = new ArrayList<>();
    private static final List<Messages.DecidePick> PICKS = new ArrayList<>();

    @BeforeAll
    static void play() {
        for (long seed = 1; seed <= 16; seed++) {
            TestGames.Recorder r = new TestGames.Recorder(TestGames.first());
            TestGames.play(seed, 10, r);
            GAMES.add(List.copyOf(r.messages));
            PICKS.addAll(r.all(Messages.DecidePick.class));
        }
    }

    private static List<Messages.DecidePick> picks(String purpose) {
        return PICKS.stream().filter(p -> p.pick().purpose().equals(purpose)).toList();
    }

    private static List<String> cardLabels(Messages.DecidePick p) {
        return p.pick().options().stream().filter(o -> o.card() != null).map(Messages.PickOption::label).toList();
    }

    private static boolean isLook(Messages.InGame m) {
        return m instanceof Messages.DecidePick p
                && (p.pick().purpose().equals("scry") || p.pick().purpose().equals("other"));
    }

    /** True when {@code next} offers the cards of {@code prev} less exactly one: the next pick of the same look. */
    private static boolean nextPickOfSameLook(Messages.DecidePick prev, Messages.DecidePick next) {
        if (!prev.pick().purpose().equals(next.pick().purpose())) {
            return false;
        }
        List<String> left = new ArrayList<>(cardLabels(prev));
        for (String label : cardLabels(next)) {
            if (!left.remove(label)) {
                return false;
            }
        }
        return left.size() == 1;
    }

    /** The picks of each scry or look: runs of adjacent pick messages, one card fewer each time. */
    private static List<List<Messages.DecidePick>> looks() {
        List<List<Messages.DecidePick>> looks = new ArrayList<>();
        for (List<Messages.InGame> game : GAMES) {
            List<Messages.DecidePick> run = null;
            for (Messages.InGame m : game) {
                if (!isLook(m)) {
                    run = null;
                    continue;
                }
                Messages.DecidePick p = (Messages.DecidePick) m;
                if (run == null || !nextPickOfSameLook(run.get(run.size() - 1), p)) {
                    run = new ArrayList<>();
                    looks.add(run);
                }
                run.add(p);
            }
        }
        return looks;
    }

    @Test
    void tutorOptionsHideTheLibraryOrder() {
        assertFalse(picks("tutor").isEmpty(), "no tutor pick in sixteen games");
        for (Messages.DecidePick p : picks("tutor")) {
            List<String> labels = cardLabels(p);
            List<String> sorted = new ArrayList<>(labels);
            sorted.sort(null);
            assertEquals(sorted, labels);
            assertNull(p.pick().seen());
        }
    }

    /** Ruling F4: the check cannot pass on zero stop options. */
    @Test
    void theStopOptionIsLastAndNamesNoCard() {
        int stops = 0;
        for (Messages.DecidePick p : PICKS) {
            if (p.pick().purpose().equals("shuffle")) {
                continue; // its two options, do not shuffle and shuffle, name no card by design
            }
            List<Messages.PickOption> options = p.pick().options();
            for (int i = 0; i < options.size(); i++) {
                Messages.PickOption o = options.get(i);
                if (!o.id().equals(PilotPlayer.NONE)) {
                    assertNotNull(o.card(), o.toString());
                    continue;
                }
                stops++;
                assertEquals(options.size() - 1, i, p.toString());
                assertNull(o.card());
                assertTrue(o.label().equals("Find nothing") || o.label().equals("Keep the rest on top"), o.label());
            }
        }
        assertTrue(stops > 0, "no stop option in sixteen games");
    }

    /** Ruling F4: seen lists every looked-at card, and stays the same for every pick of one look. */
    @Test
    void scryAndLookPicksShowTheSeenCards() {
        assertFalse(picks("scry").isEmpty(), "no scry pick in sixteen games");
        boolean seenBeyondOptions = false;
        for (List<Messages.DecidePick> look : looks()) {
            List<String> seen = look.get(0).pick().seen();
            assertNotNull(seen);
            if (look.get(0).pick().purpose().equals("scry")) {
                // A scry may put any looked-at card on the bottom: its first pick offers every seen card.
                assertEquals(seen.size(), cardLabels(look.get(0)).size(), look.get(0).toString());
            }
            for (Messages.DecidePick p : look) {
                assertEquals(seen, p.pick().seen(), p.toString());
                assertTrue(seen.containsAll(cardLabels(p)), p.toString());
                seenBeyondOptions |= seen.size() > cardLabels(p).size();
            }
        }
        // The sixteen games have only one-pick looks: a look that takes one card of four shows the other three.
        assertTrue(seenBeyondOptions, "no look shows a card it does not offer in sixteen games");
    }

    @Test
    void namePicksOfferTheDecksOwnCardsSorted() {
        List<String> deck = TestGames.obeka().names();
        assertFalse(picks("name").isEmpty(), "no name pick in sixteen games");
        for (Messages.DecidePick p : picks("name")) {
            List<String> labels = cardLabels(p);
            assertTrue(deck.containsAll(labels));
            List<String> sorted = new ArrayList<>(labels);
            sorted.sort(null);
            assertEquals(sorted, labels);
        }
    }

    /** The phase-0 schema is the contract for every pick decision the seat sends. */
    @Test
    void everyPickDecisionValidatesAgainstTheSchema() {
        JsonSchema schema = WireContract.rootSchema();
        assertFalse(PICKS.isEmpty(), "no pick in sixteen games");
        for (Messages.DecidePick p : PICKS) {
            String text = Wire.GSON.toJson(p);
            Set<ValidationMessage> errors = WireContract.validate(schema, text);
            assertTrue(errors.isEmpty(), errors + " in " + text);
        }
    }

    /**
     * CR 701.23b: a search for cards with a stated quality (here, "instant or sorcery") need not find
     * them, so its tutor pick offers the stop option even though the search target is mandatory.
     */
    @Test
    void aStatedQualitySearchOffersFindNothingBelowItsMinimum() {
        Messages.ScenarioResult r = TestGames.scenario(ScenarioTest.scenario(List.of("Mystical Tutor"),
                List.of(ScenarioTest.untapped("Island")), List.of("Forest", "Brainstorm", "Sol Ring"), List.of(),
                List.of(ScenarioTest.cast("Mystical Tutor"), ScenarioTest.choose("fail to find"))));

        assertNull(r.error(), String.valueOf(r.error()));
        assertTrue(r.picks().get(0).options().contains(PilotPlayer.NONE), r.picks().get(0).options().toString());
    }

    /**
     * A plain "a card" search (no stated quality: Demonic Tutor) stays mandatory: it never offers the
     * stop option while its minimum is unmet.
     */
    @Test
    void aPlainSearchDoesNotOfferFindNothingBelowItsMinimum() {
        Messages.ScenarioResult r = TestGames.scenario(ScenarioTest.scenario(List.of("Demonic Tutor"),
                List.of(ScenarioTest.untapped("Swamp"), ScenarioTest.untapped("Swamp")), List.of("Sol Ring", "Forest"), List.of(),
                List.of(ScenarioTest.cast("Demonic Tutor"), ScenarioTest.choose("Sol Ring"))));

        assertNull(r.error(), String.valueOf(r.error()));
        assertFalse(r.picks().get(0).options().contains(PilotPlayer.NONE), r.picks().get(0).options().toString());
    }
}
