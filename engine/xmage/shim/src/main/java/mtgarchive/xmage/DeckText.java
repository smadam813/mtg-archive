package mtgarchive.xmage;

import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/** Reads the spike's deck text format: "# comment", "COMMANDER: name", "1 name". */
public final class DeckText {

    private DeckText() {
    }

    public static Messages.LoadDeck parse(String text) {
        List<String> commanders = new ArrayList<>();
        List<Messages.DeckEntry> cards = new ArrayList<>();
        for (String raw : text.split("\n")) {
            String line = raw.trim();
            if (line.isEmpty() || line.startsWith("#")) {
                continue;
            }
            if (line.startsWith("COMMANDER:")) {
                commanders.add(line.substring("COMMANDER:".length()).trim());
            } else {
                int space = line.indexOf(' ');
                cards.add(new Messages.DeckEntry(line.substring(space + 1).trim(),
                        Integer.parseInt(line.substring(0, space))));
            }
        }
        return new Messages.LoadDeck("load_deck", commanders, cards);
    }

    /** Reads a deck from the classpath, for example "decks/obeka.txt". */
    public static Messages.LoadDeck resource(String path) {
        try (InputStream in = DeckText.class.getClassLoader().getResourceAsStream(path)) {
            if (in == null) {
                throw new IllegalArgumentException("no classpath resource " + path);
            }
            return parse(new String(in.readAllBytes(), StandardCharsets.UTF_8));
        } catch (IOException e) {
            throw new IllegalStateException(e);
        }
    }
}
