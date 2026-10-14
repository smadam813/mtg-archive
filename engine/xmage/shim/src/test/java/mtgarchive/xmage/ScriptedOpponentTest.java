package mtgarchive.xmage;

import mage.cards.repository.CardInfo;
import mage.util.RandomUtil;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The scripted opponent seat (ADR 0013). With nothing due it plays exactly as the idle opponent, so a
 * game with a non-null opponent gives the idle opponent's digest byte for byte.
 */
class ScriptedOpponentTest {

    /** The disruption card table the service sends (DisruptionCards.All). */
    static final Messages.Opponent TABLE = new Messages.Opponent(List.of("Counterspell", "Atomize", "Rout"));

    @Test
    void aScriptedSeatWithNothingDueGivesTheIdleOpponentsDigestByteForByte() {
        for (long seed : new long[] {7, 8, 121}) {
            Messages.StartGame idle = new Messages.StartGame("start_game", TestGames.obeka().deckId(), seed, 4, 40, true,
                    true, 1, false, true);
            Messages.GameOver a = TestGames.play(idle, TestGames.obeka(), TestGames.first());
            Messages.GameOver b = TestGames.play(idle.withOpponent(TABLE), TestGames.obeka(), TestGames.first());

            assertNull(a.error());
            assertEquals("horizon", a.reason());
            assertEquals(a.digest(), b.digest(), "seed " + seed);
            assertEquals(a.log(), b.log(), "seed " + seed);
        }
    }

    /**
     * Seam 2: a null opponent leaves today's logs unchanged. logs/null-opponent-obeka.tsv holds the game
     * log and digest of the idle path for these seeds, printed by the shim before the scripted opponent.
     */
    @Test
    void aNullOpponentLeavesTodaysGameLogUnchanged() throws IOException {
        Map<Long, List<String>> pinned = new LinkedHashMap<>();
        try (InputStream in = ScriptedOpponentTest.class.getResourceAsStream("/logs/null-opponent-obeka.tsv")) {
            assertNotNull(in, "logs/null-opponent-obeka.tsv");
            for (String line : new String(in.readAllBytes(), StandardCharsets.UTF_8).split("\r?\n")) {
                String[] cells = line.split("\t", 3);
                pinned.computeIfAbsent(Long.parseLong(cells[1]), s -> new ArrayList<>()).add(cells[0] + "\t" + cells[2]);
            }
        }

        assertEquals(List.of(7L, 8L, 121L), List.copyOf(pinned.keySet()));
        for (Map.Entry<Long, List<String>> seed : pinned.entrySet()) {
            Messages.StartGame idle = new Messages.StartGame("start_game", TestGames.obeka().deckId(), seed.getKey(), 4,
                    40, true, true, 1, false, true);
            assertNull(idle.opponent());
            Messages.GameOver over = TestGames.play(idle, TestGames.obeka(), TestGames.first());

            List<String> today = new ArrayList<>();
            today.add("over\t" + over.reason() + "\t" + over.digest());
            for (Messages.LogEntry e : over.log()) {
                today.add("log\t" + e.turn() + "\t" + e.step() + "\t" + e.message());
            }
            assertEquals(seed.getValue(), today, "seed " + seed.getKey());
        }
    }

    @Test
    void theSeatsDrawTheSameObjectIds() {
        List<CardInfo> cards = OpponentCards.resolve(TABLE.cards());

        RandomUtil.setSeed(42);
        UUID idle = GameRunner.opponent(null).getId();
        int idleNext = RandomUtil.nextInt();
        RandomUtil.setSeed(42);
        IdlePlayer scripted = GameRunner.opponent(cards);
        int scriptedNext = RandomUtil.nextInt();
        GameRunner.reseed();

        assertInstanceOf(ScriptedOpponent.class, scripted);
        assertEquals(idle, scripted.getId());
        assertEquals(idleNext, scriptedNext);
        assertEquals(IdlePlayer.NAME, scripted.getName());
    }

    @Test
    void theCardsResolveOncePerJvmInTheServicesOrder() {
        List<CardInfo> first = OpponentCards.resolve(TABLE.cards());
        List<CardInfo> again = OpponentCards.resolve(List.of("Rout", "Counterspell"));

        assertEquals(List.of("Counterspell", "Atomize", "Rout"), first.stream().map(CardInfo::getName).toList());
        assertSame(first.get(2), again.get(0));
        assertSame(first.get(0), again.get(1));
    }

    @Test
    void aCopyKeepsTheCards() {
        ScriptedOpponent seat = new ScriptedOpponent(IdlePlayer.NAME, OpponentCards.resolve(TABLE.cards()));

        ScriptedOpponent copy = seat.copy();

        assertEquals(seat.getId(), copy.getId());
        assertSame(seat.cards(), copy.cards());
    }

    @Test
    void anUnknownCardNameEndsTheGameAsAnEngineError() {
        Messages.StartGame start = TestGames.start(7, 2).withOpponent(new Messages.Opponent(List.of("Counterspell", "No Such Spell")));

        Messages.GameOver over = TestGames.play(start, TestGames.obeka(), TestGames.pass());

        assertEquals("engine_error", over.reason());
        assertTrue(over.error().contains("No Such Spell"), over.error());
        assertThrows(IllegalArgumentException.class, () -> OpponentCards.resolve(List.of()));
    }
}
