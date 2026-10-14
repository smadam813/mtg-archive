package mtgarchive.xmage;

import mage.util.ThreadUtils;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicReference;

/** Test helpers: the done-query 2 deck, games on a real game thread, and a recording decider. */
final class TestGames {

    static final DeckLibrary LIBRARY = new DeckLibrary();

    private TestGames() {
    }

    static LoadedDeck obeka() {
        Engine.warmUp();
        return LIBRARY.load(DeckText.resource("decks/obeka.txt"));
    }

    static Messages.StartGame start(long seed, int horizon) {
        return new Messages.StartGame("start_game", obeka().deckId(), seed, horizon, 40, true, true, 1, false, false);
    }

    static Messages.GameOver play(long seed, int horizon, Decider decider) {
        return play(start(seed, horizon), obeka(), decider);
    }

    /** XMage runs game code only on a thread named GAME... (ThreadUtils.ensureRunInGameThread). */
    static Messages.GameOver play(Messages.StartGame start, LoadedDeck deck, Decider decider) {
        return play(new GameCtx(start, deck, decider));
    }

    /** Plays the game of a context, for tests that lower the watchdog caps. */
    static Messages.GameOver play(GameCtx ctx) {
        AtomicReference<Messages.GameOver> over = new AtomicReference<>();
        Thread thread = new Thread(null, () -> over.set(GameRunner.play(ctx)),
                ThreadUtils.THREAD_PREFIX_GAME + "-test", GameRunner.GAME_THREAD_STACK);
        thread.start();
        try {
            thread.join();
        } catch (InterruptedException e) {
            throw new IllegalStateException(e);
        }
        return over.get();
    }

    /** Runs a scenario on a GAME thread, as XMage requires. */
    static Messages.ScenarioResult scenario(Messages.Scenario scenario) {
        Engine.warmUp();
        AtomicReference<Messages.ScenarioResult> result = new AtomicReference<>();
        Thread thread = new Thread(null, () -> result.set(ScenarioRunner.run(scenario, LIBRARY)),
                ThreadUtils.THREAD_PREFIX_GAME + "-scenario", GameRunner.GAME_THREAD_STACK);
        thread.start();
        try {
            thread.join();
        } catch (InterruptedException e) {
            throw new IllegalStateException(e);
        }
        return result.get();
    }

    /** The view of any decide or event. */
    static Messages.View viewOf(Messages.InGame m) {
        if (m instanceof Messages.DecideAction d) {
            return d.view();
        }
        if (m instanceof Messages.DecideKeep d) {
            return d.view();
        }
        if (m instanceof Messages.DecideCount d) {
            return d.view();
        }
        if (m instanceof Messages.DecidePick d) {
            return d.view();
        }
        if (m instanceof Messages.EventEndOfTurn e) {
            return e.view();
        }
        if (m instanceof Messages.EventResolved e) {
            return e.view();
        }
        throw new IllegalArgumentException("no view on " + m);
    }

    /**
     * Eight Islands and Thassa's Oracle: the seven, the turn-1 draw and the turn-2 draw hold all nine
     * cards, so on turn 2 two Islands cast the Oracle with an empty library, and its trigger wins.
     */
    static LoadedDeck oracle() {
        return LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("Talrand, Sky Summoner"),
                List.of(new Messages.DeckEntry("Island", 8), new Messages.DeckEntry("Thassa's Oracle", 1))));
    }

    /**
     * Eight Islands: the seven and the turn-1 draw take all eight, so the turn-2 draw is from an
     * empty library, and our seat loses (CR 704.5b).
     */
    static LoadedDeck eightIslands() {
        return LIBRARY.load(new Messages.LoadDeck("load_deck", List.of("Talrand, Sky Summoner"),
                List.of(new Messages.DeckEntry("Island", 8))));
    }

    /** Casts Thassa's Oracle when it can, else plays the first land; passes everything else. */
    static Decider castOracle() {
        return m -> {
            if (m instanceof Messages.DecideAction d) {
                String land = null;
                for (Messages.WireAction a : d.view().actions()) {
                    if (a.card().equals("Thassa's Oracle")) {
                        return Messages.Reply.ofAction(m.seq(), a.id());
                    }
                    if (a.kind().equals("land") && land == null) {
                        land = a.id();
                    }
                }
                return Messages.Reply.ofAction(m.seq(), land);
            }
            return pass().ask(m);
        };
    }

    static ScriptedResponder pass() {
        return new ScriptedResponder(ScriptedResponder.Mode.PASS);
    }

    static ScriptedResponder first() {
        return new ScriptedResponder(ScriptedResponder.Mode.FIRST);
    }

    /** Records every decide and event, then lets another decider answer. */
    static final class Recorder implements Decider {

        final List<Messages.InGame> messages = new ArrayList<>();
        private final Decider answer;

        Recorder(Decider answer) {
            this.answer = answer;
        }

        @Override
        public Messages.Reply ask(Messages.InGame message) {
            messages.add(message);
            return answer.ask(message);
        }

        <T> List<T> all(Class<T> type) {
            List<T> out = new ArrayList<>();
            for (Messages.InGame m : messages) {
                if (type.isInstance(m)) {
                    out.add(type.cast(m));
                }
            }
            return out;
        }
    }
}
