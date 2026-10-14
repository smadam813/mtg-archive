package mtgarchive.xmage;

import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

class GameRunnerTest {

    @Test
    void theSameSeedPlaysTheSameGame() {
        Messages.GameOver a = TestGames.play(7, 3, TestGames.pass());
        Messages.GameOver b = TestGames.play(7, 3, TestGames.pass());

        assertEquals("horizon", a.reason());
        assertEquals(3, a.turns());
        assertNull(a.error());
        assertTrue(a.digest().matches("[0-9a-f]{64}"));
        assertEquals(a.digest(), b.digest());
    }

    @Test
    void anotherSeedPlaysAnotherGame() {
        assertNotEquals(TestGames.play(7, 3, TestGames.pass()).digest(), TestGames.play(8, 3, TestGames.pass()).digest());
    }

    @Test
    void ourSeatDrawsOnTurnOneLikeOurEngine() {
        TestGames.Recorder r = new TestGames.Recorder(TestGames.pass());
        TestGames.play(7, 1, r);

        Messages.View end = r.all(Messages.EventEndOfTurn.class).get(0).view();
        assertEquals(1, end.turn());
        assertEquals("end", end.step());
        assertEquals(8, end.hand().size()); // the seven, the turn-1 draw, no land played
        assertEquals(91, end.libraryCount()); // 99 - 8
        assertEquals(List.of("Obeka, Brute Chronologist"), end.commandZone());
        assertEquals(40, end.life());
    }

    @Test
    void aGameOnThePlayStartsAtItsStartLifeAndSkipsTheTurnOneDraw() {
        TestGames.Recorder r = new TestGames.Recorder(TestGames.pass());
        TestGames.play(new Messages.StartGame("start_game", TestGames.obeka().deckId(), 7, 1, 25, false, false, 1, false, false),
                TestGames.obeka(), r);

        Messages.View end = r.all(Messages.EventEndOfTurn.class).get(0).view();
        assertEquals(1, end.turn());
        assertEquals(7, end.hand().size()); // the seven, no turn-1 draw, no land played
        assertEquals(92, end.libraryCount()); // 99 - 7
        assertEquals(25, end.life());
    }

    @Test
    void theGameRulesSetCommanderDamage() {
        Messages.StartGame commander = TestGames.start(7, 1);
        Messages.StartGame brawl = new Messages.StartGame("start_game", commander.deckId(), 7, 1, 25, false, false, 1, false, false);

        assertTrue(GameRunner.newGame(commander).isCheckCommanderDamage());
        assertFalse(GameRunner.newGame(brawl).isCheckCommanderDamage());
    }

    @Test
    void aStartLifeBelowOneEndsTheGameWithAnEngineError() {
        Messages.GameOver over = TestGames.play(
                new Messages.StartGame("start_game", TestGames.obeka().deckId(), 7, 1, 0, true, true, 1, false, false),
                TestGames.obeka(), TestGames.pass());

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().contains("start_life"), over.error());
    }

    @Test
    void theCleanupDiscardIsForwarded() {
        TestGames.Recorder r = new TestGames.Recorder(TestGames.pass());
        TestGames.play(7, 1, r);

        Messages.DecideCount discard = r.all(Messages.DecideCount.class).get(0);
        assertEquals("discard", discard.kind());
        assertEquals(1, discard.count());
        assertEquals("cleanup", discard.view().step());
    }

    @Test
    void aDiscardAsksForTheMinimumAndLeavesAnOptionalOneToXmage() {
        assertEquals(1, PilotPlayer.discardCount(1, 1, 8)); // cleanup: exactly the excess
        assertEquals(2, PilotPlayer.discardCount(2, 5, 8)); // at least two: the pilot names two
        assertEquals(3, PilotPlayer.discardCount(4, 4, 3)); // more than the hand holds: the whole hand
        assertEquals(0, PilotPlayer.discardCount(0, 1, 8)); // "you may discard a card": XMage decides
        assertEquals(0, PilotPlayer.discardCount(0, Integer.MAX_VALUE, 8)); // "any number": XMage decides
    }

    @Test
    void theHorizonCountsOurTurns() {
        TestGames.Recorder r = new TestGames.Recorder(TestGames.pass());
        Messages.GameOver over = TestGames.play(7, 3, r);

        assertEquals(List.of(1, 2, 3), r.all(Messages.EventEndOfTurn.class).stream().map(e -> e.view().turn()).toList());
        assertEquals(3, over.turns());
    }

    @Test
    void theRawXmageBackstopEndsAnOrdinaryGameAsHorizonNotWin() {
        // No extra turns, so GameRunner's own stopOnTurn backstop (not our endAtHorizon) ends this
        // game: XMage's checkStopOnTurnOption records a draw and never calls game.end() itself, so
        // it must not trip the "we ended it ourselves" guard in gameOver and must not read back as win.
        Messages.GameOver over = TestGames.play(
                new Messages.StartGame("start_game", TestGames.obeka().deckId(), 7, 3, 40, true, true, 1, false, true),
                TestGames.obeka(), TestGames.pass());

        assertEquals("horizon", over.reason(), over.error());
        assertTrue(over.log().stream().noneMatch(e -> e.message().equals("HORIZON")),
                "endAtHorizon fired: this test no longer exercises the raw backstop");
    }

    @Test
    void extraTurnsCountAgainstTheHorizon() {
        // Each Time Warp that resolves on our turn gives us the next turn too: the global turn number
        // runs behind our turn count, so XMage's stopOnTurn alone would let us play past the horizon.
        LoadedDeck warps = TestGames.LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("Talrand, Sky Summoner"),
                List.of(new Messages.DeckEntry("Island", 60), new Messages.DeckEntry("Time Warp", 39))));
        TestGames.Recorder r = new TestGames.Recorder(m -> {
            if (m instanceof Messages.DecideAction d) {
                String land = null;
                for (Messages.WireAction a : d.view().actions()) {
                    if (a.card().equals("Time Warp")) {
                        return Messages.Reply.ofAction(m.seq(), a.id());
                    }
                    if (a.kind().equals("land") && land == null) {
                        land = a.id();
                    }
                }
                return Messages.Reply.ofAction(m.seq(), land);
            }
            return TestGames.pass().ask(m);
        });
        Messages.GameOver over = TestGames.play(
                new Messages.StartGame("start_game", warps.deckId(), 7, 6, 40, true, true, 1, false, true), warps, r);

        assertEquals("horizon", over.reason(), over.error());
        assertEquals(6, over.turns());
        assertEquals(List.of(1, 2, 3, 4, 5, 6), r.all(Messages.EventEndOfTurn.class).stream().map(e -> e.view().turn()).toList());
        assertTrue(over.log().stream().anyMatch(e -> e.turn() <= 5 && e.message().equals("RESOLVED Time Warp")),
                "no Time Warp resolved before our last turn");
        assertTrue(over.log().stream().anyMatch(e -> e.turn() == 6 && e.message().equals("HORIZON")),
                "the horizon did not end the game: XMage's backstop did");
    }

    @Test
    void aGameThatEndsBeforeTheHorizonIsAnEngineError() {
        GameCtx ctx = new GameCtx(TestGames.start(7, 3), TestGames.obeka(), TestGames.pass());
        ctx.ourTurns = 1;

        Messages.GameOver over = ctx.gameOver(null);

        assertEquals("engine_error", over.reason());
        assertEquals("game ended early at turn 1 of 3", over.error());
        assertEquals(1, over.turns());
    }

    @Test
    void aGameThatReachesTheHorizonReportsHorizon() {
        GameCtx ctx = new GameCtx(TestGames.start(7, 3), TestGames.obeka(), TestGames.pass());
        ctx.ourTurns = 3;

        Messages.GameOver over = ctx.gameOver(null);

        assertEquals("horizon", over.reason());
        assertNull(over.error());
    }

    @Test
    void theWatchdogEndsAGameThatAsksTooOften() {
        Messages.GameOver over = TestGames.play(
                new GameCtx(TestGames.start(7, 3), TestGames.obeka(), TestGames.pass(), 5, GameCtx.MAX_PRIORITIES));

        assertEquals("engine_error", over.reason());
        assertEquals("game exceeded 5 decisions", over.error());
    }

    @Test
    void theWatchdogEndsAGameThatTakesTooManyPriorities() {
        Messages.GameOver over = TestGames.play(
                new GameCtx(TestGames.start(7, 3), TestGames.obeka(), TestGames.pass(), GameCtx.MAX_DECISIONS, 10));

        assertEquals("engine_error", over.reason());
        assertEquals("game exceeded 10 priorities", over.error());
    }

    @Test
    void stopEndsTheGameAtOnce() {
        TestGames.Recorder r = new TestGames.Recorder(m -> m instanceof Messages.EventEndOfTurn
                ? Messages.Reply.ofDo(m.seq(), "stop") : TestGames.pass().ask(m));
        Messages.GameOver over = TestGames.play(5, 10, r);

        assertEquals("stopped", over.reason());
        assertEquals(1, over.turns());
        assertTrue(r.messages.get(r.messages.size() - 1) instanceof Messages.EventEndOfTurn);
    }

    @Test
    void anExceptionInsideAPriorityEndsTheGameWithItsFirstLine() {
        Messages.GameOver over = TestGames.play(5, 10, m -> {
            if (m instanceof Messages.EventEndOfTurn) {
                throw new IllegalStateException("boom");
            }
            return TestGames.pass().ask(m);
        });

        assertEquals("engine_error", over.reason());
        assertEquals("java.lang.IllegalStateException: boom", over.error());
        assertEquals(1, over.turns());
    }

    @Test
    void anExceptionBeforeTheFirstTurnEndsTheGameToo() {
        Messages.GameOver over = TestGames.play(5, 10, m -> {
            throw new IllegalStateException("boom at keep");
        });

        assertEquals("engine_error", over.reason());
        assertEquals("java.lang.IllegalStateException: boom at keep", over.error());
        assertEquals(0, over.turns());
    }

    @Test
    void aDecisionFailureEndsTheGameWithItsMessage() {
        Messages.GameOver over = TestGames.play(5, 10, m -> {
            if (m instanceof Messages.EventEndOfTurn) {
                throw new DecisionFailure("no reply to seq " + m.seq() + " within 5000 ms");
            }
            return TestGames.pass().ask(m);
        });

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().matches("no reply to seq \\d+ within 5000 ms"), over.error());
    }

    @Test
    void theLogComesOnlyWhenAskedAndNeverShowsTheLibraryOrder() {
        Messages.GameOver quiet = TestGames.play(7, 2, TestGames.pass());
        Messages.GameOver logged = TestGames.play(
                new Messages.StartGame("start_game", TestGames.obeka().deckId(), 7, 2, 40, true, true, 1, false, true),
                TestGames.obeka(), TestGames.pass());

        assertNull(quiet.log());
        assertEquals(quiet.digest(), logged.digest());
        assertEquals("SEED 7", logged.log().get(0).message());
        assertTrue(logged.log().stream().noneMatch(e -> e.message().startsWith("LIBRARY")));
        assertTrue(logged.log().stream().anyMatch(e -> e.turn() == 2 && e.step().equals("end")));
    }

    /** The Oracle deck (TestGames.oracle) wins on turn 2. */
    @Test
    void aComboWinEndsTheGameWithWin() {
        LoadedDeck oracle = TestGames.oracle();
        TestGames.Recorder r = new TestGames.Recorder(TestGames.castOracle());
        Messages.GameOver over = TestGames.play(
                new Messages.StartGame("start_game", oracle.deckId(), 7, 5, 40, true, true, 1, false, true), oracle, r);

        assertEquals("win", over.reason(), over.error());
        assertNull(over.error());
        assertEquals(2, over.turns());
        assertTrue(over.log().stream().anyMatch(e -> e.message().equals("RESOLVED Thassa's Oracle")), "the Oracle never resolved");
    }

    /**
     * The win trigger is on the stack when the Oracle resolves, so no resolved event carries the
     * Oracle: the win's game_over carries the final view, with the Oracle resolved and on the battlefield.
     */
    @Test
    void aComboWinCarriesTheFinalViewWithTheWinningCast() {
        LoadedDeck oracle = TestGames.oracle();
        TestGames.Recorder r = new TestGames.Recorder(TestGames.castOracle());
        Messages.GameOver over = TestGames.play(
                new Messages.StartGame("start_game", oracle.deckId(), 7, 5, 40, true, true, 1, false, false), oracle, r);

        assertEquals("win", over.reason(), over.error());
        assertTrue(r.all(Messages.EventResolved.class).stream()
                        .noneMatch(e -> e.view().resolved().stream().anyMatch(c -> c.card().equals("Thassa's Oracle"))),
                "a resolved event carried the Oracle: the final view is not the only place it shows");
        Messages.View last = over.view();
        assertEquals(List.of(new Messages.ResolvedCast("Thassa's Oracle", "front", 2)), last.resolved());
        assertTrue(last.battlefield().stream().anyMatch(p -> p.card().equals("Thassa's Oracle")), last.battlefield().toString());
        assertEquals(2, last.turn());
        assertEquals(List.of(), last.actions());
        assertEquals(Map.of(), last.finds());
    }

    @Test
    void aHorizonEndingCarriesNoView() {
        assertNull(TestGames.play(7, 2, TestGames.pass()).view());
    }

    @Test
    void theReasonTakesAnErrorFirstThenAStopThenALossThenAWin() {
        assertEquals("engine_error", GameCtx.reasonOf(true, true, true, true));
        assertEquals("stopped", GameCtx.reasonOf(false, true, true, true));
        assertEquals("life", GameCtx.reasonOf(false, false, true, true));
        assertEquals("win", GameCtx.reasonOf(false, false, false, true));
        assertEquals("horizon", GameCtx.reasonOf(false, false, false, false));
    }

    /**
     * With no land, a spirit guide in the opening hand pays for a one-drop: XMage's ComputerPlayer
     * alone does not pay with a mana ability that works from hand (see PilotPlayer.playManaHandling).
     */
    @Test
    void aSpiritGuideInTheOpeningHandPaysForAOneDrop() {
        LoadedDeck goblins = TestGames.LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("Krenko, Mob Boss"),
                List.of(new Messages.DeckEntry("Simian Spirit Guide", 1), new Messages.DeckEntry("Raging Goblin", 8))));
        Messages.GameOver over = TestGames.play(
                new Messages.StartGame("start_game", goblins.deckId(), 1, 1, 40, true, true, 1, false, true), goblins, m -> {
                    if (m instanceof Messages.DecideAction d) {
                        for (Messages.WireAction a : d.view().actions()) {
                            if (a.card().equals("Raging Goblin")) {
                                return Messages.Reply.ofAction(m.seq(), a.id());
                            }
                        }
                        return Messages.Reply.ofAction(m.seq(), null);
                    }
                    return TestGames.pass().ask(m);
                });

        assertEquals("horizon", over.reason(), over.error());
        assertTrue(over.log().stream().anyMatch(e -> e.message().startsWith("OPENING") && e.message().contains("Simian Spirit Guide")),
                "seed 1 must deal the guide into the opening hand");
        assertTrue(over.log().stream().anyMatch(e -> e.turn() == 1 && e.message().equals("PILOT cast Raging Goblin")),
                "the guide did not pay for Raging Goblin on turn 1");
        assertTrue(over.log().stream().noneMatch(e -> e.message().startsWith("PILOT cannot cast")),
                "XMage refused a cast that its playable list offered");
    }

    /**
     * Phase 3E, Obeka runs 112, 141 and 149: a cast that fails before the land drop is offered again
     * after it. XMage offers Stormchaser Mage ({U}{R}) on Mountain and Cascade Bluffs (the filter
     * chain makes {U}{R}), but its payment taps the Mountain for {R} and then cannot pay {U}. A new
     * Island makes a direct payment. A cast that fails again with no new mana source stays blocked.
     */
    @Test
    void aCastThatFailedBeforeTheLandDropIsTriedAgainAfterIt() {
        LoadedDeck izzet = TestGames.LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("Niv-Mizzet, Parun"),
                List.of(new Messages.DeckEntry("Stormchaser Mage", 1), new Messages.DeckEntry("Cascade Bluffs", 1),
                        new Messages.DeckEntry("Island", 1), new Messages.DeckEntry("Mountain", 7))));
        Messages.GameOver over = TestGames.play(
                new Messages.StartGame("start_game", izzet.deckId(), 1, 3, 40, true, true, 1, false, true), izzet, m -> {
                    if (m instanceof Messages.DecideAction d) {
                        List<Messages.ViewPermanent> board = d.view().battlefield();
                        String want = board.stream().noneMatch(p -> p.card().equals("Mountain")) ? "Mountain"
                                : board.stream().noneMatch(p -> p.card().equals("Cascade Bluffs")) ? "Cascade Bluffs" : "Island";
                        String land = null;
                        for (Messages.WireAction a : d.view().actions()) {
                            if (a.card().equals("Stormchaser Mage")) {
                                return Messages.Reply.ofAction(m.seq(), a.id());
                            }
                            if (a.kind().equals("land") && a.card().equals(want)) {
                                land = a.id();
                            }
                        }
                        return Messages.Reply.ofAction(m.seq(), land);
                    }
                    return TestGames.pass().ask(m);
                });

        assertEquals("horizon", over.reason(), over.error());
        List<String> log = over.log().stream().map(e -> "T" + e.turn() + " " + e.message()).toList();
        assertTrue(log.contains("T2 PILOT land Cascade Bluffs"), "seed 1 must give Cascade Bluffs by turn 2: " + log);
        assertEquals(1, log.stream().filter(l -> l.equals("T2 PILOT cannot cast Stormchaser Mage")).count(),
                "with no new mana source, the failed cast must be tried once only: " + log);
        int failed = log.indexOf("T3 PILOT cannot cast Stormchaser Mage");
        int island = log.indexOf("T3 PILOT land Island");
        int cast = log.indexOf("T3 PILOT cast Stormchaser Mage");
        assertTrue(failed >= 0 && island > failed, "turn 3 must try the cast before the land drop: " + log);
        assertTrue(cast > island, "the cast was not tried again after the land drop: " + log);
    }

    /** A modal double-faced card's back face resolves as "back", so a goal on that face can match. */
    @Test
    void aResolvedCastNamesTheFaceItWasCastAs() {
        LoadedDeck gods = TestGames.LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("Talrand, Sky Summoner"),
                List.of(new Messages.DeckEntry("Forest", 60),
                        new Messages.DeckEntry("Kolvori, God of Kinship // The Ringhart Crest", 39))));
        TestGames.Recorder r = new TestGames.Recorder(m -> {
            if (m instanceof Messages.DecideAction d) {
                String land = null;
                for (Messages.WireAction a : d.view().actions()) {
                    if (a.kind().equals("cast") && a.face().equals("back")) {
                        return Messages.Reply.ofAction(m.seq(), a.id());
                    }
                    if (a.kind().equals("land") && land == null) {
                        land = a.id();
                    }
                }
                return Messages.Reply.ofAction(m.seq(), land);
            }
            return TestGames.pass().ask(m);
        });
        Messages.GameOver over = TestGames.play(
                new Messages.StartGame("start_game", gods.deckId(), 7, 4, 40, true, true, 1, false, false), gods, r);

        assertEquals("horizon", over.reason(), over.error());
        List<Messages.ResolvedCast> resolved = r.all(Messages.EventEndOfTurn.class).get(3).view().resolved();
        assertFalse(resolved.isEmpty(), "no back face resolved in four turns");
        for (Messages.ResolvedCast cast : resolved) {
            assertEquals("Kolvori, God of Kinship // The Ringhart Crest", cast.card());
            assertEquals("back", cast.face());
        }
    }

    /** Carried-forward item 19: a real loss (TestGames.eightIslands). */
    @Test
    void aDrawFromAnEmptyLibraryEndsWithLifeAndTheFinalView() {
        LoadedDeck eight = TestGames.eightIslands();
        Messages.GameOver over = TestGames.play(
                new Messages.StartGame("start_game", eight.deckId(), 7, 5, 40, true, true, 1, false, true), eight, TestGames.pass());

        assertEquals("life", over.reason(), over.error());
        assertNull(over.error());
        assertEquals(2, over.turns());
        assertNotNull(over.view(), "a life ending carries the final view");
        assertEquals(0, over.view().libraryCount());
        assertEquals(2, over.view().turn());
        assertTrue(over.view().actions().isEmpty());
        assertTrue(over.view().finds().isEmpty());
    }

    /**
     * A run against the idle opponent has no combat: the pilot casts creatures, and none attacks.
     * XMage's Combat logs "{player} attacks {defenders} with {n} creature(s)" for a real attack
     * declaration, and "{player} skip attack" when a player declares none; the test must fail only
     * on the former.
     */
    @Test
    void ourSeatDeclaresNoAttackers() {
        LoadedDeck deck = TestGames.obeka();
        for (long seed = 1; seed <= 4; seed++) {
            Messages.GameOver over = TestGames.play(
                    new Messages.StartGame("start_game", deck.deckId(), seed, 10, 40, true, true, 1, false, true), deck, TestGames.first());
            long capturedSeed = seed;
            assertTrue(over.log().stream().noneMatch(e -> e.message().toLowerCase(Locale.ROOT).contains("attacks ")),
                    () -> "seed " + capturedSeed + " attacked: " + over.log().stream()
                            .filter(e -> e.message().toLowerCase(Locale.ROOT).contains("attacks "))
                            .findFirst().orElseThrow());
        }
    }

    /**
     * Carried-forward item 3: the shim logs CAP the first time in a turn that the activation cap hides
     * an ability. Sensei's Divining Top's "{1}: Look at the top three cards" activates twice on turn 3,
     * and the third Island leaves a third activation affordable.
     */
    @Test
    void aThirdActivationInATurnLogsTheCap() {
        LoadedDeck tops = TestGames.LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("Talrand, Sky Summoner"),
                List.of(new Messages.DeckEntry("Island", 60), new Messages.DeckEntry("Sensei's Divining Top", 39))));
        assertEquals(List.of(), tops.unimplemented());
        Messages.GameOver over = TestGames.play(new Messages.StartGame("start_game", tops.deckId(), 7, 3, 40, true, true, 1, false, true), tops, m -> {
            if (m instanceof Messages.DecideAction d) {
                String cast = null;
                String land = null;
                for (Messages.WireAction a : d.view().actions()) {
                    if (a.kind().equals("activate") && a.cost().equals("{1}")) {
                        return Messages.Reply.ofAction(m.seq(), a.id());
                    }
                    if (a.kind().equals("cast") && cast == null) {
                        cast = a.id();
                    }
                    if (a.kind().equals("land") && land == null) {
                        land = a.id();
                    }
                }
                return Messages.Reply.ofAction(m.seq(), land != null ? land : cast);
            }
            return TestGames.pass().ask(m);
        });

        assertEquals("horizon", over.reason(), over.error());
        assertEquals(1, over.log().stream().filter(e -> e.turn() == 3 && e.message().equals("CAP Sensei's Divining Top")).count());
    }

    /**
     * The BQ2 run 3 deck with Three Tree Mascot in place of Thror's Map, seed 95. On turn 4
     * XMage offers Springleaf Drum from exile for {1} while the Mascot is our only untapped source.
     * Without the guard, the payment pays the Mascot's own {1} with the Mascot again and saves a game
     * state at each level until the heap runs out. With it, the cast fails and the game plays on to
     * the horizon.
     */
    @Test
    void aManaAbilityNeverPaysItsOwnCost() {
        List<Messages.DeckEntry> cards = new ArrayList<>();
        for (String name : List.of("Angry Rabble", "Arcane Signet", "Ashe, Princess of Dalmasca", "Boros Guildgate",
                "Burning Curiosity", "Command Tower", "Earth Kingdom Jailer", "Elegant Parlor", "Embermouth Sentinel", "Erode",
                "Expedition Map", "Fire Sages", "Get Lost", "Giada, Font of Hope", "Giantfall", "Harsh Annotation",
                "Page, Loose Leaf", "Pinecone Strike", "Prompto Argentum", "Prophetic Prism", "Sacred Foundry",
                "Scorching Dragonfire", "Silver Deputy", "Solstice Revelations", "Springleaf Drum", "Stand Up for Yourself",
                "Sundown Pass", "Sunshot Militia", "Sunstar Expansionist", "Three Tree Mascot", "Troop of Ponies", "Tweeze",
                "Unwanted Remake", "Volatile Wanderglyph", "Wind-Scarred Crag", "World Map", "Zookeeper Mechan")) {
            cards.add(new Messages.DeckEntry(name, 1));
        }
        cards.add(new Messages.DeckEntry("Mountain", 11));
        cards.add(new Messages.DeckEntry("Plains", 11));
        Engine.warmUp();
        LoadedDeck deck = TestGames.LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("The Jolly Balloon Man"), cards));

        Messages.GameOver over = TestGames.play(
                new Messages.StartGame("start_game", deck.deckId(), 95, 4, 25, true, true, 1, false, false), deck, TestGames.first());

        assertNull(over.error());
        assertEquals("horizon", over.reason());
    }
}
