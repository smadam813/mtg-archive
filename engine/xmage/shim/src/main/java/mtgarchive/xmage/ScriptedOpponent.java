package mtgarchive.xmage;

import mage.ApprovingObject;
import mage.Mana;
import mage.abilities.Ability;
import mage.abilities.SpellAbility;
import mage.abilities.costs.mana.ManaCost;
import mage.cards.Card;
import mage.cards.repository.CardInfo;
import mage.constants.Outcome;
import mage.game.Game;
import mage.game.stack.StackObject;
import mage.target.Target;

import java.io.Serializable;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/**
 * The scripted opponent of interactive disruption (ADR 0013): the idle opponent with its disruption cards. It keeps
 * the idle opponent's name, deck and start life. It acts only through the disruption profile, so with nothing due it
 * plays exactly as the idle opponent. Its constructor draws no object id beyond the idle opponent's. OpponentCards
 * resolves the cards before the run seed is set, and no card object exists until a cast creates one.
 * <p>
 * Our seat casts its cards for it ({@link Opposition}). The window is the time while its disruption card is being
 * cast or is on the stack. In the window it adds exactly the unpaid cost of each payment to its pool. It accepts every
 * "pay?" question, so it pays its card's cost, ward and every "unless its controller pays" tax. While its card is
 * being cast, it takes the target the service proposed. It refuses every cast of a disruption card outside the current proposal (a recast
 * that Delay offers). Outside the window it behaves as the idle opponent.
 */
public class ScriptedOpponent extends IdlePlayer {

    /** A payment loop that adds mana this often without paying the cost stops: the payment fails. */
    static final int MAX_PAYMENTS = 32;

    private final List<CardInfo> cards;

    /** The current proposal, shared by every copy of this seat (XMage copies players on bookmark and restore). */
    private final Window window;

    public ScriptedOpponent(String name, List<CardInfo> cards) {
        super(name);
        this.cards = List.copyOf(cards);
        this.window = new Window();
    }

    public ScriptedOpponent(final ScriptedOpponent player) {
        super(player);
        this.cards = player.cards;
        this.window = player.window;
    }

    @Override
    public ScriptedOpponent copy() {
        return new ScriptedOpponent(this);
    }

    /** The disruption cards the service named in start_game, in its order. */
    List<CardInfo> cards() {
        return cards;
    }

    /** The card of this name among the disruption cards; null when the service did not name it. */
    CardInfo card(String name) {
        for (CardInfo info : cards) {
            if (info.getName().equals(name)) {
                return info;
            }
        }
        return null;
    }

    Window window() {
        return window;
    }

    /** True while the proposed disruption card is on the stack: from its cast until it leaves the stack. */
    boolean inWindow(Game game) {
        UUID card = window.card;
        if (card == null) {
            return false;
        }
        for (StackObject o : game.getStack()) {
            if (card.equals(o.getSourceId())) {
                return true;
            }
        }
        return false;
    }

    /**
     * In the window: adds exactly the unpaid cost to the pool, and XMage then pays it from the pool. Generic mana
     * comes as colorless. Outside the window: the idle opponent, which has no mana.
     */
    @Override
    public boolean playMana(Ability ability, ManaCost unpaid, String promptText, Game game) {
        if (!inWindow(game)) {
            return super.playMana(ability, unpaid, promptText, game);
        }
        if (++window.payments > MAX_PAYMENTS) {
            return false;
        }
        Mana mana = exactly(unpaid.getMana());
        if (mana.count() == 0) {
            return false;
        }
        getManaPool().addMana(mana, game, ability);
        return true;
    }

    /** The mana that pays {@code cost} exactly: its colored symbols, and its generic part as colorless mana. */
    static Mana exactly(Mana cost) {
        Mana mana = new Mana();
        mana.setWhite(cost.getWhite());
        mana.setBlue(cost.getBlue());
        mana.setBlack(cost.getBlack());
        mana.setRed(cost.getRed());
        mana.setGreen(cost.getGreen());
        mana.setColorless(cost.getColorless() + cost.getGeneric());
        return mana;
    }

    /** In the window every "pay?" question gets yes: the opponent pays ward and every tax. */
    @Override
    public boolean chooseUse(Outcome outcome, String message, String secondMessage, String trueText, String falseText,
                             Ability source, Game game) {
        if (inWindow(game)) {
            return true;
        }
        return super.chooseUse(outcome, message, secondMessage, trueText, falseText, source, game);
    }

    /** While its card is being cast, the card takes the proposed target, or no target at all. */
    @Override
    public boolean chooseTarget(Outcome outcome, Target target, Ability source, Game game) {
        UUID forced = window.target;
        if (forced != null && source != null && source.getSourceId().equals(window.card)) {
            if (target.contains(forced) || !target.canTarget(getId(), forced, source, game)) {
                return false;
            }
            target.addTarget(forced, source, game);
            return true;
        }
        return super.chooseTarget(outcome, target, source, game);
    }

    /**
     * A choice its disruption card makes as it resolves chooses nothing when it may: Atomize's proliferate gives no
     * permanent and no player another counter (ADR 0013). A choice that needs a target goes to the idle opponent.
     */
    @Override
    public boolean choose(Outcome outcome, Target target, Ability source, Game game, Map<String, Serializable> options) {
        if (window.card != null && source != null && window.card.equals(source.getSourceId())
                && target.getMinNumberOfTargets() == 0) {
            return false;
        }
        return super.choose(outcome, target, source, game, options);
    }

    /** The cast guard: a disruption card is cast only as the current proposal, never again (a Delay recast). */
    @Override
    public boolean cast(SpellAbility ability, Game game, boolean noMana, ApprovingObject approvingObject) {
        Card card = ability == null ? null : game.getCard(ability.getSourceId());
        if (card != null && isDisruptionCard(card) && !card.getMainCard().getId().equals(window.casting)) {
            return false;
        }
        return super.cast(ability, game, noMana, approvingObject);
    }

    /** True when the card is one of the disruption cards, by name. */
    boolean isDisruptionCard(Card card) {
        return card(card.getMainCard().getName()) != null;
    }

    /**
     * The current proposal. {@code card} is the disruption card's object id from its creation until it settles;
     * {@code casting} is the same id while Opposition casts it, so the cast guard lets that one cast through;
     * {@code target} is the proposed target while the card is cast.
     */
    static final class Window {
        UUID card;
        UUID casting;
        UUID target;
        int payments;

        void open(UUID cardId, UUID targetId) {
            card = cardId;
            casting = cardId;
            target = targetId;
            payments = 0;
        }

        /** The cast is over: the target and the cast pass go; the window stays open while the card is on the stack. */
        void castDone() {
            casting = null;
            target = null;
        }

        void close() {
            card = null;
            casting = null;
            target = null;
            payments = 0;
        }
    }
}
