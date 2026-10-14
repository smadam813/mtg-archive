package mtgarchive.xmage;

import mage.counters.CounterType;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.function.Function;

import static mtgarchive.xmage.InteractiveDisruptionTest.COUNTERSPELL;
import static mtgarchive.xmage.InteractiveDisruptionTest.cheatOntoBattlefield;
import static mtgarchive.xmage.InteractiveDisruptionTest.logged;
import static mtgarchive.xmage.InteractiveDisruptionTest.opposed;
import static mtgarchive.xmage.InteractiveDisruptionTest.start;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The scripted opponent casts Atomize at our end step (ADR 0013). The end-of-turn event before disruption
 * takes oppose; Atomize takes the first candidate it can legally target and can affect; our seat settles it at its
 * first empty-stack priority, after the dies triggers; inside the end-step sequence the opposed event takes oppose
 * again; and the after view goes out once the sequence ends.
 */
class InteractiveRemovalTest {

    static final String ATOMIZE = "Atomize";

    /**
     * Casts the first listed card of {@code cast}, else plays the first land, and answers the window with
     * {@code respondWith} (null passes). Each end-of-turn view before disruption goes to {@code endOfTurn}, and each
     * opposed event to {@code opposed}: a null answer continues.
     */
    static Decider endStep(List<String> cast, String respondWith, Function<Messages.EventEndOfTurn, List<String>> endOfTurn,
                           Function<Messages.Opposed, List<String>> opposed) {
        return endStep(ATOMIZE, cast, respondWith, endOfTurn, opposed);
    }

    /** As {@link #endStep(List, String, Function, Function)}, opposing with {@code card} (Rout). */
    static Decider endStep(String card, List<String> cast, String respondWith,
                           Function<Messages.EventEndOfTurn, List<String>> endOfTurn,
                           Function<Messages.Opposed, List<String>> opposed) {
        return m -> {
            if (m instanceof Messages.DecideRespond d) {
                for (Messages.WireAction a : d.view().actions()) {
                    if (a.card().equals(respondWith)) {
                        return Messages.Reply.ofAction(m.seq(), a.id());
                    }
                }
                return Messages.Reply.ofAction(m.seq(), null);
            }
            if (m instanceof Messages.DecideAction d) {
                for (String name : cast) {
                    for (Messages.WireAction a : d.view().actions()) {
                        if (a.card().equals(name) && !a.kind().equals("activate")) {
                            return Messages.Reply.ofAction(m.seq(), a.id());
                        }
                    }
                }
                for (Messages.WireAction a : d.view().actions()) {
                    if (a.kind().equals("land")) {
                        return Messages.Reply.ofAction(m.seq(), a.id());
                    }
                }
                return Messages.Reply.ofAction(m.seq(), null);
            }
            List<String> targets = null;
            if (m instanceof Messages.EventEndOfTurn e && !e.afterDisruption()) {
                targets = endOfTurn.apply(e);
            } else if (m instanceof Messages.Opposed o) {
                targets = opposed.apply(o);
            }
            return targets != null ? Messages.Reply.ofOppose(m.seq(), card, targets) : TestGames.pass().ask(m);
        };
    }

    /** Opposes the first end of turn from {@code turn} on that shows a permanent of {@code names}, at those, in order. */
    static Function<Messages.EventEndOfTurn, List<String>> once(int turn, String... names) {
        boolean[] done = {false};
        return e -> {
            if (done[0] || e.view().turn() < turn) {
                return null;
            }
            List<String> ids = idsOf(e.view(), names);
            if (ids.isEmpty()) {
                return null;
            }
            done[0] = true;
            return ids;
        };
    }

    /** The ids of our permanents with these names, in the order of the names, then of the view. */
    static List<String> idsOf(Messages.View view, String... names) {
        List<String> ids = new ArrayList<>();
        for (String name : names) {
            for (Messages.ViewPermanent p : view.battlefield()) {
                if (p.card().equals(name)) {
                    ids.add(p.id());
                }
            }
        }
        return ids;
    }

    static boolean onBattlefield(Messages.View view, String name) {
        return view.battlefield().stream().anyMatch(p -> p.card().equals(name));
    }

    /** The after view that follows {@code from} in the recorded messages. */
    static Messages.EventEndOfTurn afterView(TestGames.Recorder r, Messages.InGame from) {
        for (int i = r.messages.indexOf(from) + 1; i < r.messages.size(); i++) {
            if (r.messages.get(i) instanceof Messages.EventEndOfTurn e) {
                assertTrue(e.afterDisruption(), "an end-of-turn view came where the after view was due");
                return e;
            }
        }
        throw new AssertionError("no after view");
    }

    static Lab.Hook cheatOnTurnOne(String... names) {
        return new Lab.Hook(seat -> seat.game().getTurnNum() == 1 && seat.game().getStack().isEmpty(),
                seat -> cheatOntoBattlefield(seat, names));
    }

    /** Talrand and 99 Islands: a deck that casts nothing before Talrand. */
    static LoadedDeck islands() {
        return DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Island", 99));
    }

    /**
     * The candidates in order: a hexproof creature, an indestructible one, then Grizzly Bears. Atomize cannot target
     * the first or affect the second, so it takes the Bears, and the other two stand on the after view.
     */
    @Test
    void atomizeSkipsAHexproofAndAnIndestructibleCandidateAndTakesTheNext() {
        LoadedDeck deck = islands();
        TestGames.Recorder r = new TestGames.Recorder(endStep(List.of(), null,
                once(1, "Gladecover Scout", "Darksteel Myr", "Grizzly Bears"), o -> null));
        Messages.GameOver over = Lab.play(start(deck, 7, 2), deck, r, cheatOnTurnOne("Gladecover Scout", "Darksteel Myr", "Grizzly Bears"));

        assertEquals("horizon", over.reason(), over.error());
        Messages.Opposed hit = opposed(r).get(0);
        assertEquals(ATOMIZE, hit.card());
        assertEquals("hit", hit.outcome());
        assertEquals(1, hit.victims().size());
        assertEquals("Grizzly Bears", hit.victims().get(0).card());
        assertTrue(hit.victims().get(0).hit());
        assertEquals(List.of("Grizzly Bears"), hit.lost());
        Messages.View after = afterView(r, hit).view();
        assertTrue(onBattlefield(after, "Gladecover Scout"));
        assertTrue(onBattlefield(after, "Darksteel Myr"));
        assertFalse(onBattlefield(after, "Grizzly Bears"));
        assertTrue(after.graveyard().contains("Grizzly Bears"));
        assertTrue(logged(over, "OPPOSE Atomize on Grizzly Bears"));
        assertTrue(logged(over, "OPPOSED Atomize hit"));
    }

    /** No candidate Atomize can target and affect: not_cast with no_target, then the after view; the event stays armed. */
    @Test
    void noCandidateGivesNotCastThenTheAfterView() {
        LoadedDeck deck = islands();
        int[] tries = {0};
        TestGames.Recorder r = new TestGames.Recorder(endStep(List.of(), null, e -> {
            List<String> ids = idsOf(e.view(), "Gladecover Scout", "Darksteel Myr");
            if (ids.isEmpty()) {
                return null;
            }
            tries[0]++;
            return ids;
        }, o -> null));
        Messages.GameOver over = Lab.play(start(deck, 7, 2), deck, r, cheatOnTurnOne("Gladecover Scout", "Darksteel Myr"));

        assertEquals("horizon", over.reason(), over.error());
        assertEquals(2, tries[0], "a not_cast event stays armed: each end step opposes again");
        for (Messages.Opposed o : opposed(r)) {
            assertEquals("not_cast", o.outcome());
            assertEquals(Opposition.NO_TARGET, ((Messages.EventNotCast) o).reason());
            assertEquals(List.of(), o.victims());
            assertTrue(afterView(r, o).view().battlefield().size() >= 2);
        }
        assertEquals(2, opposed(r).size());
        assertFalse(over.log().stream().anyMatch(e -> e.message().contains("casts Atomize")));
    }

    /**
     * Proliferate chooses nothing. The opponent has an energy counter, which the idle opponent's own choice would
     * proliferate (a benefit to itself); after Atomize it still has one.
     */
    @Test
    void atomizeProliferatesNothing() {
        LoadedDeck deck = islands();
        int[] energy = {-1};
        Lab.Hook counter = new Lab.Hook(seat -> seat.game().getTurnNum() == 1 && seat.game().getStack().isEmpty(), seat -> {
            cheatOntoBattlefield(seat, "Grizzly Bears");
            seat.opponent().addCounters(CounterType.ENERGY.createInstance(), seat.opponent().getId(), null, seat.game());
        });
        Lab.Hook read = new Lab.Hook(seat -> seat.game().getTurnNum() == 3 && seat.game().getStack().isEmpty(),
                seat -> energy[0] = seat.opponent().getCountersCount(CounterType.ENERGY));
        TestGames.Recorder r = new TestGames.Recorder(endStep(List.of(), null, once(1, "Grizzly Bears"), o -> null));
        Messages.GameOver over = Lab.play(start(deck, 7, 2), deck, r, counter, read);

        assertEquals("horizon", over.reason(), over.error());
        assertEquals("hit", opposed(r).get(0).outcome());
        assertEquals(1, energy[0], "proliferate chose the opponent");
    }

    /** Doomed Traveler: its dies trigger resolves before the settle, so the opposed event and the after view show the Spirit. */
    @Test
    void aDiesTriggerResolvesBeforeTheAfterView() {
        LoadedDeck travelers = DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Plains", 60),
                new Messages.DeckEntry("Doomed Traveler", 39));
        TestGames.Recorder r = new TestGames.Recorder(endStep(List.of("Doomed Traveler"), null, once(1, "Doomed Traveler"), o -> null));
        Messages.GameOver over = TestGames.play(start(travelers, 7, 3), travelers, r);

        assertEquals("horizon", over.reason(), over.error());
        Messages.Opposed hit = opposed(r).get(0);
        assertEquals("hit", hit.outcome());
        Messages.View after = afterView(r, hit).view();
        assertTrue(after.graveyard().contains("Doomed Traveler"));
        assertTrue(after.battlefield().stream().anyMatch(p -> p.card().startsWith("Spirit")),
                "no Spirit token: the dies trigger did not resolve before the after view " + after.battlefield());
    }

    @Test
    void aRemovedCommanderGoesToTheCommandZone() {
        LoadedDeck deck = islands();
        TestGames.Recorder r = new TestGames.Recorder(endStep(List.of("Talrand, Sky Summoner"), null,
                once(1, "Talrand, Sky Summoner"), o -> null));
        Messages.GameOver over = TestGames.play(start(deck, 7, 5), deck, r);

        assertEquals("horizon", over.reason(), over.error());
        Messages.Opposed hit = opposed(r).get(0);
        assertEquals("hit", hit.outcome());
        Messages.View after = afterView(r, hit).view();
        assertEquals(4, after.turn());
        assertEquals(List.of("Talrand, Sky Summoner"), after.commandZone());
        assertFalse(onBattlefield(after, "Talrand, Sky Summoner"));
    }

    /**
     * The end-step chain: the opposed event of the first Atomize takes oppose, and a second Atomize takes the second
     * Bears. The after view follows the last opposed event of the sequence, with no Bears on it.
     */
    @Test
    void insideTheEndStepSequenceTheOpposedEventTakesOppose() {
        LoadedDeck deck = islands();
        boolean[] chained = {false};
        TestGames.Recorder r = new TestGames.Recorder(endStep(List.of(), null, once(1, "Grizzly Bears"), o -> {
            if (chained[0]) {
                return null;
            }
            chained[0] = true;
            return idsOf(o.view(), "Grizzly Bears");
        }));
        Messages.GameOver over = Lab.play(start(deck, 7, 2), deck, r, cheatOnTurnOne("Grizzly Bears", "Grizzly Bears"));

        assertEquals("horizon", over.reason(), over.error());
        List<Messages.Opposed> both = opposed(r);
        assertEquals(2, both.size());
        assertEquals("hit", both.get(0).outcome());
        assertEquals("hit", both.get(1).outcome());
        assertEquals(both.get(0).view().turn(), both.get(1).view().turn());
        assertTrue(r.messages.indexOf(both.get(1)) > r.messages.indexOf(both.get(0)));
        Messages.View after = afterView(r, both.get(1)).view();
        assertFalse(onBattlefield(after, "Grizzly Bears"));
        assertEquals(1, r.all(Messages.EventEndOfTurn.class).stream()
                .filter(e -> e.afterDisruption() && e.view().turn() == after.turn()).count());
    }

    /** Our Counterspell answers Atomize in the end-step response window: the Needle stands on the after view. */
    @Test
    void ourCounterspellAnswersAtomize() {
        LoadedDeck deck = ResponseWindowTest.deckWith(COUNTERSPELL);
        TestGames.Recorder r = new TestGames.Recorder(endStep(List.of(ResponseWindowTest.NEEDLE), COUNTERSPELL, e ->
                e.view().hand().contains(COUNTERSPELL) && ResponseWindowTest.untappedIslands(e.view()) >= 2
                        ? nonNull(idsOf(e.view(), ResponseWindowTest.NEEDLE)) : null, o -> null));
        Messages.GameOver over = TestGames.play(start(deck, 11, 6), deck, r);

        assertEquals("horizon", over.reason(), over.error());
        assertFalse(opposed(r).isEmpty(), "seed 11 never opposes a Needle with Counterspell up: pick another seed, keep the assertions");
        Messages.Opposed answered = opposed(r).get(0);
        assertEquals("answered", answered.outcome(), over.log().toString());
        assertFalse(answered.victims().get(0).hit());
        Messages.DecideRespond respond = r.all(Messages.DecideRespond.class).get(0);
        assertEquals(ATOMIZE, respond.respond().threat().card());
        assertEquals(answered.victims().get(0).id(), respond.respond().victims().get(0).id());
        assertTrue(respond.respond().victims().get(0).id().startsWith("p:"));
        assertTrue(logged(over, "RESPOND Counterspell"));
        assertTrue(onBattlefield(afterView(r, answered).view(), ResponseWindowTest.NEEDLE));
    }

    static final String SNAKESKIN_VEIL = "Snakeskin Veil";

    /**
     * Snakeskin Veil answers Atomize in the end-step window. The target hint skips the threat (a spell is
     * no target creature you control) and aims the Veil at the victim, so the Bears gain hexproof, Atomize fizzles on
     * resolution, and the settle reports answered. The Bears stand on the after view.
     */
    @Test
    void snakeskinVeilOnTheVictimMakesAtomizeFizzle() {
        LoadedDeck deck = DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Forest", 60),
                new Messages.DeckEntry(SNAKESKIN_VEIL, 39));
        TestGames.Recorder r = new TestGames.Recorder(endStep(List.of(), SNAKESKIN_VEIL, e ->
                e.view().hand().contains(SNAKESKIN_VEIL) && untappedForests(e.view()) >= 1
                        ? nonNull(idsOf(e.view(), "Grizzly Bears")) : null, o -> null));
        Messages.GameOver over = Lab.play(start(deck, 7, 4), deck, r, cheatOnTurnOne("Grizzly Bears"));

        assertEquals("horizon", over.reason(), over.error());
        assertFalse(opposed(r).isEmpty(), "seed 7 never opposes the Bears with the Veil up: pick another seed, keep the assertions");
        Messages.Opposed answered = opposed(r).get(0);
        assertEquals(ATOMIZE, answered.card());
        assertEquals("answered", answered.outcome(), over.log().toString());
        assertFalse(answered.victims().get(0).hit());
        assertEquals(List.of(), answered.lost());
        assertTrue(logged(over, "RESPOND " + SNAKESKIN_VEIL));
        Messages.DecideRespond respond = r.all(Messages.DecideRespond.class).get(0);
        assertEquals(answered.victims().get(0).id(), respond.respond().victims().get(0).id());
        Messages.View after = afterView(r, answered).view();
        assertTrue(onBattlefield(after, "Grizzly Bears"));
        assertTrue(after.graveyard().contains(SNAKESKIN_VEIL));
    }

    static long untappedForests(Messages.View view) {
        return view.battlefield().stream().filter(p -> p.card().equals("Forest") && !p.tapped()).count();
    }

    /** The self-test's interactive pass casts Atomize from the end of turn 3, once, with zero engine errors. */
    @Test
    void theSelfTestResponderCastsAtomizeFromTheEndOfTurnThree() {
        LoadedDeck obeka = TestGames.obeka();
        int cast = 0;
        for (long seed = 121; seed < 126; seed++) {
            TestGames.Recorder r = new TestGames.Recorder(new InteractiveResponder());
            // As SelfTest starts an interactive game: cast events on.
            Messages.StartGame start = new Messages.StartGame("start_game", obeka.deckId(), seed, 10, 40, true, true, 1, true, false);
            Messages.GameOver over = TestGames.play(start.withOpponent(SelfTest.TABLE), obeka, r);
            assertTrue(over.error() == null, over.error());
            List<Messages.Opposed> atomize = opposed(r).stream().filter(o -> o.card().equals(ATOMIZE)).toList();
            assertTrue(atomize.stream().allMatch(o -> o.view().turn() >= InteractiveResponder.REMOVAL_TURN));
            assertTrue(atomize.stream().filter(o -> !o.outcome().equals("not_cast")).count() <= 1);
            cast += (int) atomize.stream().filter(o -> !o.outcome().equals("not_cast")).count();
        }
        assertTrue(cast > 0, "no self-test game cast Atomize in seeds 121 to 125");
    }

    static List<String> nonNull(List<String> ids) {
        return ids.isEmpty() ? null : ids;
    }
}
