package mtgarchive.xmage;

import org.junit.jupiter.api.Test;

import java.util.List;

import static mtgarchive.xmage.ScenarioTest.cast;
import static mtgarchive.xmage.ScenarioTest.choose;
import static mtgarchive.xmage.ScenarioTest.ok;
import static mtgarchive.xmage.ScenarioTest.scenario;
import static mtgarchive.xmage.ScenarioTest.untapped;
import static org.junit.jupiter.api.Assertions.assertEquals;

/**
 * The library-order picks the pilot makes (roadmap carried-forward item 2): the order of cards put
 * back on top, Brainstorm's cards from hand, and Ponder's shuffle. The bottom order of a scry
 * follows pick order. Each test is a scenario on XMage.
 */
class LibraryPicksTest {

    private static List<String> purposes(Messages.ScenarioResult r) {
        return r.picks().stream().map(Messages.PickRecord::purpose).toList();
    }

    @Test
    void brainstormAsksTwoPutBackPicksAndTheLastGoesOnTop() {
        Messages.ScenarioResult r = ok(scenario(List.of("Brainstorm"), List.of(untapped("Island")),
                List.of("Sol Ring", "Forest", "Swamp", "Sheoldred, the Apocalypse"), List.of(),
                List.of(cast("Brainstorm"), choose("Swamp"), choose("Forest"))));

        assertEquals(List.of("put_back", "put_back"), purposes(r));
        assertEquals(null, r.picks().get(0).seen());
        assertEquals(List.of("Sol Ring"), r.view().hand());
        assertEquals(List.of("Forest", "Swamp", "Sheoldred, the Apocalypse"), r.library());
    }

    @Test
    void ponderOrdersTheTopThreeThenAsksTheShuffleWithThemAsSeen() {
        Messages.ScenarioResult r = ok(scenario(List.of("Ponder"), List.of(untapped("Island")),
                List.of("Forest", "Sol Ring", "Swamp", "Brainstorm"), List.of(),
                List.of(cast("Ponder"), choose("Forest"), choose("Swamp"), choose("do not shuffle"))));

        assertEquals(List.of("order_top", "order_top", "shuffle"), purposes(r));
        assertEquals(List.of("Forest", "Sol Ring", "Swamp"), r.picks().get(0).seen());
        assertEquals(List.of("do not shuffle", "shuffle"), r.picks().get(2).options());
        assertEquals(List.of("Sol Ring", "Swamp", "Forest"), r.picks().get(2).seen());
        assertEquals(List.of("Sol Ring"), r.view().hand());
        assertEquals(List.of("Swamp", "Forest", "Brainstorm"), r.library());
    }

    @Test
    void ponderShufflesWhenThePilotSaysSo() {
        Messages.ScenarioResult r = ok(scenario(List.of("Ponder"), List.of(untapped("Island")),
                List.of("Forest", "Sol Ring", "Swamp", "Brainstorm"), List.of(),
                List.of(cast("Ponder"), choose("Forest"), choose("Swamp"), choose("shuffle"))));

        assertEquals("shuffle", r.picks().get(2).chosen());
        assertEquals(3, r.view().libraryCount());
    }

    @Test
    void theOrderOfIdenticalCardsIsNotAsked() {
        Messages.ScenarioResult r = ok(scenario(List.of("Ponder"), List.of(untapped("Island")),
                List.of("Swamp", "Swamp", "Swamp", "Forest"), List.of(),
                List.of(cast("Ponder"), choose("do not shuffle"))));

        assertEquals(List.of("shuffle"), purposes(r));
        assertEquals(List.of("Swamp", "Swamp", "Swamp"), r.picks().get(0).seen());
    }

    @Test
    void preordainOrdersTheCardsItKeeps() {
        Messages.ScenarioResult r = ok(scenario(List.of("Preordain"), List.of(untapped("Island")),
                List.of("Forest", "Sol Ring", "Swamp", "Brainstorm"), List.of(),
                List.of(cast("Preordain"), choose("keep the rest"), choose("Forest"))));

        assertEquals(List.of("scry", "order_top"), purposes(r));
        assertEquals(List.of("Sol Ring"), r.view().hand());
        assertEquals(List.of("Forest", "Swamp", "Brainstorm"), r.library());
    }

    @Test
    void aScryPutsItsBottomCardsDownInPickOrder() {
        Messages.ScenarioResult r = ok(scenario(List.of("Preordain"), List.of(untapped("Island")),
                List.of("Forest", "Sol Ring", "Swamp", "Brainstorm"), List.of(),
                List.of(cast("Preordain"), choose("Forest"), choose("Sol Ring"))));

        assertEquals(List.of("scry", "scry"), purposes(r));
        assertEquals(List.of("Swamp"), r.view().hand());
        assertEquals(List.of("Brainstorm", "Forest", "Sol Ring"), r.library());
    }
}
