package mtgarchive.xmage;

import mage.cards.ModalDoubleFacedCard;
import mage.cards.repository.CardRepository;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;

class NamesTest {

    private static final String SHATTERSKULL = "Shatterskull Smashing // Shatterskull, the Hammer Pass";

    @BeforeAll
    static void warm() {
        Engine.warmUp();
    }

    @Test
    void aBackHalfIsTheBackFaceOfItsDecklistCard() {
        LoadedDeck deck = new DeckLibrary().load(new Messages.LoadDeck("load_deck", List.of(),
                List.of(new Messages.DeckEntry(SHATTERSKULL, 1))));
        Names names = new Names(deck);
        ModalDoubleFacedCard card = (ModalDoubleFacedCard) CardRepository.instance.findCards(SHATTERSKULL).get(0).createCard();

        assertEquals(SHATTERSKULL, names.card(card));
        assertEquals("front", Names.face(card));
        assertEquals(SHATTERSKULL, names.card(card.getLeftHalfCard()));
        assertEquals("front", Names.face(card.getLeftHalfCard()));
        assertEquals(SHATTERSKULL, names.card(card.getRightHalfCard()));
        assertEquals("back", Names.face(card.getRightHalfCard()));
    }

    @Test
    void aCardOutsideTheDeckKeepsItsXmageName() {
        LoadedDeck deck = new DeckLibrary().load(new Messages.LoadDeck("load_deck", List.of(),
                List.of(new Messages.DeckEntry("Island", 1))));
        assertEquals("Treasure", deck.decklistName("Treasure"));
    }
}
