package mtgarchive.xmage;

import com.networknt.schema.JsonSchema;
import com.networknt.schema.ValidationMessage;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** Games in which the pilot takes the first listed action (ScriptedResponder FIRST), seeds 1 to 8. */
class PilotActionsTest {

    private static final List<TestGames.Recorder> GAMES = new ArrayList<>();
    private static final List<Messages.GameOver> OVERS = new ArrayList<>();

    @BeforeAll
    static void play() {
        for (long seed = 1; seed <= 8; seed++) {
            TestGames.Recorder r = new TestGames.Recorder(TestGames.first());
            OVERS.add(TestGames.play(seed, 10, r));
            GAMES.add(r);
        }
    }

    private static List<Messages.DecideAction> actionDecisions() {
        List<Messages.DecideAction> out = new ArrayList<>();
        GAMES.forEach(g -> out.addAll(g.all(Messages.DecideAction.class)));
        return out;
    }

    @Test
    void theSameSeedPlaysTheSameGameWhenThePilotActs() {
        assertEquals(OVERS.get(2).digest(), TestGames.play(3, 10, TestGames.first()).digest());
        OVERS.forEach(o -> assertNull(o.error()));
    }

    @Test
    void actionDecisionsComeOnlyInOurUpkeepMainAndEndStep() {
        for (Messages.DecideAction d : actionDecisions()) {
            assertTrue(Set.of("upkeep", "main", "end").contains(d.view().step()), d.view().step());
            assertFalse(d.view().actions().isEmpty());
        }
    }

    @Test
    void theActionListIsSortedAndItsIdsAreUnique() {
        for (Messages.DecideAction d : actionDecisions()) {
            List<Messages.WireAction> actions = d.view().actions();
            List<Messages.WireAction> sorted = new ArrayList<>(actions);
            sorted.sort(ActionList.ORDER);
            assertEquals(sorted, actions);
            assertEquals(actions.size(), new HashSet<>(actions.stream().map(Messages.WireAction::id).toList()).size());
            actions.forEach(a -> assertTrue(a.id().startsWith("a:"), a.id()));
        }
    }

    @Test
    void manaAbilitiesAreNeverListed() {
        for (Messages.DecideAction d : actionDecisions()) {
            for (Messages.WireAction a : d.view().actions()) {
                assertFalse(a.kind().equals("activate") && Set.of("Sol Ring", "Arcane Signet").contains(a.card()),
                        "listed a mana ability of " + a.card());
            }
        }
    }

    /** Ruling F3: the schema requires permanent_id on activate, so only battlefield activations are listed. */
    @Test
    void activateActionsNameTheirPermanentAndNoOtherActionDoes() {
        for (Messages.DecideAction d : actionDecisions()) {
            Set<String> permanents = new HashSet<>();
            d.view().battlefield().forEach(p -> permanents.add(p.id()));
            for (Messages.WireAction a : d.view().actions()) {
                if (a.kind().equals("activate")) {
                    assertEquals("battlefield", a.zone(), a.toString());
                    assertTrue(permanents.contains(a.permanentId()), a.toString());
                } else {
                    assertNull(a.permanentId(), a.toString());
                    assertFalse(a.spendsSource(), a.toString());
                }
            }
        }
    }

    @Test
    void everyListedActionMatchesTheSchemaActionRules() {
        JsonSchema schema = WireContract.definition("action");

        int checked = 0;
        for (Messages.DecideAction d : actionDecisions()) {
            for (Messages.WireAction a : d.view().actions()) {
                String text = Wire.GSON.toJson(a);
                Set<ValidationMessage> errors = WireContract.validate(schema, text);
                assertTrue(errors.isEmpty(), errors + " in " + text);
                checked++;
            }
        }
        assertTrue(checked > 0, "no action listed in eight games");
    }

    @Test
    void theViewNamesOnlyCardsWeHold() {
        boolean anyFinds = false;
        for (Messages.DecideAction d : actionDecisions()) {
            Messages.View v = d.view();
            Set<String> held = new HashSet<>(v.hand());
            held.addAll(v.commandZone());
            Set<String> untapped = new HashSet<>();
            for (Messages.ViewPermanent p : v.battlefield()) {
                held.add(p.card());
                if (!p.tapped()) {
                    untapped.add(p.id());
                }
            }
            assertTrue(held.containsAll(v.finds().keySet()), v.finds().keySet().toString());
            v.manaSources().forEach(s -> assertTrue(untapped.contains(s.id()), s.id()));
            v.handMana().forEach(h -> assertTrue(v.hand().contains(h.card()), h.card()));
            anyFinds |= v.finds().values().stream().anyMatch(f -> !f.isEmpty());
        }
        assertTrue(anyFinds, "no search found a library card in eight games");
    }

    @Test
    void theCommanderIsListedFromTheCommandZoneWithItsCost() {
        Messages.WireAction commander = actionDecisions().stream()
                .flatMap(d -> d.view().actions().stream())
                .filter(a -> a.kind().equals("cast_commander"))
                .findFirst().orElseThrow();
        assertEquals("Obeka, Brute Chronologist", commander.card());
        assertEquals("command_zone", commander.zone());
        assertEquals("{1}{U}{B}{R}", commander.cost());
    }

    @Test
    void eachResolvedEventAddsOurResolvedCasts() {
        int events = 0;
        for (TestGames.Recorder g : GAMES) {
            int last = 0;
            for (Messages.EventResolved e : g.all(Messages.EventResolved.class)) {
                events++;
                List<Messages.ResolvedCast> resolved = e.view().resolved();
                assertTrue(resolved.size() > last);
                last = resolved.size();
                Messages.ResolvedCast newest = resolved.get(resolved.size() - 1);
                assertNotNull(newest.card());
                assertEquals(e.view().turn(), newest.turn());
            }
        }
        assertTrue(events > 0, "no spell resolved in eight games");
    }

    /** Ruling F12: an action reply that sets keep, cards, option or do is a bad reply, not a pass. */
    @Test
    void anActionReplyWithAnotherKindsFieldEndsTheGame() {
        Messages.GameOver over = TestGames.play(3, 10, m -> m instanceof Messages.DecideAction
                ? Messages.Reply.ofKeep(m.seq(), true)
                : TestGames.first().ask(m));

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().startsWith("bad reply: ") && over.error().contains("action"), over.error());
    }
}
