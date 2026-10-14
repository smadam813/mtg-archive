package mtgarchive.xmage;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HexFormat;
import java.util.List;

/**
 * The deck_id rule (roadmap, "Wire protocol"): the lowercase hex SHA-256 of the canonical load_deck
 * message. Canonical: commanders sorted, cards sorted by name (then quantity), fields in the order
 * type, commanders, cards, no whitespace, no HTML escaping, UTF-8. The service treats the id as opaque.
 */
public final class DeckId {

    private static final Gson CANONICAL = new GsonBuilder().disableHtmlEscaping().create();

    private DeckId() {
    }

    public static Messages.LoadDeck canonical(Messages.LoadDeck request) {
        List<String> commanders = new ArrayList<>(request.commanders() == null ? List.of() : request.commanders());
        commanders.sort(Comparator.naturalOrder());
        List<Messages.DeckEntry> cards = new ArrayList<>(request.cards() == null ? List.of() : request.cards());
        cards.sort(Comparator.comparing(Messages.DeckEntry::name).thenComparingInt(Messages.DeckEntry::quantity));
        return new Messages.LoadDeck("load_deck", List.copyOf(commanders), List.copyOf(cards));
    }

    public static String canonicalJson(Messages.LoadDeck request) {
        return CANONICAL.toJson(canonical(request));
    }

    public static String of(Messages.LoadDeck request) {
        try {
            byte[] hash = MessageDigest.getInstance("SHA-256")
                    .digest(canonicalJson(request).getBytes(StandardCharsets.UTF_8));
            return HexFormat.of().formatHex(hash);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }
}
