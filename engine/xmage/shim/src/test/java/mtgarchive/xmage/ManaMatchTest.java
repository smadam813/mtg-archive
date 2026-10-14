package mtgarchive.xmage;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.List;
import java.util.Set;
import org.junit.jupiter.api.Test;

/** The maximum matching of pips to sources (roadmap item 22, phase 3E). */
class ManaMatchTest {

    private static ManaMatch.Source src(String id, String... colors) {
        return new ManaMatch.Source(id, Set.of(colors));
    }

    /** Run 9 of the Obeka diagnosis: Command Tower is the only blue source, so it must not pay {B}. */
    @Test
    void theOnlySourceOfAColorIsKeptForIt() {
        List<ManaMatch.Source> board = List.of(
                src("a-badlands", "B", "R"), src("b-crypt", "B", "R"), src("c-tower", "U", "B", "R"), src("d-swamp", "B"));

        assertEquals("d-swamp", ManaMatch.pick("B", List.of("U", "R"), board));
        assertEquals("c-tower", ManaMatch.pick("U", List.of("B", "R"), board));
    }

    /** Review Focus 3: generic takes the source with the fewest colors, so Sol Ring pays it and the any-color land stays. */
    @Test
    void genericTakesTheSourceWithFewestColors() {
        List<ManaMatch.Source> board = List.of(
                src("a-confluence", "W", "U", "B", "R", "G"), src("b-solring", "C"), src("c-island", "U"), src("d-mountain", "R"));

        assertEquals("b-solring", ManaMatch.pick("1", List.of("U", "B", "R"), board));
    }

    private static ManaMatch.Source hand(String id, String... colors) {
        return new ManaMatch.Source(id, Set.of(colors), true);
    }

    /**
     * A card in hand pays only when the board alone has no plan, as in the retired engine's GreedyPayment.
     * With one pass over board and hand together, the Mountain could pay {1} and leave {R} to the guide.
     */
    @Test
    void aCardInHandPaysOnlyWhenTheBoardCannot() {
        List<ManaMatch.Source> board = List.of(src("a-mountain", "R"), src("b-island", "U"), hand("0-guide", "R"));

        assertEquals("b-island", ManaMatch.pick("1", List.of("R"), board));
        assertEquals("a-mountain", ManaMatch.pick("R", List.of(), board));
    }

    /** Volcanic Island is the only blue source, so the guide pays {R} (run 141). */
    @Test
    void aCardInHandPaysWhenTheBoardHasNoPlan() {
        List<ManaMatch.Source> board = List.of(src("a-volcanic", "U", "R"), hand("b-guide", "R"));

        assertEquals("b-guide", ManaMatch.pick("R", List.of("U"), board));
        assertEquals("a-volcanic", ManaMatch.pick("U", List.of("R"), board));
    }

    @Test
    void noPlanReturnsNull() {
        List<ManaMatch.Source> board = List.of(src("a-island", "U"), src("b-island", "U"));

        assertNull(ManaMatch.pick("B", List.of("U"), board));
        assertNull(ManaMatch.pick("U", List.of("B"), board));
    }

    /** The matching augments: the first greedy choice is undone when a later pip needs it. */
    @Test
    void aLaterPipCanTakeBackASource() {
        List<ManaMatch.Source> board = List.of(src("a-dual", "U", "B"), src("b-swamp", "B"));

        assertEquals(true, ManaMatch.covers(List.of("B", "U"), board));
        assertEquals(false, ManaMatch.covers(List.of("B", "B", "U"), board));
    }

    /** Review Focus 4: a hybrid or Phyrexian part has no plain symbol, so XMage pays it. */
    @Test
    void aHybridPartIsLeftToXmage() {
        assertNull(ManaMatch.symbol(new mage.abilities.costs.mana.HybridManaCost(mage.constants.ColoredManaSymbol.U, mage.constants.ColoredManaSymbol.R)));
        assertEquals("U", ManaMatch.symbol(new mage.abilities.costs.mana.ColoredManaCost(mage.constants.ColoredManaSymbol.U)));
        assertEquals("1", ManaMatch.symbol(new mage.abilities.costs.mana.GenericManaCost(1)));
        assertEquals("C", ManaMatch.symbol(new mage.abilities.costs.mana.ColorlessManaCost(1)));
        mage.abilities.costs.mana.ColoredManaCost phyrexian = new mage.abilities.costs.mana.ColoredManaCost(mage.constants.ColoredManaSymbol.U);
        phyrexian.setPhyrexian(true);
        assertNull(ManaMatch.symbol(phyrexian));
    }

    /**
     * Command Tower is the only blue source, so the matching taps it for {U}, the last part of
     * {W}{U}. XMage's ComputerPlayer.choose would make white, the first color of W, R, G, U, B
     * that the unpaid cost holds, and leave {U} unpaid. The shim makes the color it matched.
     */
    @Test
    void commandTowerMakesTheColorItWasMatchedFor() {
        Messages.Scenario scenario = new Messages.Scenario("scenario", List.of("Brago, King Eternal"),
                List.of(new Messages.DeckEntry("Command Tower", 1), new Messages.DeckEntry("Deft Duelist", 1),
                        new Messages.DeckEntry("Plains", 1)),
                1,
                new Messages.ScenarioStart(List.of("Deft Duelist"),
                        List.of(ScenarioTest.untapped("Command Tower"), ScenarioTest.untapped("Plains")), List.of(), List.of()),
                List.of(ScenarioTest.cast("Deft Duelist")));

        Messages.ScenarioResult r = ScenarioTest.ok(scenario);

        assertTrue(r.view().battlefield().stream().anyMatch(p -> p.card().equals("Deft Duelist")), String.valueOf(r.log()));
    }

    /**
     * Phase 3E, Obeka run 141: Volcanic Island alone cannot pay {U}{R}, but with Simian Spirit Guide
     * in hand it can. The matching must count the guide, or it finds no plan, and XMage then taps
     * the Island for {R} and leaves {U} unpaid.
     */
    @Test
    void aSpiritGuideInHandIsASourceOfTheMatching() {
        Messages.ScenarioResult r = ScenarioTest.ok(ScenarioTest.scenario(List.of("Stormchaser Mage", "Simian Spirit Guide"),
                List.of(ScenarioTest.untapped("Volcanic Island")), List.of(), List.of(),
                List.of(ScenarioTest.cast("Stormchaser Mage"))));

        assertTrue(r.view().battlefield().stream().anyMatch(p -> p.card().equals("Stormchaser Mage")), String.valueOf(r.log()));
        assertTrue(r.view().exile().contains("Simian Spirit Guide"), String.valueOf(r.log()));
    }

    /** When the board alone can pay, the guide stays in hand: a card in hand pays only when it must. */
    @Test
    void theBoardPaysBeforeASpiritGuideInHand() {
        Messages.ScenarioResult r = ScenarioTest.ok(ScenarioTest.scenario(List.of("Stormchaser Mage", "Simian Spirit Guide"),
                List.of(ScenarioTest.untapped("Volcanic Island"), ScenarioTest.untapped("Mountain")), List.of(), List.of(),
                List.of(ScenarioTest.cast("Stormchaser Mage"))));

        assertTrue(r.view().battlefield().stream().anyMatch(p -> p.card().equals("Stormchaser Mage")), String.valueOf(r.log()));
        assertTrue(r.view().hand().contains("Simian Spirit Guide"), String.valueOf(r.log()));
    }

    @Test
    void anyColorManaMakesEveryColor() {
        assertEquals(Set.of("W", "U", "B", "R", "G"), ManaMatch.colors(mage.Mana.AnyMana(1)));
        assertEquals(Set.of("C"), ManaMatch.colors(mage.Mana.ColorlessMana(2)));
        assertEquals(Set.of("U"), ManaMatch.colors(mage.Mana.BlueMana(1)));
    }
}
