package mtgarchive.xmage;

import mage.util.ThreadUtils;

import java.util.Map;
import java.util.TreeMap;
import java.util.concurrent.ConcurrentSkipListMap;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Determinism self-test (spec 6.3). Plays seeds [seed, seed + games) of the done-query 2 deck with
 * the FIRST responder on the given number of game threads, twice in this JVM. Prints one line per
 * seed, "GAME seed digest reason turns", in seed order, then exits 1 when the two passes differ.
 * selftest.sh compares the lines across fresh JVMs, thread counts and JVM flags. With
 * {@code --interactive true} each game seats the scripted opponent, and the responder opposes the
 * first cast from turn 2 with Counterspell, the end of turn 3 with Atomize and the end of turn 4 with Rout
 * (InteractiveResponder).
 * <pre>java ... mtgarchive.xmage.SelfTest --seed 101 --games 20 --threads 4 [--interactive true]</pre>
 */
public final class SelfTest {

    private SelfTest() {
    }

    public static void main(String[] args) throws Exception {
        Map<String, String> options = new TreeMap<>();
        for (int i = 0; i + 1 < args.length; i += 2) {
            options.put(args[i], args[i + 1]);
        }
        long seed = Long.parseLong(options.getOrDefault("--seed", "101"));
        int games = Integer.parseInt(options.getOrDefault("--games", "20"));
        int threads = Integer.parseInt(options.getOrDefault("--threads", "1"));
        boolean interactive = Boolean.parseBoolean(options.getOrDefault("--interactive", "false"));
        Engine.warmUp();
        LoadedDeck deck = new DeckLibrary().load(DeckText.resource("decks/obeka.txt"));
        Map<Long, String> first = playAll(deck, seed, games, threads, interactive);
        Map<Long, String> second = playAll(deck, seed, games, threads, interactive);
        first.forEach((s, line) -> System.out.println("GAME " + s + " " + line));
        boolean same = first.equals(second);
        System.out.println("SELFTEST same_jvm_repeat=" + same + " games=" + first.size() + " threads=" + threads
                + (interactive ? " interactive=true" : ""));
        System.out.flush();
        System.exit(same ? 0 : 1);
    }

    /** The disruption card table the service sends (DisruptionCards.All). */
    static final Messages.Opponent TABLE = new Messages.Opponent(java.util.List.of("Counterspell", "Atomize", "Rout"));

    static Map<Long, String> playAll(LoadedDeck deck, long seed, int games, int threads) throws InterruptedException {
        return playAll(deck, seed, games, threads, false);
    }

    static Map<Long, String> playAll(LoadedDeck deck, long seed, int games, int threads, boolean interactive)
            throws InterruptedException {
        Map<Long, String> out = new ConcurrentSkipListMap<>();
        AtomicInteger next = new AtomicInteger();
        Thread[] workers = new Thread[threads];
        for (int t = 0; t < threads; t++) {
            workers[t] = new Thread(null, () -> {
                int k;
                while ((k = next.getAndIncrement()) < games) {
                    long s = seed + k;
                    Messages.StartGame start = new Messages.StartGame("start_game", deck.deckId(), s, 10, 40, true, true, 1,
                            interactive, false);
                    Messages.GameOver over = interactive
                            ? GameRunner.play(start.withOpponent(TABLE), deck, new InteractiveResponder())
                            : GameRunner.play(start, deck, new ScriptedResponder(ScriptedResponder.Mode.FIRST));
                    out.put(s, over.digest() + " " + over.reason() + " " + over.turns()
                            + (over.error() == null ? "" : " error=" + over.error()));
                }
            }, ThreadUtils.THREAD_PREFIX_GAME + "-selftest-" + t, GameRunner.GAME_THREAD_STACK);
            workers[t].start();
        }
        for (Thread w : workers) {
            w.join();
        }
        return out;
    }
}
