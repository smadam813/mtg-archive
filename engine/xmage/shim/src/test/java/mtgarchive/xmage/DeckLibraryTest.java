package mtgarchive.xmage;

import mage.cards.decks.DeckCardInfo;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertSame;
import static org.junit.jupiter.api.Assertions.assertTrue;

class DeckLibraryTest {

    @BeforeAll
    static void warm() {
        Engine.warmUp();
    }

    @Test
    void aCardXmageLacksIsUnimplementedAndTheRestLoads() {
        LoadedDeck deck = new DeckLibrary().load(new Messages.LoadDeck("load_deck",
                List.of("Obeka, Brute Chronologist"),
                List.of(new Messages.DeckEntry("Not A Real Card", 1), new Messages.DeckEntry("Island", 10))));

        assertEquals(List.of("Not A Real Card"), deck.unimplemented());
        assertEquals(List.of("Island", "Obeka, Brute Chronologist"), deck.names());
    }

    @Test
    void multiFaceAndSplitCardsResolveByTheirFullName() {
        LoadedDeck deck = new DeckLibrary().load(new Messages.LoadDeck("load_deck", List.of(), List.of(
                new Messages.DeckEntry("Shatterskull Smashing // Shatterskull, the Hammer Pass", 1),
                new Messages.DeckEntry("Fire // Ice", 1))));

        assertEquals(List.of(), deck.unimplemented());
        assertEquals("Shatterskull Smashing", deck.xmageName("Shatterskull Smashing // Shatterskull, the Hammer Pass"));
        assertEquals("Shatterskull Smashing // Shatterskull, the Hammer Pass", deck.decklistName("Shatterskull Smashing"));
        assertEquals("Fire // Ice", deck.decklistName("Fire // Ice"));
    }

    @Test
    void entryOrderGivesTheSameCachedDeck() {
        DeckLibrary library = new DeckLibrary();
        Messages.LoadDeck obeka = DeckText.resource("decks/obeka.txt");
        List<Messages.DeckEntry> reversed = new ArrayList<>(obeka.cards());
        Collections.reverse(reversed);

        LoadedDeck first = library.load(obeka);
        LoadedDeck second = library.load(new Messages.LoadDeck("load_deck", obeka.commanders(), reversed));

        assertSame(first, second);
        assertSame(first, library.get(first.deckId()));
    }

    @Test
    void commandersGoToTheSideboardAndTheNinetyNineToTheDeck() {
        LoadedDeck deck = new DeckLibrary().load(DeckText.resource("decks/obeka.txt"));

        assertEquals(List.of("Obeka, Brute Chronologist"),
                deck.lists().getSideboard().stream().map(DeckCardInfo::getCardName).toList());
        assertEquals(99, deck.lists().getCards().stream().mapToInt(DeckCardInfo::getAmount).sum());
        assertTrue(deck.unimplemented().isEmpty());
    }
}
