package mtgarchive.xmage;

import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;

class DeckIdTest {

    private static final Messages.LoadDeck DECK = new Messages.LoadDeck("load_deck",
            List.of("Obeka, Splitter of Seconds"),
            List.of(new Messages.DeckEntry("Sol Ring", 1), new Messages.DeckEntry("Island", 2)));

    @Test
    void theCanonicalFormSortsCardsAndHasNoWhitespace() {
        assertEquals("{\"type\":\"load_deck\",\"commanders\":[\"Obeka, Splitter of Seconds\"],"
                        + "\"cards\":[{\"name\":\"Island\",\"quantity\":2},{\"name\":\"Sol Ring\",\"quantity\":1}]}",
                DeckId.canonicalJson(DECK));
    }

    @Test
    void theDeckIdIsTheSha256OfTheCanonicalForm() {
        // printf '%s' '<canonical form above>' | sha256sum
        assertEquals("bb593980929d8fd2237f7c0c0744e3c775e64a39fa1c9dd46948d7d9acff5568", DeckId.of(DECK));
    }

    @Test
    void entryOrderDoesNotChangeTheDeckId() {
        Messages.LoadDeck reordered = new Messages.LoadDeck("load_deck", List.of("Obeka, Splitter of Seconds"),
                List.of(new Messages.DeckEntry("Island", 2), new Messages.DeckEntry("Sol Ring", 1)));
        assertEquals(DeckId.of(DECK), DeckId.of(reordered));
    }

    @Test
    void apostrophesAndAccentsStayLiteralUtf8() {
        Messages.LoadDeck deck = new Messages.LoadDeck("load_deck", List.of(),
                List.of(new Messages.DeckEntry("Lim-Dûl's Vault", 1)));
        assertEquals("{\"type\":\"load_deck\",\"commanders\":[],\"cards\":[{\"name\":\"Lim-Dûl's Vault\",\"quantity\":1}]}",
                DeckId.canonicalJson(deck));
        assertEquals("4b1a91e2d78428e994b579090c4d771cb2f517d2c8ee656d550a892b41651bee", DeckId.of(deck));
    }
}
