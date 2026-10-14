package mtgarchive.xmage;

import com.google.gson.JsonObject;

import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.net.Socket;

/** A service stand-in on a real socket, for connection tests. */
final class TestClient implements AutoCloseable {

    private final Socket socket;
    private final DataInputStream in;
    private final DataOutputStream out;

    TestClient(int port) throws IOException {
        socket = new Socket("127.0.0.1", port);
        socket.setTcpNoDelay(true);
        in = new DataInputStream(socket.getInputStream());
        out = new DataOutputStream(socket.getOutputStream());
    }

    void send(Object message) throws IOException {
        Framing.write(out, Wire.GSON.toJson(message));
    }

    /** Sends a frame body as is, for tests of frames that are not wire messages. */
    void sendRaw(String body) throws IOException {
        Framing.write(out, body);
    }

    /** The next message; SocketTimeoutException when none arrives in time. */
    JsonObject receive(int timeoutMs) throws IOException {
        socket.setSoTimeout(timeoutMs);
        byte[] frame = Framing.read(in);
        if (frame == null) {
            throw new IOException("the sidecar closed the connection");
        }
        return Wire.parse(frame);
    }

    /** Answers every decide and event with the decider until game_over, and returns game_over. */
    JsonObject play(Decider decider) throws IOException {
        while (true) {
            JsonObject message = receive(60_000);
            if ("game_over".equals(Wire.typeOf(message))) {
                return message;
            }
            send(decider.ask(WireContract.inGame(message)));
        }
    }

    @Override
    public void close() throws IOException {
        socket.close();
    }
}
