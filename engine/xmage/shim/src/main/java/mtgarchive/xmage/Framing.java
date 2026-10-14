package mtgarchive.xmage;

import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.EOFException;
import java.io.IOException;
import java.nio.charset.StandardCharsets;

/** Wire framing: a 4-byte big-endian length, then that many bytes of UTF-8 JSON. */
public final class Framing {

    /** A longer frame is a broken peer, not a message. */
    public static final int MAX_FRAME = 16 * 1024 * 1024;

    private Framing() {
    }

    /** Reads one frame body, or returns null when the stream ends cleanly before a frame starts. */
    public static byte[] read(DataInputStream in) throws IOException {
        int length;
        try {
            length = in.readInt();
        } catch (EOFException e) {
            return null;
        }
        if (length < 0 || length > MAX_FRAME) {
            throw new IOException("frame length " + length + " is outside 0.." + MAX_FRAME);
        }
        byte[] body = new byte[length];
        in.readFully(body);
        return body;
    }

    /** Writes one frame and flushes it. */
    public static void write(DataOutputStream out, String json) throws IOException {
        byte[] body = json.getBytes(StandardCharsets.UTF_8);
        out.writeInt(body.length);
        out.write(body);
        out.flush();
    }
}
