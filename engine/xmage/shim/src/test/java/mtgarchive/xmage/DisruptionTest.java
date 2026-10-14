package mtgarchive.xmage;

import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.UUID;
import java.util.function.Function;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Tier 3 in the shim (spec §7). The service picks every victim; the shim carries it out. A cast
 * event goes out after our spell is on the stack with its costs paid, and counter moves the spell
 * to the graveyard. At the end of our turn, disrupt moves permanents to the graveyard, and the shim
 * sends the turn's view again with after_disruption.
 */
class DisruptionTest {

    /** A deck of one commander and the given cards; XMage must implement all of them. */
    static LoadedDeck deck(String commander, Messages.DeckEntry... entries) {
        Engine.warmUp();
        LoadedDeck deck = TestGames.LIBRARY.load(new Messages.LoadDeck("load_deck", List.of(commander), List.of(entries)));
        assertEquals(List.of(), deck.unimplemented(), "XMage lacks a card of this test deck: report it, do not swap it");
        return deck;
    }

    /** Talrand, 50 Islands and 49 Pithing Needles: a Needle is castable on turn 1 in almost every game. */
    static LoadedDeck needles() {
        return deck("Talrand, Sky Summoner", new Messages.DeckEntry("Island", 50), new Messages.DeckEntry("Pithing Needle", 49));
    }

    static Messages.StartGame start(LoadedDeck deck, long seed, int horizon, boolean watchCasts) {
        return new Messages.StartGame("start_game", deck.deckId(), seed, horizon, 40, true, true, 1, watchCasts, true);
    }

    /**
     * Casts {@code card} (from hand or the command zone) whenever it is listed, else plays the first
     * land. Cast events and end-of-turn events go to {@code events}; everything else passes.
     */
    static Decider caster(String card, Function<Messages.InGame, Messages.Reply> events) {
        return m -> {
            if (m instanceof Messages.DecideAction d) {
                String land = null;
                for (Messages.WireAction a : d.view().actions()) {
                    if (a.card().equals(card) && !a.kind().equals("activate")) {
                        return Messages.Reply.ofAction(m.seq(), a.id());
                    }
                    if (a.kind().equals("land") && land == null) {
                        land = a.id();
                    }
                }
                return Messages.Reply.ofAction(m.seq(), land);
            }
            if (m instanceof Messages.EventCast || m instanceof Messages.EventEndOfTurn) {
                return events.apply(m);
            }
            return TestGames.pass().ask(m);
        };
    }

    /** Answers counter to the first cast event and continue to every other event. */
    static Function<Messages.InGame, Messages.Reply> counterFirstCast() {
        int[] casts = {0};
        return m -> Messages.Reply.ofDo(m.seq(), m instanceof Messages.EventCast && casts[0]++ == 0 ? "counter" : "continue");
    }

    /** The first end_of_turn view of our turn {@code turn}. */
    static Messages.View endOfTurn(TestGames.Recorder r, int turn) {
        return r.all(Messages.EventEndOfTurn.class).stream()
                .filter(e -> e.view().turn() == turn)
                .findFirst()
                .orElseThrow(() -> new AssertionError("no end_of_turn on turn " + turn))
                .view();
    }

    /** Answers disrupt, naming every permanent called {@code card}, to the first end_of_turn that shows one. */
    static Function<Messages.InGame, Messages.Reply> removeFirst(String card) {
        boolean[] done = {false};
        return m -> {
            if (!done[0] && m instanceof Messages.EventEndOfTurn e && !e.afterDisruption()) {
                List<String> ids = e.view().battlefield().stream().filter(p -> p.card().equals(card)).map(Messages.ViewPermanent::id).toList();
                if (!ids.isEmpty()) {
                    done[0] = true;
                    return Messages.Reply.ofDisrupt(m.seq(), ids);
                }
            }
            return Messages.Reply.ofDo(m.seq(), "continue");
        };
    }

    /** The first after_disruption view, and the end_of_turn view before it that the disrupt answered. */
    static List<Messages.EventEndOfTurn> disruptedTurn(TestGames.Recorder r) {
        Messages.EventEndOfTurn before = null;
        for (Messages.InGame m : r.messages) {
            if (m instanceof Messages.EventEndOfTurn e) {
                if (e.afterDisruption()) {
                    assertTrue(before != null, "an after view came first");
                    return List.of(before, e);
                }
                before = e;
            }
        }
        throw new AssertionError("no after_disruption view");
    }

    /** The "bf=[...]" part of an "END life=" snapshot line. */
    static String battlefieldOf(String snapshot) {
        int start = snapshot.indexOf(" bf=[");
        return snapshot.substring(start, snapshot.indexOf(']', start));
    }

    @Test
    void aCounteredSpellGoesToTheGraveyardAndNeverResolves() {
        LoadedDeck needles = needles();
        TestGames.Recorder r = new TestGames.Recorder(caster("Pithing Needle", counterFirstCast()));
        Messages.GameOver over = TestGames.play(start(needles, 7, 3, true), needles, r);

        assertEquals("horizon", over.reason(), over.error());
        List<Messages.EventCast> casts = r.all(Messages.EventCast.class);
        assertTrue(casts.size() >= 2, "seed 7 casts fewer than two Needles in three turns: pick another seed, keep the assertions");
        Messages.EventCast first = casts.get(0);
        assertEquals("Pithing Needle", first.spell().card());
        assertEquals("front", first.spell().face());
        assertTrue(first.spell().id().startsWith("s:"), first.spell().id());
        assertTrue(first.view().actions().isEmpty(), "an event view lists no actions");

        Messages.View end = endOfTurn(r, first.view().turn());
        assertTrue(end.graveyard().contains("Pithing Needle"), "the countered Needle is not in the graveyard");
        assertTrue(over.log().stream().anyMatch(e -> e.message().equals("DISRUPT counter Pithing Needle")));
        assertTrue(over.log().stream().anyMatch(e -> e.message().equals("COUNTERED Pithing Needle")));
        // The counter took one cast: a later Needle resolves.
        assertTrue(over.log().stream().anyMatch(e -> e.message().equals("RESOLVED Pithing Needle")));
    }

    @Test
    void aCounteredCommanderGoesHomeAndItsTaxRises() {
        LoadedDeck islands = deck("Talrand, Sky Summoner", new Messages.DeckEntry("Island", 99));
        TestGames.Recorder r = new TestGames.Recorder(caster("Talrand, Sky Summoner", counterFirstCast()));
        Messages.GameOver over = TestGames.play(start(islands, 7, 6, true), islands, r);

        assertEquals("horizon", over.reason(), over.error());
        Messages.EventCast countered = r.all(Messages.EventCast.class).get(0);
        assertEquals(4, countered.view().turn());
        assertEquals(List.of(), countered.view().commandZone()); // on the stack
        assertEquals(List.of("Talrand, Sky Summoner"), endOfTurn(r, 4).commandZone());
        assertTrue(r.all(Messages.DecideAction.class).stream().flatMap(d -> d.view().actions().stream())
                        .anyMatch(a -> a.kind().equals("cast_commander") && a.cost().equals("{4}{U}{U}")),
                "the commander tax did not rise after the counter");
    }

    /** Great Sable Stag: "This spell can't be countered." The counter moves it all the same (spec §7). */
    @Test
    void aCounterIgnoresCantBeCountered() {
        LoadedDeck stags = deck("Talrand, Sky Summoner", new Messages.DeckEntry("Forest", 60), new Messages.DeckEntry("Great Sable Stag", 39));
        TestGames.Recorder r = new TestGames.Recorder(caster("Great Sable Stag", counterFirstCast()));
        Messages.GameOver over = TestGames.play(start(stags, 7, 4, true), stags, r);

        assertEquals("horizon", over.reason(), over.error());
        Messages.EventCast first = r.all(Messages.EventCast.class).get(0);
        assertTrue(endOfTurn(r, first.view().turn()).graveyard().contains("Great Sable Stag"));
        assertTrue(over.log().stream().anyMatch(e -> e.message().equals("COUNTERED Great Sable Stag")));
    }

    @Test
    void withoutWatchCastsNoCastEventGoesOut() {
        LoadedDeck needles = needles();
        TestGames.Recorder r = new TestGames.Recorder(caster("Pithing Needle", m -> Messages.Reply.ofDo(m.seq(), "continue")));
        Messages.GameOver over = TestGames.play(start(needles, 7, 2, false), needles, r);

        assertEquals("horizon", over.reason(), over.error());
        assertEquals(List.of(), r.all(Messages.EventCast.class));
        assertTrue(over.log().stream().anyMatch(e -> e.message().equals("RESOLVED Pithing Needle")));
    }

    @Test
    void aCastEventTakesContinueStopOrCounter() {
        LoadedDeck needles = needles();
        Messages.GameOver over = TestGames.play(start(needles, 7, 2, true), needles,
                caster("Pithing Needle", m -> Messages.Reply.ofDo(m.seq(), m instanceof Messages.EventCast ? "disrupt" : "continue")));

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().contains("the cast event takes do=continue, counter, stop"), over.error());
    }

    @Test
    void anEndOfTurnTakesNoCounter() {
        LoadedDeck needles = needles();
        Messages.GameOver over = TestGames.play(start(needles, 7, 2, false), needles,
                caster("Pithing Needle", m -> Messages.Reply.ofDo(m.seq(), m instanceof Messages.EventEndOfTurn ? "counter" : "continue")));

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().contains("the end_of_turn event takes do=continue, disrupt, stop"), over.error());
    }

    @Test
    void aDisruptWithNoRemoveIsAnEngineError() {
        LoadedDeck needles = needles();
        Messages.GameOver over = TestGames.play(start(needles, 7, 2, false), needles,
                caster("Pithing Needle", m -> m instanceof Messages.EventEndOfTurn
                        ? Messages.Reply.ofDisrupt(m.seq(), List.of())
                        : Messages.Reply.ofDo(m.seq(), "continue")));

        assertEquals("engine_error", over.reason());
        assertEquals("bad reply: remove goes with do=disrupt, and disrupt names at least one permanent", over.error());
    }

    @Test
    void aDisruptionMovesThePermanentToTheGraveyardThenSendsTheAfterView() {
        LoadedDeck needles = needles();
        TestGames.Recorder r = new TestGames.Recorder(caster("Pithing Needle", removeFirst("Pithing Needle")));
        Messages.GameOver over = TestGames.play(start(needles, 7, 3, false), needles, r);

        assertEquals("horizon", over.reason(), over.error());
        List<Messages.EventEndOfTurn> turn = disruptedTurn(r);
        Messages.EventEndOfTurn before = turn.get(0);
        Messages.EventEndOfTurn after = turn.get(1);
        assertFalse(before.afterDisruption());
        assertEquals(before.view().turn(), after.view().turn());
        assertEquals(before.seq() + 1, after.seq(), "a decision came between the two views of one end of turn");
        long onBoard = before.view().battlefield().stream().filter(p -> p.card().equals("Pithing Needle")).count();
        assertTrue(onBoard > 0);
        assertTrue(after.view().battlefield().stream().noneMatch(p -> p.card().equals("Pithing Needle")));
        assertEquals(onBoard, after.view().graveyard().stream().filter(c -> c.equals("Pithing Needle")).count()
                - before.view().graveyard().stream().filter(c -> c.equals("Pithing Needle")).count());
        assertTrue(over.log().stream().anyMatch(e -> e.message().equals("DISRUPT remove Pithing Needle")));
        assertEquals(1, r.all(Messages.EventEndOfTurn.class).stream().filter(Messages.EventEndOfTurn::afterDisruption).count());

        // Two snapshots on the disrupted turn: the last one comes after the move and shows no Needle.
        List<String> turnLog = over.log().stream().filter(e -> e.turn() == after.view().turn()).map(Messages.LogEntry::message).toList();
        List<String> snapshots = turnLog.stream().filter(m -> m.startsWith("END life=")).toList();
        assertEquals(2, snapshots.size(), String.join("\n", turnLog));
        assertTrue(turnLog.lastIndexOf(snapshots.get(1)) > turnLog.indexOf("DISRUPT remove Pithing Needle"));
        assertFalse(battlefieldOf(snapshots.get(1)).contains("Pithing Needle"), snapshots.get(1));
        assertTrue(battlefieldOf(snapshots.get(0)).contains("Pithing Needle"), snapshots.get(0));
    }

    @Test
    void aRemovedCommanderGoesToTheCommandZone() {
        LoadedDeck islands = deck("Talrand, Sky Summoner", new Messages.DeckEntry("Island", 99));
        TestGames.Recorder r = new TestGames.Recorder(caster("Talrand, Sky Summoner", removeFirst("Talrand, Sky Summoner")));
        Messages.GameOver over = TestGames.play(start(islands, 7, 5, false), islands, r);

        assertEquals("horizon", over.reason(), over.error());
        Messages.EventEndOfTurn after = disruptedTurn(r).get(1);
        assertEquals(4, after.view().turn());
        assertEquals(List.of("Talrand, Sky Summoner"), after.view().commandZone());
        assertTrue(after.view().battlefield().stream().noneMatch(p -> p.card().equals("Talrand, Sky Summoner")));
    }

    /** Doomed Traveler: "When Doomed Traveler dies, create a 1/1 white Spirit creature token with flying." */
    @Test
    void aDiesTriggerResolvesBeforeTheAfterView() {
        LoadedDeck travelers = deck("Talrand, Sky Summoner", new Messages.DeckEntry("Plains", 60), new Messages.DeckEntry("Doomed Traveler", 39));
        TestGames.Recorder r = new TestGames.Recorder(caster("Doomed Traveler", removeFirst("Doomed Traveler")));
        Messages.GameOver over = TestGames.play(start(travelers, 7, 3, false), travelers, r);

        assertEquals("horizon", over.reason(), over.error());
        Messages.View after = disruptedTurn(r).get(1).view();
        assertTrue(after.graveyard().contains("Doomed Traveler"));
        assertTrue(after.battlefield().stream().anyMatch(p -> p.card().startsWith("Spirit")),
                "no Spirit token: the dies trigger did not resolve before the after view " + after.battlefield());
    }

    @Test
    void aDisruptNamingNoPermanentOfOursIsAnEngineError() {
        LoadedDeck needles = needles();
        Messages.GameOver over = TestGames.play(start(needles, 7, 2, false), needles,
                caster("Pithing Needle", m -> m instanceof Messages.EventEndOfTurn
                        ? Messages.Reply.ofDisrupt(m.seq(), List.of("p:" + UUID.randomUUID()))
                        : Messages.Reply.ofDo(m.seq(), "continue")));

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().contains("not a permanent of ours"), over.error());
    }

    @Test
    void theAfterViewTakesNoSecondDisrupt() {
        LoadedDeck needles = needles();
        Messages.GameOver over = TestGames.play(start(needles, 7, 3, false), needles,
                caster("Pithing Needle", m -> {
                    if (m instanceof Messages.EventEndOfTurn e) {
                        // Needles on the first view of a turn; the lands that still stand on the after view.
                        String card = e.afterDisruption() ? "Island" : "Pithing Needle";
                        List<String> ids = e.view().battlefield().stream().filter(p -> p.card().equals(card)).map(Messages.ViewPermanent::id).toList();
                        if (!ids.isEmpty()) {
                            return Messages.Reply.ofDisrupt(m.seq(), ids);
                        }
                    }
                    return Messages.Reply.ofDo(m.seq(), "continue");
                }));

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().contains("the end_of_turn event takes do=continue, stop"), over.error());
    }
}
