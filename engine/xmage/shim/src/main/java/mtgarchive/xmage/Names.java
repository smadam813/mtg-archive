package mtgarchive.xmage;

import mage.MageObject;
import mage.cards.Card;
import mage.cards.DoubleFacedCardHalf;
import mage.game.Game;
import mage.game.permanent.Permanent;
import mage.game.permanent.PermanentCard;
import mage.game.stack.Spell;

import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.UUID;

/** Decklist names and faces for XMage objects: a card is named by its full decklist name on the wire. */
public final class Names {

    private final LoadedDeck deck;

    public Names(LoadedDeck deck) {
        this.deck = deck;
    }

    /** The decklist name of the card behind an object: a card, a half, a permanent or a spell. */
    public String card(MageObject object) {
        if (object == null) {
            return "?";
        }
        Card card = cardOf(object);
        return deck.decklistName(card != null ? card.getMainCard().getName() : object.getName());
    }

    public String card(Game game, UUID id) {
        return card(game.getObject(id));
    }

    public List<String> cards(Collection<? extends Card> cards) {
        List<String> out = new ArrayList<>(cards.size());
        for (Card c : cards) {
            out.add(card(c));
        }
        return out;
    }

    /** "back" for a modal double-faced card's back half and a transformed permanent; "front" otherwise. */
    public static String face(MageObject object) {
        if (object instanceof Permanent && ((Permanent) object).isTransformed()) {
            return "back";
        }
        Card card = cardOf(object);
        if (card instanceof DoubleFacedCardHalf && ((DoubleFacedCardHalf) card).isBackSide()) {
            return "back";
        }
        return "front";
    }

    static Card cardOf(MageObject object) {
        if (object instanceof PermanentCard) {
            return ((PermanentCard) object).getCard();
        }
        if (object instanceof Spell) {
            return ((Spell) object).getCard();
        }
        if (object instanceof Card) {
            return (Card) object;
        }
        return null;
    }
}
