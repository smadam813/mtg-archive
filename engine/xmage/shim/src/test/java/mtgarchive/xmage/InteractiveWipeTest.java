package mtgarchive.xmage;

import mage.abilities.common.PayMoreToCastAsThoughtItHadFlashAbility;
import mage.game.stack.Spell;
import mage.game.stack.StackObject;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.function.Function;

import static mtgarchive.xmage.InteractiveDisruptionTest.logged;
import static mtgarchive.xmage.InteractiveDisruptionTest.opponentCardOnTop;
import static mtgarchive.xmage.InteractiveDisruptionTest.opposed;
import static mtgarchive.xmage.InteractiveDisruptionTest.start;
import static mtgarchive.xmage.InteractiveRemovalTest.afterView;
import static mtgarchive.xmage.InteractiveRemovalTest.cheatOnTurnOne;
import static mtgarchive.xmage.InteractiveRemovalTest.endStep;
import static mtgarchive.xmage.InteractiveRemovalTest.islands;
import static mtgarchive.xmage.InteractiveRemovalTest.onBattlefield;
import static mtgarchive.xmage.InteractiveRemovalTest.untappedForests;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The scripted opponent casts Rout at our end step through its "pay {2} more to cast it as though it had
 * flash" ability (ADR 0013). Rout takes no target: the oppose reply has empty targets, its victims are our creatures
 * as it is cast, and its own text decides what dies. Team protection in the response window answers it.
 */
class InteractiveWipeTest {

    static final String ROUT = "Rout";

    static final String HEROIC_INTERVENTION = "Heroic Intervention";

    /** Opposes with Rout, no targets, at the first end of turn from {@code turn} on. */
    static Function<Messages.EventEndOfTurn, List<String>> routAt(int turn) {
        boolean[] done = {false};
        return e -> {
            if (done[0] || e.view().turn() < turn) {
                return null;
            }
            done[0] = true;
            return List.of();
        };
    }

    /**
     * Proves first that the opponent casts Rout through its flash ability, not its sorcery spell ability (PlayerImpl.cast
     * checks no timing, so the shim picks), and that it pays the {2} more: the spell on the stack has the flash ability,
     * and its seven mana are paid.
     */
    @Test
    void routIsCastThroughItsFlashAbilityAndTheOpponentPaysTheExtraCost() {
        LoadedDeck deck = islands();
        Object[] ability = {null};
        int[] manaValue = {-1};
        boolean[] paid = {false};
        Lab.Hook read = new Lab.Hook(seat -> opponentCardOnTop(seat) != null, seat -> {
            StackObject top = opponentCardOnTop(seat);
            Spell spell = (Spell) top;
            ability[0] = spell.getSpellAbility();
            manaValue[0] = spell.getSpellAbility().getManaCostsToPay().getMana().count();
            paid[0] = spell.getSpellAbility().getManaCostsToPay().isPaid();
        });
        TestGames.Recorder r = new TestGames.Recorder(endStep(ROUT, List.of(), null, routAt(1), o -> null));
        Messages.GameOver over = Lab.play(start(deck, 7, 2), deck, r, cheatOnTurnOne("Grizzly Bears"), read);

        assertEquals("horizon", over.reason(), over.error());
        assertInstanceOf(PayMoreToCastAsThoughtItHadFlashAbility.class, ability[0]);
        assertEquals(7, manaValue[0], "Rout through its flash ability costs {3}{W}{W} and {2} more");
        assertTrue(paid[0]);
        Messages.Opposed hit = opposed(r).get(0);
        assertEquals(ROUT, hit.card());
        assertEquals("hit", hit.outcome());
        assertEquals(List.of("Grizzly Bears"), hit.victims().stream().map(Messages.Victim::card).toList());
        assertTrue(logged(over, "OPPOSE Rout"));
        assertTrue(logged(over, "OPPOSED Rout hit"));
    }

    /**
     * Doomed Traveler dies to Rout, and its dies trigger resolves before the settle: the opposed event and the after
     * view show the Spirit token. A later Rout kills that token: it is a victim, it is hit, and it is gone.
     */
    @Test
    void routKillsATokenAndADiesTriggerResolvesBeforeTheAfterView() {
        LoadedDeck travelers = DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Plains", 60),
                new Messages.DeckEntry("Doomed Traveler", 39));
        int[] routs = {0};
        TestGames.Recorder r = new TestGames.Recorder(endStep(ROUT, List.of("Doomed Traveler"), null, e -> {
            boolean due = routs[0] == 0 ? onBattlefield(e.view(), "Doomed Traveler") : routs[0] == 1 && spirit(e.view()) != null;
            if (!due) {
                return null;
            }
            routs[0]++;
            return List.of();
        }, o -> null));
        Messages.GameOver over = TestGames.play(start(travelers, 7, 6), travelers, r);

        assertEquals("horizon", over.reason(), over.error());
        assertEquals(2, opposed(r).size(), "seed 7 never put a Spirit token in front of a second Rout: pick another seed");
        Messages.Opposed first = opposed(r).get(0);
        assertEquals("hit", first.outcome());
        assertTrue(first.lost().contains("Doomed Traveler"));
        Messages.View after = afterView(r, first).view();
        assertTrue(after.graveyard().contains("Doomed Traveler"));
        assertNotNull(spirit(after), "no Spirit token: the dies trigger did not resolve before the after view " + after.battlefield());

        Messages.Opposed second = opposed(r).get(1);
        Messages.Victim token = second.victims().stream().filter(v -> v.card().startsWith("Spirit")).findFirst()
                .orElseThrow(() -> new AssertionError("the Spirit token is no victim: " + second.victims()));
        assertEquals("hit", second.outcome());
        assertTrue(token.hit());
        assertFalse(afterView(r, second).view().battlefield().stream().anyMatch(p -> p.id().equals(token.id())),
                "the Spirit token survived Rout");
    }

    /**
     * Heroic Intervention in the response window: our permanents gain indestructible, so Rout destroys nothing. The
     * settle reports answered, every victim is not hit, nothing is lost, and the Bears stand on the after view.
     */
    @Test
    void heroicInterventionSavesTheCreatures() {
        LoadedDeck deck = DisruptionTest.deck("Talrand, Sky Summoner", new Messages.DeckEntry("Forest", 60),
                new Messages.DeckEntry(HEROIC_INTERVENTION, 39));
        boolean[] done = {false};
        TestGames.Recorder r = new TestGames.Recorder(endStep(ROUT, List.of(), HEROIC_INTERVENTION, e -> {
            if (done[0] || !e.view().hand().contains(HEROIC_INTERVENTION) || untappedForests(e.view()) < 2) {
                return null;
            }
            done[0] = true;
            return List.of();
        }, o -> null));
        Messages.GameOver over = Lab.play(start(deck, 7, 4), deck, r, cheatOnTurnOne("Grizzly Bears", "Grizzly Bears"));

        assertEquals("horizon", over.reason(), over.error());
        assertFalse(opposed(r).isEmpty(), "seed 7 never opposes with Heroic Intervention up: pick another seed, keep the assertions");
        Messages.Opposed answered = opposed(r).get(0);
        assertEquals(ROUT, answered.card());
        assertEquals("answered", answered.outcome(), over.log().toString());
        assertEquals(2, answered.victims().size());
        assertTrue(answered.victims().stream().noneMatch(Messages.Victim::hit));
        assertEquals(List.of(), answered.lost());
        assertTrue(logged(over, "RESPOND " + HEROIC_INTERVENTION));
        Messages.DecideRespond respond = r.all(Messages.DecideRespond.class).get(0);
        assertEquals(ROUT, respond.respond().threat().card());
        assertEquals(answered.victims().stream().map(Messages.Victim::id).toList(),
                respond.respond().victims().stream().map(Messages.RespondVictim::id).toList());
        Messages.View after = afterView(r, answered).view();
        assertEquals(2, after.battlefield().stream().filter(p -> p.card().equals("Grizzly Bears")).count());
        assertTrue(after.graveyard().contains(HEROIC_INTERVENTION));
    }

    /**
     * The self-test's interactive pass casts Rout from the end of turn 4, once, with zero engine errors. Rout takes
     * no target, so a game that reaches the end of turn 4 casts it.
     */
    @Test
    void theSelfTestResponderCastsRoutAtTheEndOfTurnFour() {
        LoadedDeck obeka = TestGames.obeka();
        List<Integer> turns = new ArrayList<>();
        for (long seed = 121; seed < 126; seed++) {
            TestGames.Recorder r = new TestGames.Recorder(new InteractiveResponder());
            // As SelfTest starts an interactive game: cast events on.
            Messages.StartGame start = new Messages.StartGame("start_game", obeka.deckId(), seed, 10, 40, true, true, 1, true, false);
            Messages.GameOver over = TestGames.play(start.withOpponent(SelfTest.TABLE), obeka, r);
            assertTrue(over.error() == null, over.error());
            List<Messages.Opposed> rout = opposed(r).stream().filter(o -> o.card().equals(ROUT)).toList();
            assertTrue(rout.stream().filter(o -> !o.outcome().equals("not_cast")).count() <= 1);
            rout.stream().filter(o -> !o.outcome().equals("not_cast")).forEach(o -> turns.add(o.view().turn()));
        }
        assertFalse(turns.isEmpty(), "no self-test game cast Rout in seeds 121 to 125");
        assertTrue(turns.stream().allMatch(t -> t == InteractiveResponder.WIPE_TURN), "Rout went on turns " + turns);
    }

    /** A Spirit token on our battlefield; null when there is none. */
    static Messages.ViewPermanent spirit(Messages.View view) {
        return view.battlefield().stream().filter(p -> p.card().startsWith("Spirit")).findFirst().orElse(null);
    }
}
