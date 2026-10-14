package mtgarchive.xmage;

import mage.cards.decks.DeckCardLists;

import java.util.List;
import java.util.Map;

/**
 * One resolved load_deck.
 *
 * @param deckId        the deck_id (see {@link DeckId})
 * @param lists         XMage's deck lists: the 99 in cards, the commanders in the sideboard
 * @param unimplemented the decklist names XMage has no card for, sorted
 * @param decklistNames XMage card name to decklist name, for every card XMage has
 * @param blanks        the cards of the 99 that XMage has no card for, by name: each copy plays as a {@link BlankCard}
 */
public record LoadedDeck(String deckId, DeckCardLists lists, List<String> unimplemented,
                         Map<String, String> decklistNames, List<Messages.DeckEntry> blanks) {

    /** The decklist name of the card XMage calls {@code xmageName}; the XMage name for a card outside the deck. */
    public String decklistName(String xmageName) {
        return decklistNames.getOrDefault(xmageName, xmageName);
    }

    /** The XMage name of a decklist name, or null when the deck has no such card. */
    public String xmageName(String decklistName) {
        for (Map.Entry<String, String> e : decklistNames.entrySet()) {
            if (e.getValue().equals(decklistName)) {
                return e.getKey();
            }
        }
        return null;
    }

    /** Every decklist name XMage implements, sorted. */
    public List<String> names() {
        return decklistNames.values().stream().distinct().sorted().toList();
    }

    /** True for the decklist name of a blank card. */
    public boolean isBlank(String decklistName) {
        for (Messages.DeckEntry blank : blanks) {
            if (blank.name().equals(decklistName)) {
                return true;
            }
        }
        return false;
    }
}
