package mtgarchive.xmage;

import org.junit.jupiter.api.Test;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;

class FramingTest {

    @Test
    void aFrameIsABigEndianLengthThenUtf8Json() throws Exception {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        Framing.write(new DataOutputStream(bytes), "{\"a\":\"Lim-Dûl\"}");

        byte[] body = "{\"a\":\"Lim-Dûl\"}".getBytes(StandardCharsets.UTF_8);
        byte[] wire = bytes.toByteArray();
        assertEquals(4 + body.length, wire.length);
        assertArrayEquals(new byte[]{0, 0, 0, (byte) body.length}, java.util.Arrays.copyOf(wire, 4));
    }

    @Test
    void framesRoundTripInOrderAndEndCleanly() throws Exception {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        DataOutputStream out = new DataOutputStream(bytes);
        Messages.LoadDeck deck = new Messages.LoadDeck("load_deck", List.of("Obeka, Splitter of Seconds"),
                List.of(new Messages.DeckEntry("Lim-Dûl's Vault", 1)));
        Framing.write(out, Wire.GSON.toJson(new Messages.Hello("hello", 1)));
        Framing.write(out, Wire.GSON.toJson(deck));

        DataInputStream in = new DataInputStream(new ByteArrayInputStream(bytes.toByteArray()));
        assertEquals("hello", Wire.typeOf(Wire.parse(Framing.read(in))));
        assertEquals(deck, Wire.GSON.fromJson(Wire.parse(Framing.read(in)), Messages.LoadDeck.class));
        assertNull(Framing.read(in));
    }

    @Test
    void anOversizedLengthIsRefused() {
        byte[] wire = {0x7f, 0, 0, 0};
        DataInputStream in = new DataInputStream(new ByteArrayInputStream(wire));
        assertThrows(IOException.class, () -> Framing.read(in));
    }

    @Test
    void wireNamesAreSnakeCaseAndNullsAreWritten() {
        String json = Wire.GSON.toJson(new Messages.GameOver("game_over", 3, "horizon", "0123456789abcdef", null, null, null));
        assertEquals("{\"type\":\"game_over\",\"turns\":3,\"reason\":\"horizon\",\"digest\":\"0123456789abcdef\",\"error\":null,\"log\":null,\"view\":null}", json);
        assertEquals(Messages.Reply.ofDo(2, "stop"), Wire.GSON.fromJson("{\"seq\":2,\"do\":\"stop\"}", Messages.Reply.class));
        assertEquals(Messages.Reply.ofAction(7, null), Wire.GSON.fromJson("{\"seq\":7,\"action\":null}", Messages.Reply.class));
    }
}
