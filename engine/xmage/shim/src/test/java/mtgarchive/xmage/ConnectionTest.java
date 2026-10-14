package mtgarchive.xmage;

import com.google.gson.JsonObject;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.net.SocketTimeoutException;
import java.nio.file.Path;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** One sidecar with one game slot and a 500 ms decision timeout, driven over real sockets. */
class ConnectionTest {

    private static Server server;
    private static String deckId;

    @BeforeAll
    static void start() throws IOException {
        Engine.warmUp();
        server = new Server(new ShimConfig(0, 1, 500, Path.of("/tmp")), new DeckLibrary(), "xmage-test+00000000")
                .startInBackground();
        try (TestClient c = new TestClient(server.port())) {
            c.send(DeckText.resource("decks/obeka.txt"));
            deckId = c.receive(60_000).get("deck_id").getAsString();
        }
    }

    @AfterAll
    static void stop() throws IOException {
        server.close();
    }

    private static Messages.StartGame start(long seed) {
        return new Messages.StartGame("start_game", deckId, seed, 2, 40, true, true, 1, false, false);
    }

    @Test
    void helloReportsEngineVersionThreadsAndProtocol() throws IOException {
        try (TestClient c = new TestClient(server.port())) {
            c.send(new Messages.Hello("hello", Messages.PROTOCOL));
            assertEquals(new Messages.HelloReply("hello", "xmage", "xmage-test+00000000", 1, 2),
                    Wire.GSON.fromJson(c.receive(5_000), Messages.HelloReply.class));
        }
    }

    @Test
    void loadDeckRepliesWithTheDeckIdAndUnimplementedCards() throws IOException {
        try (TestClient c = new TestClient(server.port())) {
            c.send(DeckText.resource("decks/obeka.txt"));
            JsonObject deck = c.receive(60_000);
            assertEquals("deck", Wire.typeOf(deck));
            assertEquals(deckId, deck.get("deck_id").getAsString());
            assertEquals(0, deck.getAsJsonArray("unimplemented").size());
        }
    }

    @Test
    void aGamePlaysToGameOverOverTheSocket() throws IOException {
        try (TestClient c = new TestClient(server.port())) {
            c.send(start(7));
            JsonObject over = c.play(TestGames.pass());
            assertEquals("horizon", over.get("reason").getAsString());
            assertEquals(2, over.get("turns").getAsInt());
            assertEquals(TestGames.play(start(7), TestGames.obeka(), TestGames.pass()).digest(),
                    over.get("digest").getAsString());
        }
    }

    /** A scripted opponent with nothing due plays the idle opponent's game over the socket too. */
    @Test
    void aGameWithTheScriptedOpponentPlaysToGameOverOverTheSocket() throws IOException {
        try (TestClient c = new TestClient(server.port())) {
            c.send(start(7).withOpponent(ScriptedOpponentTest.TABLE));
            JsonObject over = c.play(TestGames.pass());
            assertEquals("horizon", over.get("reason").getAsString());
            assertEquals(TestGames.play(start(7), TestGames.obeka(), TestGames.pass()).digest(),
                    over.get("digest").getAsString());
        }
    }

    /** Protocol 2 requires the opponent member; a start_game without it plays no game. */
    @Test
    void aStartGameWithoutAnOpponentMemberEndsWithEngineErrorAndTheConnectionPlaysOn() throws IOException {
        try (TestClient c = new TestClient(server.port())) {
            JsonObject noOpponent = Wire.GSON.toJsonTree(start(7)).getAsJsonObject();
            assertTrue(noOpponent.has("opponent"));
            noOpponent.remove("opponent");
            c.send(noOpponent);
            JsonObject over = c.receive(5_000);
            assertEquals("engine_error", over.get("reason").getAsString());
            assertTrue(over.get("error").getAsString().contains("opponent"), over.get("error").getAsString());

            c.send(start(7));
            assertEquals("horizon", c.play(TestGames.pass()).get("reason").getAsString());
        }
    }

    @Test
    void anUnknownDeckIdEndsWithEngineError() throws IOException {
        try (TestClient c = new TestClient(server.port())) {
            c.send(new Messages.StartGame("start_game", "nope", 7, 2, 40, true, true, 1, false, false));
            JsonObject over = c.receive(5_000);
            assertEquals("engine_error", over.get("reason").getAsString());
            assertTrue(over.get("error").getAsString().contains("unknown deck_id"));
        }
    }

    @Test
    void aMessageTheShimCannotActOnEndsWithEngineErrorAndTheConnectionPlaysOn() throws IOException {
        try (TestClient c = new TestClient(server.port())) {
            JsonObject noDeck = Wire.GSON.toJsonTree(start(7)).getAsJsonObject();
            noDeck.remove("deck_id");
            c.send(noDeck);
            JsonObject over = c.receive(5_000);
            assertEquals("engine_error", over.get("reason").getAsString());
            assertEquals("start_game needs a deck_id", over.get("error").getAsString());

            JsonObject badDeck = Wire.GSON.toJsonTree(DeckText.resource("decks/obeka.txt")).getAsJsonObject();
            badDeck.getAsJsonArray("cards").get(0).getAsJsonObject().remove("name");
            c.send(badDeck);
            over = c.receive(5_000);
            assertEquals("engine_error", over.get("reason").getAsString());
            assertTrue(over.get("error").getAsString().startsWith("load_deck failed: "), over.get("error").getAsString());

            c.send(start(7));
            assertEquals("horizon", c.play(TestGames.pass()).get("reason").getAsString());
        }
    }

    @Test
    void aFrameThatIsNotAJsonObjectEndsWithEngineErrorAndTheConnectionPlaysOn() throws IOException {
        try (TestClient c = new TestClient(server.port())) {
            for (String body : new String[] {"{\"type\": ", "[1, 2]"}) {
                c.sendRaw(body);
                JsonObject over = c.receive(5_000);
                assertEquals("engine_error", over.get("reason").getAsString());
                assertTrue(over.get("error").getAsString().startsWith("bad frame: "), over.get("error").getAsString());
            }

            c.send(start(7));
            assertEquals("horizon", c.play(TestGames.pass()).get("reason").getAsString());
        }
    }

    @Test
    void aDecisionTimeoutEndsTheGameAndTheConnectionPlaysOn() throws IOException {
        try (TestClient c = new TestClient(server.port())) {
            c.send(start(9));
            JsonObject keep = c.receive(10_000);
            assertEquals("keep", keep.get("kind").getAsString());
            JsonObject over = c.receive(5_000); // no answer: the 500 ms timeout ends the game
            assertEquals("engine_error", over.get("reason").getAsString());
            assertEquals("no reply to seq 1 within 500 ms", over.get("error").getAsString());

            c.send(Messages.Reply.ofKeep(1, true)); // the late reply: dropped
            c.send(start(9));
            assertEquals("horizon", c.play(TestGames.pass()).get("reason").getAsString());
        }
    }

    @Test
    void aGameBeyondTheThreadCapWaitsForAFreeSlot() throws IOException {
        // a wider decision timeout than the class-level 500 ms: b.send plus b.receive(200) must
        // finish inside a's own timeout window, with margin, so a loaded CI host cannot flake this.
        try (Server wide = new Server(new ShimConfig(0, 1, 1500, Path.of("/tmp")), new DeckLibrary(),
                "xmage-test+00000000").startInBackground()) {
            String wideDeckId;
            try (TestClient loader = new TestClient(wide.port())) {
                loader.send(DeckText.resource("decks/obeka.txt"));
                wideDeckId = loader.receive(60_000).get("deck_id").getAsString();
            }
            Messages.StartGame wideStart = new Messages.StartGame("start_game", wideDeckId, 9, 2, 40, true, true, 1, false, false);
            try (TestClient a = new TestClient(wide.port()); TestClient b = new TestClient(wide.port())) {
                a.send(wideStart);
                assertEquals("keep", a.receive(10_000).get("kind").getAsString()); // a holds the only slot
                b.send(wideStart);
                assertThrows(SocketTimeoutException.class, () -> b.receive(200));
                assertEquals("engine_error", a.receive(5_000).get("reason").getAsString()); // a times out
                assertEquals("horizon", b.play(TestGames.pass()).get("reason").getAsString());
            }
        }
    }

    @Test
    void unsupportedMessagesEndWithEngineErrorAndAClearText() throws IOException {
        try (TestClient c = new TestClient(server.port())) {
            JsonObject scenario = new JsonObject();
            scenario.addProperty("type", "scenario");
            c.send(scenario);
            assertTrue(c.receive(5_000).get("error").getAsString().contains("a scenario needs start and cards"));

            c.send(start(7));
            JsonObject over = c.play(m -> m instanceof Messages.EventEndOfTurn
                    ? Messages.Reply.ofDo(m.seq(), "counter") : TestGames.pass().ask(m));
            assertEquals("engine_error", over.get("reason").getAsString());
            assertTrue(over.get("error").getAsString().contains("the end_of_turn event takes do="),
                    over.get("error").getAsString());
        }
    }
}
