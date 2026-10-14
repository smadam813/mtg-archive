package mtgarchive.xmage;

import mage.abilities.ActivatedAbility;
import mage.abilities.SpellAbility;
import mage.cards.Card;
import mage.cards.repository.CardInfo;
import mage.constants.Zone;
import mage.game.Game;
import mage.game.PutToBattlefieldInfo;
import mage.game.stack.Spell;
import mage.game.stack.StackObject;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import java.util.function.Function;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The scripted opponent casts Counterspell (ADR 0013). The first tests prove the upstream XMage
 * behaviors the cast rests on: the pay-on-demand window pays the cost, ward and Mana Leak; the "can't be countered"
 * check changes nothing; a one-card cheat and a restore leave no card; the cast inside our priority holds up under
 * Grand Abolisher, split second and Teferi, Time Raveler. The rest drive the shim's oppose and opposed messages.
 */
class InteractiveDisruptionTest {

    static final String COUNTERSPELL = "Counterspell";

    static Messages.StartGame start(LoadedDeck deck, long seed, int horizon) {
        return DisruptionTest.start(deck, seed, horizon, true).withOpponent(ScriptedOpponentTest.TABLE);
    }

    /**
     * Casts the first card of {@code cast} that is listed (in that order), else plays the first land. While armed,
     * it answers each cast event that {@code oppose} maps to targets with oppose {@code card}; an opposed event that
     * is not not_cast disarms it, as the service's schedule does. Every other message passes or continues.
     */
    static final class Opposer implements Decider {

        final List<String> cast;
        final String card;
        final Function<Messages.EventCast, List<String>> oppose;
        /** The respond answer: the first listed action of this card, or a pass when null or not listed. */
        final String respondWith;
        boolean armed = true;

        Opposer(List<String> cast, String card, Function<Messages.EventCast, List<String>> oppose) {
            this(cast, card, oppose, null);
        }

        Opposer(List<String> cast, String card, Function<Messages.EventCast, List<String>> oppose, String respondWith) {
            this.cast = cast;
            this.card = card;
            this.oppose = oppose;
            this.respondWith = respondWith;
        }

        @Override
        public Messages.Reply ask(Messages.InGame m) {
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
            if (m instanceof Messages.EventCast c && armed) {
                List<String> targets = oppose.apply(c);
                if (targets != null) {
                    return Messages.Reply.ofOppose(m.seq(), card, targets);
                }
            }
            if (m instanceof Messages.Opposed o && !"not_cast".equals(o.outcome())) {
                armed = false;
            }
            return TestGames.pass().ask(m);
        }
    }

    /** Opposes every cast at the cast spell. */
    static Opposer counterEveryCast(String... cast) {
        return new Opposer(List.of(cast), COUNTERSPELL, c -> List.of(c.spell().id()));
    }

    static List<Messages.Opposed> opposed(TestGames.Recorder r) {
        return r.all(Messages.Opposed.class);
    }

    static boolean logged(Messages.GameOver over, String message) {
        return over.log().stream().anyMatch(e -> e.message().equals(message));
    }

    static long countLogged(Messages.GameOver over, String prefix) {
        return over.log().stream().filter(e -> e.message().startsWith(prefix)).count();
    }

    /** Every card of this name the game knows is in no zone. */
    static void assertInNoZone(Game game, String name) {
        for (Card card : game.getCards()) {
            if (card.getName().equals(name)) {
                Zone zone = game.getState().getZone(card.getId());
                assertTrue(zone == null || zone == Zone.OUTSIDE, name + " is in " + zone);
            }
        }
    }

    static Card card(String name) {
        return ScenarioRunner.card(name);
    }

    /** Puts {@code names} onto our battlefield (GameImpl.cheat, as XMage's own tests do). */
    static void cheatOntoBattlefield(Lab.Seat seat, String... names) {
        List<PutToBattlefieldInfo> battlefield = new ArrayList<>();
        for (String name : names) {
            battlefield.add(new PutToBattlefieldInfo(card(name), false));
        }
        seat.game().cheat(seat.me().getId(), List.of(), List.of(), battlefield, List.of(), List.of(), List.of());
    }

    static boolean ourMainPhase(Lab.Seat seat, int turn) {
        return seat.game().isActivePlayer(seat.me().getId()) && seat.game().getStack().isEmpty()
                && seat.game().getTurnNum() >= turn && seat.game().getTurnStepType() == mage.constants.PhaseStep.PRECOMBAT_MAIN;
    }

    /** The opponent's disruption card on top of the stack; null otherwise. */
    static StackObject opponentCardOnTop(Lab.Seat seat) {
        if (seat.game().getStack().isEmpty() || seat.opponent() == null) {
            return null;
        }
        StackObject top = seat.game().getStack().getFirst();
        return top.isControlledBy(seat.opponent().getId()) ? top : null;
    }

    /** Casts the first card in hand named {@code name} at {@code target}, from the seat's playable list. */
    static void castFromHand(Lab.Seat seat, String name, UUID target) {
        for (ActivatedAbility a : seat.me().getPlayable(seat.game(), true)) {
            Card source = seat.game().getCard(a.getSourceId());
            if (a instanceof SpellAbility && source != null && source.getName().equals(name)
                    && seat.game().getState().getZone(source.getId()) == Zone.HAND) {
                ActivatedAbility cast = a.copy();
                cast.getTargets().get(0).add(target, seat.game()); // a chosen target: XMage asks for no other
                assertTrue(seat.me().activateAbility(cast, seat.game()), "XMage refused to cast " + name);
                return;
            }
        }
        throw new AssertionError(name + " is not castable now");
    }

    // ------------------------------------------------------------ upstream proofs

    /** The opponent has no land: the window adds {U}{U} and Counterspell counters our Needle. */
    @Test
    void proofTheWindowPaysTheCost() {
        LoadedDeck needles = DisruptionTest.needles();
        TestGames.Recorder r = new TestGames.Recorder(counterEveryCast("Pithing Needle"));
        Messages.GameOver over = TestGames.play(start(needles, 7, 3), needles, r);

        assertEquals("horizon", over.reason(), over.error());
        Messages.Opposed first = opposed(r).get(0);
        assertEquals("hit", first.outcome());
        assertTrue(over.log().stream().anyMatch(e -> e.message().contains("casts Counterspell")), "no Counterspell cast in the log");
    }

    /**
     * Ward: Atomize at Wilson, Refined Grizzly (ward {2}). The ward trigger counters Atomize unless the opponent pays
     * {2}; the window pays it, so Atomize resolves and destroys Wilson. Without the payment the outcome is answered.
     */
    @Test
    void proofTheWindowPaysWard() {
        LoadedDeck wilsons = DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Forest", 60),
                new Messages.DeckEntry("Wilson, Refined Grizzly", 39));
        TestGames.Recorder r = new TestGames.Recorder(new Opposer(List.of("Wilson, Refined Grizzly"), "Atomize", c -> {
            List<String> ids = c.view().battlefield().stream().filter(p -> p.card().equals("Wilson, Refined Grizzly"))
                    .map(Messages.ViewPermanent::id).toList();
            return ids.isEmpty() ? null : List.of(ids.get(0));
        }));
        Messages.GameOver over = TestGames.play(start(wilsons, 7, 6), wilsons, r);

        assertEquals("horizon", over.reason(), over.error());
        assertFalse(opposed(r).isEmpty(), "seed 7 never casts a second Wilson: pick another seed, keep the assertions");
        Messages.Opposed atomize = opposed(r).get(0);
        assertEquals("Atomize", atomize.card());
        assertEquals("hit", atomize.outcome(), "the opponent did not pay ward " + over.log());
        assertTrue(atomize.victims().get(0).hit());
        assertTrue(atomize.lost().contains("Wilson, Refined Grizzly"));
    }

    /**
     * Mana Leak: our seat casts Mana Leak at the opponent's Counterspell. The window pays {3}, so Counterspell still
     * counters our Needle.
     */
    @Test
    void proofTheWindowPaysManaLeak() {
        LoadedDeck leaks = DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Island", 60),
                new Messages.DeckEntry("Pithing Needle", 20), new Messages.DeckEntry("Mana Leak", 19));
        boolean[] leaked = {false};
        Lab.Hook leak = new Lab.Hook(seat -> {
            StackObject top = opponentCardOnTop(seat);
            return top != null && top.getName().equals(COUNTERSPELL)
                    && seat.me().getHand().getCards(seat.game()).stream().anyMatch(c -> c.getName().equals("Mana Leak"))
                    && untappedLands(seat) >= 2;
        }, seat -> {
            castFromHand(seat, "Mana Leak", opponentCardOnTop(seat).getId());
            leaked[0] = true;
        });
        // Oppose only a Needle cast that leaves two Islands up, with Mana Leak in hand.
        TestGames.Recorder r = new TestGames.Recorder(new Opposer(List.of("Pithing Needle"), COUNTERSPELL, c ->
                c.view().hand().contains("Mana Leak") && c.view().battlefield().stream()
                        .filter(p -> p.card().equals("Island") && !p.tapped()).count() >= 2 ? List.of(c.spell().id()) : null));
        Messages.GameOver over = Lab.play(start(leaks, 11, 6), leaks, r, leak);

        assertEquals("horizon", over.reason(), over.error());
        assertTrue(leaked[0], "seed 11 never opposes a Needle with Mana Leak up: pick another seed, keep the assertions");
        assertTrue(over.log().stream().anyMatch(e -> e.message().contains("casts Mana Leak") && e.message().contains("Counterspell")),
                "Mana Leak was not cast at the Counterspell " + over.log());
        assertEquals("hit", opposed(r).get(0).outcome(), "the opponent did not pay Mana Leak " + over.log());
    }

    static long untappedLands(Lab.Seat seat) {
        return seat.game().getBattlefield().getAllActivePermanents(seat.me().getId()).stream()
                .filter(p -> p.isLand(seat.game()) && !p.isTapped()).count();
    }

    /**
     * The "can't be countered" check is silent: a game that runs it on Great Sable Stag gives the digest of the
     * same game without it. It is true for the Stag and false for a Pithing Needle.
     */
    @Test
    void proofTheCantBeCounteredCheckHasNoSideEffects() {
        LoadedDeck stags = DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Forest", 50),
                new Messages.DeckEntry("Great Sable Stag", 25), new Messages.DeckEntry("Pithing Needle", 24));
        // The Counterspell's ability comes from a card made on this thread, outside the game's seeded id stream.
        SpellAbility counterspell = card(COUNTERSPELL).getSpellAbility();
        Decider plain = new Opposer(List.of("Great Sable Stag", "Pithing Needle"), COUNTERSPELL, c -> null);
        java.util.Map<String, List<Boolean>> checks = new java.util.TreeMap<>();
        Function<String, Lab.Hook> check = name -> new Lab.Hook(seat -> topSpell(seat, name) != null, seat -> {
            Spell spell = topSpell(seat, name);
            int lines = seat.ctx().log().size();
            List<Boolean> results = checks.computeIfAbsent(name, k -> new ArrayList<>());
            results.add(Opposition.cantBeCountered(seat.game(), counterspell, spell));
            results.add(Opposition.cantBeCountered(seat.game(), counterspell, spell));
            assertEquals(lines, seat.ctx().log().size(), "the check wrote to the log");
            assertFalse(spell.isCountered());
        });

        Messages.GameOver without = Lab.play(start(stags, 7, 5), stags, plain);
        Messages.GameOver with = Lab.play(start(stags, 7, 5), stags,
                new Opposer(List.of("Great Sable Stag", "Pithing Needle"), COUNTERSPELL, c -> null),
                check.apply("Great Sable Stag"), check.apply("Pithing Needle"));

        assertEquals("horizon", with.reason(), with.error());
        assertEquals(java.util.Map.of("Great Sable Stag", List.of(true, true), "Pithing Needle", List.of(false, false)), checks,
                "seed 7 must cast a Stag and a Needle in five turns: pick another seed, keep the assertions");
        assertEquals(without.digest(), with.digest());
    }

    static Spell topSpell(Lab.Seat seat, String name) {
        Spell spell = seat.game().getStack().isEmpty() ? null : seat.game().getStack().getSpell(seat.game().getStack().getFirst().getId());
        return spell != null && spell.getName().equals(name) && spell.isControlledBy(seat.me().getId()) ? spell : null;
    }

    /** A one-card cheat and a restore leave no card behind: the attempt with no candidate restores the game. */
    @Test
    void proofACheatAndARestoreLeaveNoCard() {
        LoadedDeck needles = DisruptionTest.needles();
        CardInfo info = OpponentCards.resolve(List.of(COUNTERSPELL)).get(0);
        List<Card> made = new ArrayList<>();
        Lab.Hook attempt = new Lab.Hook(seat -> ourMainPhase(seat, 2), seat -> {
            int hand = seat.opponent().getHand().size();
            Opposition.Attempt a = Opposition.attempt(seat.game(), seat.opponent(), info, List.of());
            made.add(a.card());
            assertEquals(Opposition.NO_TARGET, a.reason());
            assertNull(seat.game().getState().getZone(a.card().getId()), "the card is still in a zone");
            assertEquals(hand, seat.opponent().getHand().size());
            assertFalse(seat.opponent().getHand().contains(a.card().getId()));
            assertNull(seat.opponent().window().card);
        });
        Messages.GameOver over = Lab.play(start(needles, 7, 3), needles, TestGames.pass(), attempt);

        assertEquals("horizon", over.reason(), over.error());
        assertEquals(1, made.size());
    }

    /** Grand Abolisher: our opponents cannot cast spells during our turn, so the cast fails and restores. */
    @Test
    void proofALockGrandAbolisherGivesCannotCast() {
        assertCannotCastUnder("Grand Abolisher");
    }

    /** Teferi, Time Raveler: each opponent casts only at sorcery speed, so the cast fails and restores. */
    @Test
    void proofALockTeferiGivesCannotCast() {
        assertCannotCastUnder("Teferi, Time Raveler");
    }

    private static void assertCannotCastUnder(String lock) {
        LoadedDeck needles = DisruptionTest.needles();
        Lab.Hook cheat = new Lab.Hook(seat -> seat.game().getTurnNum() == 1 && seat.game().getStack().isEmpty(),
                seat -> cheatOntoBattlefield(seat, lock));
        Game[] seen = {null};
        Lab.Hook after = new Lab.Hook(seat -> ourMainPhase(seat, 3), seat -> seen[0] = seat.game());
        TestGames.Recorder r = new TestGames.Recorder(counterEveryCast("Pithing Needle"));
        Messages.GameOver over = Lab.play(start(needles, 7, 3), needles, r, cheat, after);

        assertEquals("horizon", over.reason(), over.error());
        Messages.Opposed first = opposed(r).get(0);
        assertEquals("not_cast", first.outcome());
        assertEquals(Opposition.CANNOT_CAST, ((Messages.EventNotCast) first).reason());
        assertEquals(1, first.victims().size());
        assertFalse(first.victims().get(0).hit());
        assertTrue(logged(over, "RESOLVED Pithing Needle"), "the Needle did not resolve after the restore");
        assertNotNull(seen[0]);
        assertInNoZone(seen[0], COUNTERSPELL);
    }

    /** Split second: Sudden Shock on the stack stops every cast, so the opponent's cast fails and restores. */
    @Test
    void proofALockSplitSecondGivesCannotCast() {
        LoadedDeck shocks = DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Mountain", 60),
                new Messages.DeckEntry("Sudden Shock", 39));
        TestGames.Recorder r = new TestGames.Recorder(counterEveryCast("Sudden Shock"));
        Messages.GameOver over = TestGames.play(start(shocks, 7, 3), shocks, r);

        assertEquals("horizon", over.reason(), over.error());
        Messages.Opposed first = opposed(r).get(0);
        assertEquals("not_cast", first.outcome());
        assertEquals(Opposition.CANNOT_CAST, ((Messages.EventNotCast) first).reason());
        assertTrue(logged(over, "RESOLVED Sudden Shock"));
    }

    // ------------------------------------------------------------ the oppose and opposed messages

    @Test
    void theOpponentsCounterspellCountersAGoalSpell() {
        LoadedDeck needles = DisruptionTest.needles();
        Game[] seen = {null};
        Lab.Hook after = new Lab.Hook(seat -> ourMainPhase(seat, 3), seat -> seen[0] = seat.game());
        TestGames.Recorder r = new TestGames.Recorder(counterEveryCast("Pithing Needle"));
        Messages.GameOver over = Lab.play(start(needles, 7, 3), needles, r, after);

        assertEquals("horizon", over.reason(), over.error());
        Messages.EventCast cast = r.all(Messages.EventCast.class).get(0);
        assertEquals(1, opposed(r).size(), "one counterspell event fires once");
        Messages.Opposed hit = opposed(r).get(0);
        assertInstanceOfOpposed(hit);
        assertEquals(COUNTERSPELL, hit.card());
        assertEquals("hit", hit.outcome());
        assertEquals(List.of(new Messages.Victim(cast.spell().id(), "Pithing Needle", "front", true)), hit.victims());
        assertEquals(List.of("Pithing Needle"), hit.lost());
        assertTrue(((Messages.EventOpposed) hit).view().actions().isEmpty(), "an event view lists no actions");
        // The opposed event follows the cast: no decision comes between them but the opponent's priority.
        int castAt = r.messages.indexOf(cast);
        assertTrue(r.messages.indexOf(hit) > castAt);
        assertTrue(logged(over, "OPPOSE Counterspell on Pithing Needle"));
        assertTrue(logged(over, "COUNTERED Pithing Needle"));
        assertTrue(logged(over, "OPPOSED Counterspell hit"));
        assertTrue(logged(over, "RESOLVED Pithing Needle"), "a later Needle resolves: the event fired once");
        assertTrue(DisruptionTest.endOfTurn(r, cast.view().turn()).graveyard().contains("Pithing Needle"));
        assertInNoZone(seen[0], COUNTERSPELL);
    }

    private static void assertInstanceOfOpposed(Messages.Opposed opposed) {
        assertTrue(opposed instanceof Messages.EventOpposed, "a hit carries no reason");
        assertEquals("opposed", opposed.kind());
    }

    @Test
    void aCounteredCommanderGoesHomeAndItsTaxRises() {
        LoadedDeck islands = DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Island", 99));
        TestGames.Recorder r = new TestGames.Recorder(counterEveryCast("Talrand, Sky Summoner"));
        Messages.GameOver over = TestGames.play(start(islands, 7, 6), islands, r);

        assertEquals("horizon", over.reason(), over.error());
        Messages.EventCast countered = r.all(Messages.EventCast.class).get(0);
        assertEquals(4, countered.view().turn());
        assertEquals("hit", opposed(r).get(0).outcome());
        assertEquals(List.of("Talrand, Sky Summoner"), DisruptionTest.endOfTurn(r, 4).commandZone());
        assertTrue(r.all(Messages.DecideAction.class).stream().flatMap(d -> d.view().actions().stream())
                        .anyMatch(a -> a.kind().equals("cast_commander") && a.cost().equals("{4}{U}{U}")),
                "the commander tax did not rise after the counter");
    }

    /** Great Sable Stag can't be countered: Counterspell has no target, so it is not cast, and the event stays armed. */
    @Test
    void greatSableStagGivesNotCastAndTheEventStaysArmed() {
        LoadedDeck stags = DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Forest", 60),
                new Messages.DeckEntry("Great Sable Stag", 39));
        TestGames.Recorder r = new TestGames.Recorder(counterEveryCast("Great Sable Stag"));
        Messages.GameOver over = TestGames.play(start(stags, 7, 6), stags, r);

        assertEquals("horizon", over.reason(), over.error());
        List<Messages.Opposed> all = opposed(r);
        assertTrue(all.size() >= 2, "seed 7 casts fewer than two Stags: pick another seed, keep the assertions");
        for (Messages.Opposed o : all) {
            assertEquals("not_cast", o.outcome());
            assertEquals(Opposition.NO_TARGET, ((Messages.EventNotCast) o).reason());
            assertEquals(List.of(), o.victims());
            assertEquals(List.of(), o.lost());
        }
        assertEquals(r.all(Messages.EventCast.class).size(), all.size(), "each cast got its not_cast at once");
        assertEquals(all.size(), countLogged(over, "RESOLVED Great Sable Stag"));
        assertFalse(over.log().stream().anyMatch(e -> e.message().contains("casts Counterspell")));
    }

    /** The cast guard: the opponent refuses a disruption card cast outside the current proposal. */
    @Test
    void theCastGuardRefusesACastOutsideTheProposal() {
        LoadedDeck needles = DisruptionTest.needles();
        boolean[] refused = {false};
        Lab.Hook recast = new Lab.Hook(seat -> ourMainPhase(seat, 2), seat -> {
            Card counterspell = card(COUNTERSPELL);
            seat.game().loadCards(java.util.Set.of(counterspell), seat.opponent().getId());
            counterspell.setZone(Zone.HAND, seat.game());
            seat.opponent().getHand().add(counterspell);
            refused[0] = !seat.opponent().cast(counterspell.getSpellAbility(), seat.game(), true, null);
            assertEquals(Zone.HAND, seat.game().getState().getZone(counterspell.getId()));
            assertTrue(seat.game().getStack().isEmpty());
            Opposition.remove(seat.game(), seat.opponent(), counterspell.getId());
        });
        Messages.GameOver over = Lab.play(start(needles, 7, 2), needles, TestGames.pass(), recast);

        assertNull(over.error());
        assertTrue(refused[0], "the opponent cast a disruption card outside a proposal");
    }

    /**
     * Delay counters the opponent's Counterspell and exiles it with suspend. The settle takes the card out of exile,
     * so it is in no zone, and the opponent never casts it again.
     */
    @Test
    void aDelayedCounterspellIsInNoZoneAndNeverRecast() {
        LoadedDeck delays = DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Island", 60),
                new Messages.DeckEntry("Pithing Needle", 20), new Messages.DeckEntry("Delay", 19));
        Lab.Hook delay = new Lab.Hook(seat -> {
            StackObject top = opponentCardOnTop(seat);
            return top != null && seat.me().getHand().getCards(seat.game()).stream().anyMatch(c -> c.getName().equals("Delay"))
                    && untappedLands(seat) >= 2;
        }, seat -> castFromHand(seat, "Delay", opponentCardOnTop(seat).getId()));
        Game[] seen = {null};
        Lab.Hook later = new Lab.Hook(seat -> ourMainPhase(seat, 8), seat -> seen[0] = seat.game());
        TestGames.Recorder r = new TestGames.Recorder(new Opposer(List.of("Pithing Needle"), COUNTERSPELL, c ->
                c.view().hand().contains("Delay") && c.view().battlefield().stream()
                        .filter(p -> p.card().equals("Island") && !p.tapped()).count() >= 2 ? List.of(c.spell().id()) : null));
        Messages.GameOver over = Lab.play(start(delays, 11, 8), delays, r, delay, later);

        assertEquals("horizon", over.reason(), over.error());
        assertFalse(opposed(r).isEmpty(), "seed 11 never opposes a Needle with Delay up: pick another seed, keep the assertions");
        assertTrue(over.log().stream().anyMatch(e -> e.message().contains("casts Delay") && e.message().contains("Counterspell")),
                "Delay was not cast at the Counterspell " + over.log());
        assertEquals("answered", opposed(r).get(0).outcome());
        assertFalse(opposed(r).get(0).victims().get(0).hit());
        assertEquals(1, over.log().stream().filter(e -> e.message().contains("casts Counterspell")).count(),
                "the opponent cast Counterspell again");
        assertInNoZone(seen[0], COUNTERSPELL);
    }

    @Test
    void theSameSeedGivesTheSameDigest() {
        LoadedDeck needles = DisruptionTest.needles();
        Messages.GameOver a = TestGames.play(start(needles, 9, 4), needles, counterEveryCast("Pithing Needle"));
        Messages.GameOver b = TestGames.play(start(needles, 9, 4), needles, counterEveryCast("Pithing Needle"));

        assertNull(a.error());
        assertEquals(a.digest(), b.digest());
        assertEquals(a.log(), b.log());
        // and the counter changes the game: the scripted opponent with nothing due plays the idle opponent's game
        Messages.GameOver idle = TestGames.play(start(needles, 9, 4), needles, new Opposer(List.of("Pithing Needle"), COUNTERSPELL, c -> null));
        assertNotEquals(a.digest(), idle.digest());
    }

    // ------------------------------------------------------------ protocol

    @Test
    void withAnOpponentTheCastEventRefusesCounter() {
        LoadedDeck needles = DisruptionTest.needles();
        Messages.GameOver over = TestGames.play(start(needles, 7, 2), needles,
                DisruptionTest.caster("Pithing Needle", DisruptionTest.counterFirstCast()));

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().contains("the cast event takes do=continue, oppose, stop, got counter"), over.error());
    }

    @Test
    void withAnOpponentTheEndOfTurnRefusesDisrupt() {
        LoadedDeck needles = DisruptionTest.needles();
        Messages.GameOver over = TestGames.play(start(needles, 7, 3), needles,
                DisruptionTest.caster("Pithing Needle", DisruptionTest.removeFirst("Pithing Needle")));

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().contains("the end_of_turn event takes do=continue, oppose, stop, got disrupt"), over.error());
    }

    @Test
    void withoutAnOpponentTheCastEventRefusesOppose() {
        LoadedDeck needles = DisruptionTest.needles();
        Messages.GameOver over = TestGames.play(DisruptionTest.start(needles, 7, 2, true), needles, counterEveryCast("Pithing Needle"));

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().contains("the cast event takes do=continue, counter, stop, got oppose"), over.error());
    }

    @Test
    void theOpposedEventTakesContinueOrStop() {
        LoadedDeck needles = DisruptionTest.needles();
        Opposer opposer = counterEveryCast("Pithing Needle");
        Messages.GameOver over = TestGames.play(start(needles, 7, 2), needles, m -> m instanceof Messages.Opposed
                ? Messages.Reply.ofOppose(m.seq(), COUNTERSPELL, List.of()) : opposer.ask(m));

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().contains("the opposed event takes do=continue, stop, got oppose"), over.error());
    }

    @Test
    void opposeNamesACardOfTheOpponent() {
        LoadedDeck needles = DisruptionTest.needles();
        Messages.GameOver over = TestGames.play(start(needles, 7, 2), needles,
                new Opposer(List.of("Pithing Needle"), "Lightning Bolt", c -> List.of(c.spell().id())));

        assertEquals("engine_error", over.reason());
        assertEquals("bad reply: oppose names Lightning Bolt, which is not a card of start_game.opponent", over.error());
    }

    @Test
    void opposeTargetsWireIds() {
        LoadedDeck needles = DisruptionTest.needles();
        Messages.GameOver over = TestGames.play(start(needles, 7, 2), needles,
                new Opposer(List.of("Pithing Needle"), COUNTERSPELL, c -> List.of("x:" + UUID.randomUUID())));

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().contains("which is not an s: or p: id"), over.error());
    }

    @Test
    void cardAndTargetsGoWithOpposeOnly() {
        LoadedDeck needles = DisruptionTest.needles();
        Messages.GameOver over = TestGames.play(start(needles, 7, 2), needles,
                DisruptionTest.caster("Pithing Needle", m -> new Messages.Reply(m.seq(), null, null, null, null, "continue", null,
                        COUNTERSPELL, null)));

        assertEquals("engine_error", over.reason());
        assertEquals("bad reply: card and targets go with do=oppose, and oppose names both", over.error());
    }
}
