package mtgarchive.xmage;

import mage.abilities.SpellAbility;
import mage.cards.Card;
import mage.constants.Zone;
import mage.counters.CounterType;
import mage.game.Game;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;
import java.util.UUID;

import static mtgarchive.xmage.InteractiveDisruptionTest.COUNTERSPELL;
import static mtgarchive.xmage.InteractiveDisruptionTest.logged;
import static mtgarchive.xmage.InteractiveDisruptionTest.opposed;
import static mtgarchive.xmage.InteractiveDisruptionTest.start;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The response window (ADR 0013). While the scripted opponent's disruption card is on top of the stack,
 * our seat gets one respond decide over its legal actions. The service's answer activates under the target hint, the
 * shim logs RESPOND, tracks the spell and sends no cast event, and the settle reports answered or hit.
 */
class ResponseWindowTest {

    static final String NEEDLE = "Pithing Needle";

    /** Talrand, 60 Islands, 20 Needles and 19 copies of the response. */
    static LoadedDeck deckWith(String response) {
        return DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Island", 60),
                new Messages.DeckEntry(NEEDLE, 20), new Messages.DeckEntry(response, 19));
    }

    /**
     * Opposes a Needle cast that leaves two Islands up with {@code response} in hand, and answers the window with
     * {@code respondWith} (null passes).
     */
    static InteractiveDisruptionTest.Opposer opposeWhenHeld(String response, String respondWith) {
        return new InteractiveDisruptionTest.Opposer(List.of(NEEDLE), COUNTERSPELL, c ->
                c.view().hand().contains(response) && untappedIslands(c.view()) >= 2 ? List.of(c.spell().id()) : null,
                respondWith);
    }

    static long untappedIslands(Messages.View view) {
        return view.battlefield().stream().filter(p -> p.card().equals("Island") && !p.tapped()).count();
    }

    static int indexOf(Messages.GameOver over, String message) {
        for (int i = 0; i < over.log().size(); i++) {
            if (over.log().get(i).message().equals(message)) {
                return i;
            }
        }
        return -1;
    }

    /** Every card of this name that the scripted opponent owns is in no zone. */
    static void assertOpponentCardInNoZone(Lab.Seat seat, String name) {
        Game game = seat.game();
        for (Card card : game.getCards()) {
            if (card.getName().equals(name) && card.getOwnerId().equals(seat.opponent().getId())) {
                Zone zone = game.getState().getZone(card.getId());
                assertTrue(zone == null || zone == Zone.OUTSIDE, name + " is in " + zone);
            }
        }
    }

    /** Captures the seat at our first main phase from {@code turn} on. */
    static Lab.Hook seatAt(int turn, Lab.Seat[] seen) {
        return new Lab.Hook(seat -> InteractiveDisruptionTest.ourMainPhase(seat, turn), seat -> seen[0] = seat);
    }

    @Test
    void ourCounterspellAnswersTheOpponents() {
        LoadedDeck deck = deckWith(COUNTERSPELL);
        TestGames.Recorder r = new TestGames.Recorder(opposeWhenHeld(COUNTERSPELL, COUNTERSPELL));
        Lab.Seat[] later = {null};
        Messages.GameOver over = Lab.play(start(deck, 11, 6), deck, r, seatAt(6, later));

        assertEquals("horizon", over.reason(), over.error());
        assertFalse(opposed(r).isEmpty(), "seed 11 never opposes a Needle with Counterspell up: pick another seed, keep the assertions");
        Messages.Opposed answered = opposed(r).get(0);
        assertEquals("answered", answered.outcome(), over.log().toString());
        assertEquals(1, answered.victims().size());
        assertFalse(answered.victims().get(0).hit());
        assertEquals(List.of(), answered.lost());

        Messages.DecideRespond respond = r.all(Messages.DecideRespond.class).get(0);
        assertEquals(COUNTERSPELL, respond.respond().threat().card());
        assertTrue(respond.respond().threat().id().startsWith("s:"));
        assertEquals(List.of(new Messages.RespondVictim(answered.victims().get(0).id(), NEEDLE, "front")), respond.respond().victims());
        assertTrue(respond.view().actions().stream().anyMatch(a -> a.card().equals(COUNTERSPELL)), "the window lists Counterspell");
        assertTrue(r.messages.indexOf(respond) < r.messages.indexOf(answered), "the window opens before the settle");

        // The hint aims our Counterspell at the opponent's: the Needle resolves.
        int at = indexOf(over, "RESPOND Counterspell");
        assertTrue(at >= 0, over.log().toString());
        assertTrue(indexOf(over, "OPPOSED Counterspell answered") > at);
        assertTrue(over.log().subList(at, over.log().size()).stream().anyMatch(e -> e.message().equals("RESOLVED " + NEEDLE)));
        assertFalse(logged(over, "COUNTERED " + NEEDLE));
        // A response sends no cast event.
        assertTrue(r.all(Messages.EventCast.class).stream().noneMatch(c -> c.spell().card().equals(COUNTERSPELL)));
        assertEquals(1, r.all(Messages.DecideRespond.class).size(), "one window per disruption card");
        // One answer deep: the opponent never answers our response.
        assertEquals(1, over.log().stream().filter(e -> e.message().startsWith("Idle casts")).count(), over.log().toString());
        assertNotNull(later[0]);
        assertOpponentCardInNoZone(later[0], COUNTERSPELL);
    }

    @Test
    void aPassedWindowLetsTheCardHit() {
        LoadedDeck deck = deckWith(COUNTERSPELL);
        TestGames.Recorder r = new TestGames.Recorder(opposeWhenHeld(COUNTERSPELL, null));
        Messages.GameOver over = TestGames.play(start(deck, 11, 6), deck, r);

        assertEquals("horizon", over.reason(), over.error());
        assertEquals("hit", opposed(r).get(0).outcome());
        assertEquals(1, r.all(Messages.DecideRespond.class).size());
        assertFalse(over.log().stream().anyMatch(e -> e.message().startsWith("RESPOND")));
    }

    /** A deck with no instant has no legal action in the window: no respond decide goes out, and the card hits. */
    @Test
    void noLegalActionSendsNoDecide() {
        LoadedDeck needles = DisruptionTest.needles();
        TestGames.Recorder r = new TestGames.Recorder(InteractiveDisruptionTest.counterEveryCast(NEEDLE));
        Messages.GameOver over = TestGames.play(start(needles, 7, 3), needles, r);

        assertEquals("horizon", over.reason(), over.error());
        assertEquals("hit", opposed(r).get(0).outcome());
        assertTrue(r.all(Messages.DecideRespond.class).isEmpty());
    }

    /** Mana Leak through the real window: the opponent pays {3}, so its Counterspell still counters our Needle. */
    @Test
    void theOpponentPaysManaLeakThroughTheWindow() {
        LoadedDeck deck = deckWith("Mana Leak");
        TestGames.Recorder r = new TestGames.Recorder(opposeWhenHeld("Mana Leak", "Mana Leak"));
        Messages.GameOver over = TestGames.play(start(deck, 11, 6), deck, r);

        assertEquals("horizon", over.reason(), over.error());
        assertFalse(opposed(r).isEmpty(), "seed 11 never opposes a Needle with Mana Leak up: pick another seed, keep the assertions");
        assertTrue(logged(over, "RESPOND Mana Leak"), over.log().toString());
        assertTrue(over.log().stream().anyMatch(e -> e.message().contains("casts Mana Leak") && e.message().contains(COUNTERSPELL)),
                "Mana Leak was not cast at the Counterspell " + over.log());
        Messages.Opposed hit = opposed(r).get(0);
        assertEquals("hit", hit.outcome(), "the opponent did not pay Mana Leak " + over.log());
        assertEquals(List.of(NEEDLE), hit.lost());
        assertTrue(logged(over, "COUNTERED " + NEEDLE));
    }

    /**
     * Delay through the real window counters the opponent's Counterspell and exiles it with suspend. Before the
     * settle, the suspend cast (SuspendPlayCardEffect casts it without paying its cost) meets the cast guard, which
     * refuses it. The settle then takes the card out of exile, and the opponent never casts it again.
     */
    @Test
    void theCastGuardRefusesTheRecastOfACounterspellThatDelayExiled() {
        LoadedDeck deck = deckWith("Delay");
        boolean[] refused = {false};
        Lab.Hook recast = new Lab.Hook(seat -> {
            GameCtx.PendingDisruption pending = seat.ctx().pendingDisruption;
            return pending != null && seat.game().getState().getZone(pending.cardId) == Zone.EXILED;
        }, seat -> {
            Card exiled = seat.game().getCard(seat.ctx().pendingDisruption.cardId);
            assertTrue(exiled.getCounters(seat.game()).getCount(CounterType.TIME) > 0, "Delay put no time counter on the card");
            refused[0] = !seat.opponent().cast(exiled.getSpellAbility(), seat.game(), true, null);
            assertEquals(Zone.EXILED, seat.game().getState().getZone(exiled.getId()));
        });
        Lab.Seat[] later = {null};
        TestGames.Recorder r = new TestGames.Recorder(opposeWhenHeld("Delay", "Delay"));
        Messages.GameOver over = Lab.play(start(deck, 11, 8), deck, r, recast, seatAt(8, later));

        assertEquals("horizon", over.reason(), over.error());
        assertFalse(opposed(r).isEmpty(), "seed 11 never opposes a Needle with Delay up: pick another seed, keep the assertions");
        assertTrue(logged(over, "RESPOND Delay"), over.log().toString());
        assertEquals("answered", opposed(r).get(0).outcome());
        assertTrue(refused[0], "the opponent recast the Counterspell that Delay exiled");
        assertEquals(1, over.log().stream().filter(e -> e.message().contains("casts Counterspell")).count(),
                "the opponent cast Counterspell again");
        assertNotNull(later[0]);
        assertOpponentCardInNoZone(later[0], COUNTERSPELL);
    }

    /**
     * The stale-spell regression: a failed response restores the game, which replaces the stack's objects with
     * copies. The shim re-binds its pending spells after the restore, so the opponent's Counterspell still reads as
     * countering our Needle.
     */
    @Test
    void aFailedResponseOnANonEmptyStackKeepsTheCounteredResult() {
        LoadedDeck deck = deckWith(COUNTERSPELL);
        InteractiveDisruptionTest.Opposer opposer = opposeWhenHeld(COUNTERSPELL, COUNTERSPELL);
        int[] asked = {0};
        // The first respond decide takes Counterspell, which fails; the retry decide passes.
        TestGames.Recorder r = new TestGames.Recorder(m -> m instanceof Messages.DecideRespond && asked[0]++ > 0
                ? Messages.Reply.ofAction(m.seq(), null) : opposer.ask(m));
        Lab.Refusal refusal = new Lab.Refusal(a -> a instanceof SpellAbility s && COUNTERSPELL.equals(s.getCardName()));
        Messages.GameOver over = Lab.play(start(deck, 11, 6), deck, r, refusal);

        assertEquals("horizon", over.reason(), over.error());
        assertTrue(logged(over, "RESPOND cannot Counterspell"), over.log().toString());
        Messages.Opposed hit = opposed(r).get(0);
        assertEquals("hit", hit.outcome(), over.log().toString());
        assertTrue(hit.victims().get(0).hit());
        assertEquals(List.of(NEEDLE), hit.lost());
        int failed = indexOf(over, "RESPOND cannot Counterspell");
        List<String> after = over.log().subList(failed, over.log().size()).stream().map(Messages.LogEntry::message).toList();
        assertTrue(after.contains("COUNTERED " + NEEDLE), after.toString());
        assertTrue(after.indexOf("COUNTERED " + NEEDLE) < after.indexOf("OPPOSED Counterspell hit"));
    }

    /**
     * A failed response with no action left still gets one more respond decide, with no action, so the service's
     * last reply is a pass and never names the card that failed. Every Counterspell cast fails here.
     */
    @Test
    void aFailedResponseWithNoActionLeftEndsWithAnEmptyDecide() {
        LoadedDeck deck = deckWith(COUNTERSPELL);
        InteractiveDisruptionTest.Opposer opposer = opposeWhenHeld(COUNTERSPELL, COUNTERSPELL);
        TestGames.Recorder r = new TestGames.Recorder(m -> m instanceof Messages.DecideRespond d
                ? Messages.Reply.ofAction(m.seq(), d.view().actions().stream()
                        .filter(a -> a.card().equals(COUNTERSPELL)).map(Messages.WireAction::id).findFirst().orElse(null))
                : opposer.ask(m));
        Lab.Refusal refusal = Lab.Refusal.every(a -> a instanceof SpellAbility s && COUNTERSPELL.equals(s.getCardName()));
        Messages.GameOver over = Lab.play(start(deck, 11, 6), deck, r, refusal);

        assertEquals("horizon", over.reason(), over.error());
        assertTrue(logged(over, "RESPOND cannot Counterspell"), over.log().toString());
        Messages.Opposed first = opposed(r).get(0);
        List<Messages.DecideRespond> window = r.all(Messages.DecideRespond.class).stream()
                .filter(d -> r.messages.indexOf(d) < r.messages.indexOf(first)).toList();
        assertTrue(window.size() >= 2, "no decide followed the failed response");
        assertEquals(List.of(), window.get(window.size() - 1).view().actions());
        assertEquals("hit", first.outcome(), over.log().toString());
    }

    /**
     * Respond decides count toward the per-game decision cap: a cap one below the respond decide's number ends the
     * game before it goes out. The opponent's priorities do not count: a cap of exactly the game's messages plays it
     * through.
     */
    @Test
    void respondDecidesCountTowardTheDecisionCap() {
        LoadedDeck deck = deckWith(COUNTERSPELL);
        TestGames.Recorder full = new TestGames.Recorder(opposeWhenHeld(COUNTERSPELL, COUNTERSPELL));
        Messages.GameOver played = TestGames.play(start(deck, 11, 6), deck, full);
        int respondAt = full.messages.indexOf(full.all(Messages.DecideRespond.class).get(0));

        TestGames.Recorder capped = new TestGames.Recorder(opposeWhenHeld(COUNTERSPELL, COUNTERSPELL));
        Messages.GameOver over = TestGames.play(new GameCtx(start(deck, 11, 6), deck, capped, respondAt, GameCtx.MAX_PRIORITIES));
        assertEquals("engine_error", over.reason());
        assertEquals("game exceeded " + respondAt + " decisions", over.error());
        assertTrue(capped.all(Messages.DecideRespond.class).isEmpty());

        TestGames.Recorder exact = new TestGames.Recorder(opposeWhenHeld(COUNTERSPELL, COUNTERSPELL));
        Messages.GameOver through = TestGames.play(new GameCtx(start(deck, 11, 6), deck, exact, full.messages.size(), GameCtx.MAX_PRIORITIES));
        assertNull(through.error());
        assertEquals(played.digest(), through.digest());
    }

    @Test
    void theSameSeedGivesTheSameDigest() {
        LoadedDeck deck = deckWith(COUNTERSPELL);
        Messages.GameOver a = TestGames.play(start(deck, 11, 6), deck, opposeWhenHeld(COUNTERSPELL, COUNTERSPELL));
        Messages.GameOver b = TestGames.play(start(deck, 11, 6), deck, opposeWhenHeld(COUNTERSPELL, COUNTERSPELL));

        assertNull(a.error());
        assertEquals(a.digest(), b.digest());
        assertEquals(a.log(), b.log());
    }

    /** The self-test responders: FIRST answers a respond decide with the first action, PASS passes. */
    @Test
    void theSelfTestRespondersAnswerTheWindow() {
        Messages.WireAction first = new Messages.WireAction("a:1", "cast", COUNTERSPELL, "front", "hand", "{U}{U}", null, false);
        Messages.WireAction second = new Messages.WireAction("a:2", "cast", "Mana Leak", "front", "hand", "{1}{U}", null, false);
        Messages.View view = new Messages.View(3, "main", 40, true, "", List.of(COUNTERSPELL, "Mana Leak"), List.of(), List.of(),
                List.of(), List.of(), 90, List.of(), List.of(), List.of(), Map.of(), List.of(first, second));
        Messages.DecideRespond respond = new Messages.DecideRespond("decide", 4, "respond", view, new Messages.Respond(
                new Messages.Threat("s:" + UUID.randomUUID(), COUNTERSPELL), List.of()));

        assertEquals("a:1", new InteractiveResponder().ask(respond).action());
        assertEquals("a:1", new ScriptedResponder(ScriptedResponder.Mode.FIRST).ask(respond).action());
        assertNull(new ScriptedResponder(ScriptedResponder.Mode.PASS).ask(respond).action());
        assertEquals(4, new ScriptedResponder(ScriptedResponder.Mode.PASS).ask(respond).seq());
    }
}
