package mtgarchive.xmage;

import com.google.gson.JsonObject;
import com.google.gson.JsonParseException;

import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.net.Socket;
import java.net.SocketTimeoutException;
import java.util.List;
import java.util.concurrent.Semaphore;

/**
 * One service connection: hello, load_deck and start_game in any order, one game at a time. A game
 * that fails ends with engine_error; the connection then takes the next start_game.
 */
final class Connection implements Runnable {

    private final Socket socket;
    private final ShimConfig config;
    private final DeckLibrary decks;
    private final Semaphore games;
    private final String version;
    private DataInputStream in;
    private DataOutputStream out;

    Connection(Socket socket, ShimConfig config, DeckLibrary decks, Semaphore games, String version) {
        this.socket = socket;
        this.config = config;
        this.decks = decks;
        this.games = games;
        this.version = version;
    }

    @Override
    public void run() {
        try (Socket s = socket) {
            in = new DataInputStream(new BufferedInputStream(s.getInputStream(), 1 << 16));
            out = new DataOutputStream(new BufferedOutputStream(s.getOutputStream(), 1 << 16));
            while (true) {
                s.setSoTimeout(0);
                byte[] frame = Framing.read(in);
                if (frame == null) {
                    return;
                }
                JsonObject message;
                String type;
                try {
                    message = Wire.parse(frame);
                    type = Wire.typeOf(message);
                } catch (JsonParseException | IllegalStateException e) {
                    // A frame that is not a JSON object: the framing still holds, so answer and read on.
                    send(GameCtx.errorOver("bad frame: " + GameCtx.firstLine(e, null)));
                    continue;
                }
                if (type == null) {
                    continue; // a reply that arrived after its game ended (decision timeout): drop it
                }
                try {
                    handle(type, message);
                } catch (RuntimeException e) {
                    // A message the shim cannot act on (a start_game without deck_id, a bad load_deck):
                    // answer engine_error and keep the connection.
                    send(GameCtx.errorOver(type + " failed: " + GameCtx.firstLine(e, null)));
                }
            }
        } catch (IOException e) {
            System.err.println("XMAGE connection closed: " + e);
        }
    }

    /** Answers one message from the service. */
    private void handle(String type, JsonObject message) throws IOException {
        switch (type) {
            case "hello":
                send(new Messages.HelloReply("hello", "xmage", version, config.threads(), Messages.PROTOCOL));
                break;
            case "load_deck":
                LoadedDeck deck = decks.load(Wire.GSON.fromJson(message, Messages.LoadDeck.class));
                send(new Messages.DeckReply("deck", deck.deckId(), deck.unimplemented()));
                break;
            case "start_game":
                if (!message.has("opponent")) {
                    // Protocol 2 requires the member; null is the idle opponent.
                    send(GameCtx.errorOver("start_game needs an opponent member (null for the idle opponent)"));
                    break;
                }
                send(play(Wire.GSON.fromJson(message, Messages.StartGame.class)));
                break;
            case "scenario":
                send(scenario(Wire.GSON.fromJson(message, Messages.Scenario.class)));
                break;
            default:
                send(GameCtx.errorOver("unknown message type " + type));
        }
    }

    /**
     * Plays one game on this thread: the server names every connection thread GAME... and gives it
     * a 64 MB stack (see {@link Server#serve()}), as XMage game code requires.
     */
    private Messages.GameOver play(Messages.StartGame start) {
        if (start.deckId() == null) {
            return GameCtx.errorOver("start_game needs a deck_id");
        }
        LoadedDeck deck = decks.get(start.deckId());
        if (deck == null) {
            return GameCtx.errorOver("unknown deck_id " + start.deckId() + "; send load_deck first");
        }
        games.acquireUninterruptibly();
        try {
            return GameRunner.play(start, deck, this::ask);
        } finally {
            games.release();
        }
    }

    /** Plays one scenario on this thread, in a game slot like any game. */
    private Messages.ScenarioResult scenario(Messages.Scenario scenario) {
        if (scenario.start() == null || scenario.cards() == null) {
            return ScenarioRunner.error("a scenario needs start and cards", List.of(), List.of());
        }
        games.acquireUninterruptibly();
        try {
            return ScenarioRunner.run(scenario, decks);
        } finally {
            games.release();
        }
    }

    /** The socket side of a Decider: write the message, then wait for the reply with the same seq. */
    private Messages.Reply ask(Messages.InGame message) {
        try {
            send(message);
            socket.setSoTimeout(config.decisionTimeoutMs());
            byte[] frame = Framing.read(in);
            if (frame == null) {
                throw new DecisionFailure("the service closed the connection at seq " + message.seq());
            }
            JsonObject json = Wire.parse(frame);
            String type = Wire.typeOf(json);
            if (type != null) {
                throw new DecisionFailure("expected the reply to seq " + message.seq() + ", got a " + type + " message");
            }
            Messages.Reply reply = Wire.GSON.fromJson(json, Messages.Reply.class);
            if (reply.seq() == null || reply.seq() != message.seq()) {
                throw new DecisionFailure("expected the reply to seq " + message.seq() + ", got seq " + reply.seq());
            }
            return reply;
        } catch (SocketTimeoutException e) {
            throw new DecisionFailure("no reply to seq " + message.seq() + " within " + config.decisionTimeoutMs() + " ms");
        } catch (IOException | JsonParseException | IllegalStateException e) {
            throw new DecisionFailure("connection failed at seq " + message.seq() + ": " + e);
        }
    }

    private void send(Object message) throws IOException {
        Framing.write(out, Wire.GSON.toJson(message));
    }
}
