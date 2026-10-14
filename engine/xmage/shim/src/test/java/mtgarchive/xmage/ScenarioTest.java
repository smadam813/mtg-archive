package mtgarchive.xmage;

import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.TreeSet;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** Scenario mode (spec §6.2): our zones set up on turn 1, a script of steps, and the state after it. */
class ScenarioTest {

    static Messages.StartPermanent untapped(String card) {
        return new Messages.StartPermanent(card, false, null);
    }

    static Messages.ScenarioAction cast(String card) {
        return new Messages.ScenarioAction("cast", card, null, null, null);
    }

    static Messages.ScenarioAction activate(String permanent, int ability) {
        return new Messages.ScenarioAction("activate", null, permanent, ability, null);
    }

    static Messages.ScenarioAction choose(String pick) {
        return new Messages.ScenarioAction("choose", null, null, null, pick);
    }

    /** A scenario; {@code cards} holds every name it uses, and {@code extra} names outside the zones. */
    static Messages.Scenario scenario(List<String> hand, List<Messages.StartPermanent> battlefield, List<String> library,
                                      List<String> graveyard, List<Messages.ScenarioAction> actions, String... extra) {
        TreeSet<String> names = new TreeSet<>(hand);
        names.addAll(library);
        names.addAll(graveyard);
        battlefield.forEach(p -> {
            names.add(p.card());
            if (p.named() != null) {
                names.add(p.named());
            }
        });
        names.addAll(Arrays.asList(extra));
        List<Messages.DeckEntry> cards = new ArrayList<>();
        names.forEach(n -> cards.add(new Messages.DeckEntry(n, 1)));
        return new Messages.Scenario("scenario", List.of(), cards, 1,
                new Messages.ScenarioStart(hand, battlefield, library, graveyard), actions);
    }

    static Messages.ScenarioResult ok(Messages.Scenario scenario) {
        Messages.ScenarioResult result = TestGames.scenario(scenario);
        assertNull(result.error(), result.error() + "\n" + result.log());
        assertNotNull(result.view());
        return result;
    }

    @Test
    void aScenarioStartsOnTurnOneWithTheZonesItWasGiven() {
        Messages.ScenarioResult r = ok(scenario(List.of("Dark Ritual"), List.of(untapped("Swamp")),
                List.of("Forest", "Sol Ring", "Swamp"), List.of("Opt"), List.of()));

        assertEquals(1, r.view().turn());
        assertEquals("main", r.view().step());
        assertEquals(40, r.view().life());
        assertEquals(List.of("Dark Ritual"), r.view().hand());
        assertEquals(List.of("Opt"), r.view().graveyard());
        assertEquals(1, r.view().battlefield().size());
        assertEquals("Swamp", r.view().battlefield().get(0).card());
        assertEquals(List.of("Forest", "Sol Ring", "Swamp"), r.library(), "the library is not top first, or a draw took a card");
        assertEquals(3, r.view().libraryCount());
    }

    /**
     * A positive control: another source still pays the {1} of Three Tree Mascot's "{1}: Add one
     * mana of any color". It passes without the guard, because a scenario offers only casts from hand. The
     * recursion itself is pinned by GameRunnerTest.aManaAbilityNeverPaysItsOwnCost.
     */
    @Test
    void anotherSourcePaysTheManaCostOfAManaAbility() {
        Messages.ScenarioResult r = ok(scenario(List.of("Savannah Lions"),
                List.of(untapped("Mountain"), untapped("Three Tree Mascot")), List.of(), List.of(),
                List.of(cast("Savannah Lions"))));

        assertTrue(r.view().battlefield().stream().anyMatch(p -> p.card().equals("Savannah Lions")), r.log().toString());
        assertTrue(r.view().battlefield().stream().anyMatch(p -> p.card().equals("Mountain") && p.tapped()), r.log().toString());
    }

    @Test
    void darkRitualAddsThreeBlackMana() {
        Messages.ScenarioResult r = ok(scenario(List.of("Dark Ritual"), List.of(untapped("Swamp")), List.of(), List.of(),
                List.of(cast("Dark Ritual"))));

        assertEquals("{B}{B}{B}", r.view().pool());
        assertEquals(List.of("Dark Ritual"), r.view().graveyard());
        assertTrue(r.view().battlefield().get(0).tapped(), "the Swamp paid for the Ritual");
        assertEquals(List.of(), r.view().hand());
    }

    @Test
    void aTappedStartPermanentStartsTapped() {
        Messages.ScenarioResult r = ok(scenario(List.of(), List.of(new Messages.StartPermanent("Swamp", true, null)), List.of(), List.of(), List.of()));

        assertTrue(r.view().battlefield().get(0).tapped());
    }

    @Test
    void aCastThatCannotRunIsAnError() {
        Messages.ScenarioResult r = TestGames.scenario(scenario(List.of("Dark Ritual"), List.of(), List.of(), List.of(), List.of(cast("Dark Ritual"))));

        assertNotNull(r.error());
        assertTrue(r.error().contains("step 1 cannot cast Dark Ritual"), r.error());
        assertNull(r.view());
    }

    @Test
    void aCardXmageLacksIsAnError() {
        Messages.ScenarioResult r = TestGames.scenario(scenario(List.of("No Such Card In Xmage"), List.of(), List.of(), List.of(), List.of()));

        assertTrue(r.error() != null && r.error().contains("XMage lacks No Such Card In Xmage"), String.valueOf(r.error()));
    }

    @Test
    void theSameScenarioGivesTheSameLog() {
        Messages.Scenario s = scenario(List.of("Dark Ritual"), List.of(untapped("Swamp")), List.of("Forest"), List.of(), List.of(cast("Dark Ritual")));

        assertEquals(ok(s).log(), ok(s).log());
    }

    @Test
    void aTutorPickTakesTheChooseStep() {
        Messages.ScenarioResult r = ok(scenario(List.of("Demonic Tutor"), List.of(untapped("Swamp"), untapped("Swamp")),
                List.of("Sol Ring", "Forest"), List.of(), List.of(cast("Demonic Tutor"), choose("Sol Ring"))));

        assertEquals(List.of("Sol Ring"), r.view().hand());
        assertEquals(1, r.view().libraryCount());
        assertEquals(1, r.picks().size());
        assertEquals("tutor", r.picks().get(0).purpose());
    }

    @Test
    void failToFindTakesTheStopOption() {
        Messages.ScenarioResult r = ok(scenario(List.of("Mystical Tutor"), List.of(untapped("Island")),
                List.of("Forest", "Brainstorm", "Sol Ring"), List.of(), List.of(cast("Mystical Tutor"), choose("fail to find"))));

        assertEquals(PilotPlayer.NONE, r.picks().get(0).chosen());
        assertEquals(3, r.view().libraryCount());
    }

    @Test
    void aManaAbilityActivatesByIndex() {
        Messages.ScenarioResult r = ok(scenario(List.of(), List.of(untapped("Sol Ring")), List.of(), List.of(), List.of(activate("Sol Ring", 0))));

        assertEquals("{C}{C}", r.view().pool());
        assertTrue(r.view().battlefield().get(0).tapped());
    }

    @Test
    void anActivationThatSearchesTakesTheChooseStep() {
        Messages.ScenarioResult r = ok(scenario(List.of(), List.of(untapped("Evolving Wilds")), List.of("Forest"), List.of(),
                List.of(activate("Evolving Wilds", 0), choose("Forest"))));

        assertEquals(List.of("Evolving Wilds"), r.view().graveyard());
        assertEquals("Forest", r.view().battlefield().get(0).card());
        assertTrue(r.view().battlefield().get(0).tapped());
    }

    /**
     * "Basic land card" has a stated quality (predicates): Evolving Wilds can fail to find even though
     * its search is otherwise mandatory (CR 701.23b). Unlike a tutor whose found card stays in the
     * library (Mystical Tutor), this one moves it out, so a fail to find is visible in the result: no
     * land enters the battlefield, and the library keeps its card.
     */
    @Test
    void aStatedQualityActivationCanFailToFind() {
        Messages.ScenarioResult r = ok(scenario(List.of(), List.of(untapped("Evolving Wilds")), List.of("Forest"), List.of(),
                List.of(activate("Evolving Wilds", 0), choose("fail to find"))));

        assertEquals(List.of("Evolving Wilds"), r.view().graveyard());
        assertTrue(r.view().battlefield().isEmpty(), r.view().battlefield().toString());
        assertEquals(1, r.view().libraryCount());
    }

    @Test
    void aNamePickTakesTheChooseStepAndTheResultShowsTheName() {
        Messages.ScenarioResult r = ok(scenario(List.of("Pithing Needle"), List.of(untapped("Swamp")), List.of(), List.of(),
                List.of(cast("Pithing Needle"), choose("Evolving Wilds")), "Evolving Wilds"));

        assertEquals("name", r.picks().get(0).purpose());
        assertEquals(1, r.named().size());
        assertEquals("Pithing Needle", r.named().get(0).card());
        assertEquals("Evolving Wilds", r.named().get(0).value());
    }

    @Test
    void aStartNameLocksTheNamedCardsOtherAbilitiesButNotItsManaAbilities() {
        Messages.ScenarioResult r = ok(scenario(List.of(),
                List.of(new Messages.StartPermanent("Pithing Needle", false, "Evolving Wilds"), untapped("Evolving Wilds"), untapped("Sol Ring")),
                List.of("Forest"), List.of(), List.of()));

        assertEquals("Evolving Wilds", r.named().get(0).value());
        Messages.AbilityState wilds = r.abilities().stream().filter(a -> a.card().equals("Evolving Wilds")).findFirst().orElseThrow();
        Messages.AbilityState ring = r.abilities().stream().filter(a -> a.card().equals("Sol Ring")).findFirst().orElseThrow();
        assertEquals(0, wilds.index());
        assertEquals(false, wilds.mana());
        assertEquals(true, wilds.locked(), "Pithing Needle does not lock the named card");
        assertEquals(false, wilds.activatable());
        assertEquals(true, ring.mana());
        assertEquals(false, ring.locked());
        assertEquals(true, ring.activatable());
    }

    /** An ability that cannot be activated for another reason (its cost: the tap) is not locked. */
    @Test
    void aTappedPermanentsAbilityIsNotActivatableButNotLocked() {
        Messages.ScenarioResult r = ok(scenario(List.of(), List.of(new Messages.StartPermanent("Evolving Wilds", true, null)),
                List.of("Forest"), List.of(), List.of()));

        Messages.AbilityState wilds = r.abilities().get(0);
        assertEquals(false, wilds.locked());
        assertEquals(false, wilds.activatable());
    }

    @Test
    void aStartNameOnACardThatNamesNothingIsAnError() {
        Messages.ScenarioResult r = TestGames.scenario(scenario(List.of(),
                List.of(new Messages.StartPermanent("Sol Ring", false, "Evolving Wilds"), new Messages.StartPermanent("Pithing Needle", false, "Swamp")),
                List.of(), List.of(), List.of(), "Evolving Wilds", "Swamp"));

        assertTrue(r.error() != null && r.error().contains("start permanent Sol Ring should name Evolving Wilds, but it names nothing"), String.valueOf(r.error()));
    }

    @Test
    void aStepThatLacksAFieldIsAnError() {
        Messages.ScenarioResult r = TestGames.scenario(scenario(List.of("Dark Ritual"), List.of(untapped("Swamp")), List.of(), List.of(),
                List.of(new Messages.ScenarioAction("cast", null, null, null, null))));

        assertEquals("step 1 needs a card", r.error());
    }

    @Test
    void aStepOfAnUnknownTypeIsAnError() {
        Messages.ScenarioResult r = TestGames.scenario(scenario(List.of(), List.of(untapped("Swamp")), List.of(), List.of(),
                List.of(new Messages.ScenarioAction("attack", "Swamp", null, null, null))));

        assertEquals("step 1 needs a known type (cast, activate or choose), not attack", r.error());
    }

    @Test
    void theLogShowsTheStartHandNotAnEmptyOne() {
        Messages.ScenarioResult r = ok(scenario(List.of("Dark Ritual"), List.of(untapped("Swamp")), List.of(), List.of(), List.of()));

        assertTrue(r.log().stream().anyMatch(e -> e.message().equals("OPENING hand=[Dark Ritual]")), r.log().toString());
        assertTrue(r.log().stream().noneMatch(e -> e.message().equals("OPENING hand=[]")), r.log().toString());
    }

    @Test
    void aStartNameOnAManaSourceLeavesItsManaAbilityFree() {
        Messages.ScenarioResult r = ok(scenario(List.of(),
                List.of(new Messages.StartPermanent("Pithing Needle", false, "Sol Ring"), untapped("Sol Ring")), List.of(), List.of(), List.of()));

        assertEquals(true, r.abilities().stream().filter(a -> a.card().equals("Sol Ring")).findFirst().orElseThrow().activatable());
    }

    @Test
    void aChooseStepWithNoOpenPickIsAnError() {
        Messages.ScenarioResult r = TestGames.scenario(scenario(List.of(), List.of(untapped("Swamp")), List.of(), List.of(), List.of(choose("Forest"))));

        assertTrue(r.error() != null && r.error().contains("step 1 chooses Forest, but no pick is open"), String.valueOf(r.error()));
    }

    @Test
    void aPickTheScriptDoesNotAnswerIsAnError() {
        Messages.ScenarioResult r = TestGames.scenario(scenario(List.of("Demonic Tutor"), List.of(untapped("Swamp"), untapped("Swamp")),
                List.of("Sol Ring"), List.of(), List.of(cast("Demonic Tutor"))));

        assertTrue(r.error() != null && r.error().contains("a tutor pick came, but step 2 is the end of the script"), String.valueOf(r.error()));
    }

    @Test
    void aChooseStepTheTutorDoesNotOfferIsAnError() {
        Messages.ScenarioResult r = TestGames.scenario(scenario(List.of("Demonic Tutor"), List.of(untapped("Swamp"), untapped("Swamp")),
                List.of("Sol Ring"), List.of(), List.of(cast("Demonic Tutor"), choose("Forest")), "Forest"));

        assertTrue(r.error() != null && r.error().contains("step 2 chooses Forest, which the tutor pick does not offer"), String.valueOf(r.error()));
    }

    @Test
    void aSpiritGuidePaysAOneDropWithNoLand() {
        Messages.ScenarioResult r = ok(scenario(List.of("Elvish Spirit Guide", "Elvish Mystic"), List.of(), List.of(), List.of(),
                List.of(cast("Elvish Mystic"))));

        assertEquals(List.of(), r.view().hand());
        assertEquals(List.of("Elvish Spirit Guide"), r.view().exile());
        assertEquals(List.of("Elvish Mystic"), r.view().battlefield().stream().map(Messages.ViewPermanent::card).toList());
    }

    @Test
    void aLandPaysBeforeASpiritGuide() {
        Messages.ScenarioResult r = ok(scenario(List.of("Elvish Spirit Guide", "Elvish Mystic"), List.of(untapped("Forest")),
                List.of(), List.of(), List.of(cast("Elvish Mystic"))));

        assertEquals(List.of("Elvish Spirit Guide"), r.view().hand());
        assertEquals(List.of(), r.view().exile());
        assertTrue(r.view().battlefield().stream().anyMatch(p -> p.card().equals("Forest") && p.tapped()), "the Forest paid");
    }

    @Test
    void aSpiritGuidePaysWhatTheLandCannot() {
        Messages.ScenarioResult r = ok(scenario(List.of("Elvish Spirit Guide", "Grizzly Bears"), List.of(untapped("Forest")),
                List.of(), List.of(), List.of(cast("Grizzly Bears"))));

        assertEquals(List.of(), r.view().hand());
        assertEquals(List.of("Elvish Spirit Guide"), r.view().exile());
        assertTrue(r.view().battlefield().stream().anyMatch(p -> p.card().equals("Forest") && p.tapped()), "the Forest paid");
        assertTrue(r.view().battlefield().stream().anyMatch(p -> p.card().equals("Grizzly Bears")));
    }

    @Test
    void anActivationOfAMissingAbilityIsAnError() {
        Messages.ScenarioResult r = TestGames.scenario(scenario(List.of(), List.of(untapped("Sol Ring")), List.of(), List.of(), List.of(activate("Sol Ring", 3))));

        assertTrue(r.error() != null && r.error().contains("step 1 cannot activate Sol Ring ability 3"), String.valueOf(r.error()));
    }
}
