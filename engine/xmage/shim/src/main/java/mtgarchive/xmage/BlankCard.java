package mtgarchive.xmage;

import mage.cards.CardImpl;
import mage.cards.CardSetInfo;
import mage.constants.CardType;
import mage.constants.Rarity;

import java.util.UUID;

/**
 * A blank card: the stand-in for a decklist card that XMage does not implement. It keeps that
 * card's library slot and its decklist name. It has no mana cost and no abilities, and the action
 * list never offers it, so it is never cast. (XMage gives an Instant a spell ability that
 * ActionList hides; "no abilities" here means none the pilot can use or see listed.)
 */
public final class BlankCard extends CardImpl {

    /** Not a real set: no card repository lookup ever finds a blank. */
    static final String SET_CODE = "MTGA-BLANK";

    public BlankCard(UUID ownerId, String decklistName) {
        super(ownerId, new CardSetInfo(decklistName, SET_CODE, "0", Rarity.COMMON), new CardType[]{CardType.INSTANT}, "");
    }

    private BlankCard(final BlankCard card) {
        super(card);
    }

    @Override
    public BlankCard copy() {
        return new BlankCard(this);
    }
}
