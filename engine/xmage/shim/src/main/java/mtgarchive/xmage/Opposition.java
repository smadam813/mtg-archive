package mtgarchive.xmage;

import mage.abilities.Ability;
import mage.abilities.SpellAbility;
import mage.abilities.effects.Effect;
import mage.abilities.effects.common.CounterTargetEffect;
import mage.abilities.effects.common.DestroyTargetEffect;
import mage.abilities.keyword.FlashAbility;
import mage.abilities.keyword.IndestructibleAbility;
import mage.cards.Card;
import mage.cards.repository.CardInfo;
import mage.constants.TimingRule;
import mage.constants.Zone;
import mage.game.Game;
import mage.game.events.GameEvent;
import mage.game.permanent.Permanent;
import mage.game.stack.Spell;
import mage.players.Player;
import mage.target.Target;

import java.util.List;
import java.util.Set;
import java.util.UUID;

/**
 * The life cycle of one disruption card (ADR 0013). Our seat runs it inside its own priority, at a tier-3 fire
 * point. It takes a bookmark, creates one card and puts it into the scripted opponent's hand. It takes the first
 * proposed candidate that the card can legally target and can affect. Then the opponent casts the card at that
 * target. A card with no target (Rout) takes no candidate: its own text decides what it affects. The opponent casts
 * each card at instant speed, through its flash ability when it is a sorcery (Rout). Any failure restores the
 * bookmark, so no card stays in any zone, and the outcome is not cast at once. The shim names cards and ids only: it
 * knows no event kind.
 */
final class Opposition {

    /** The not-cast reasons on the wire. */
    static final String NO_TARGET = "no_target";

    static final String CANNOT_CAST = "cannot_cast";

    private Opposition() {
    }

    /**
     * One attempt. {@code card} is the card the attempt created (after a failure it is in no zone); {@code target}
     * is the candidate it aimed at, null with no target; {@code reason} is null on a cast.
     */
    record Attempt(Card card, UUID target, String reason) {

        boolean cast() {
            return reason == null;
        }
    }

    /**
     * Creates the card and casts it for the opponent at the first candidate it can target and affect; a card with no
     * target ignores the candidates. Every failure restores the game. The caller re-binds the spells it tracks
 * afterwards: a restore replaces the stack.
     */
    static Attempt attempt(Game game, ScriptedOpponent opponent, CardInfo info, List<UUID> candidates) {
        int bookmark = game.bookmarkState();
        Card card = info.createCard();
        game.loadCards(Set.of(card), opponent.getId());
        card.setZone(Zone.HAND, game);
        opponent.getHand().add(card);
        SpellAbility spell = instantSpeed(card, game);
        if (spell == null) {
            game.restoreState(bookmark, "disruption card: no instant-speed cast");
            return new Attempt(card, null, CANNOT_CAST);
        }
        // A card with no target (Rout) takes no candidate: its own text decides what it affects.
        boolean targeted = !spell.getTargets().isEmpty();
        UUID target = null;
        for (UUID candidate : targeted ? candidates : List.<UUID>of()) {
            if (canTarget(game, opponent, spell, candidate) && canAffect(game, spell, candidate)) {
                target = candidate;
                break;
            }
        }
        if (targeted && target == null) {
            game.restoreState(bookmark, "disruption card: no target");
            return new Attempt(card, null, NO_TARGET);
        }
        ScriptedOpponent.Window window = opponent.window();
        window.open(card.getId(), target);
        boolean cast;
        try {
            cast = opponent.cast(spell, game, false, null) && aimsAt(game, card, target);
        } finally {
            window.castDone();
        }
        if (!cast) {
            window.close();
            game.restoreState(bookmark, "disruption card: cannot cast");
            return new Attempt(card, target, CANNOT_CAST);
        }
        game.removeBookmark(bookmark);
        return new Attempt(card, target, null);
    }

    /** True when the card's spell ability has a target that accepts the candidate (hexproof, shroud and protection fail). */
    static boolean canTarget(Game game, Player opponent, SpellAbility spell, UUID candidate) {
        if (spell.getTargets().isEmpty()) {
            return false;
        }
        Target target = spell.getTargets().get(0).copy();
        return target.canTarget(opponent.getId(), candidate, spell, game);
    }

    /**
     * True when the card can affect the candidate. A counter skips a spell that "can't be countered": the check
     * asks the rule-modifying effects in silent mode, as XMage asks them when it counters, and changes nothing. A
     * destroy skips an indestructible permanent. Ward is no reason to skip: the opponent pays it.
     */
    static boolean canAffect(Game game, SpellAbility spell, UUID candidate) {
        if (has(spell, CounterTargetEffect.class)) {
            Spell victim = game.getStack().getSpell(candidate);
            return victim != null && !cantBeCountered(game, spell, victim);
        }
        if (has(spell, DestroyTargetEffect.class)) {
            Permanent victim = game.getPermanent(candidate);
            return victim != null && !victim.hasAbility(IndestructibleAbility.getInstance(), game);
        }
        return true;
    }

    /** True when a rule-modifying effect stops the counter of {@code victim} by {@code source} ("can't be countered"). */
    static boolean cantBeCountered(Game game, Ability source, Spell victim) {
        GameEvent counter = GameEvent.getEvent(GameEvent.EventType.COUNTER, victim.getId(), source, victim.getControllerId());
        return game.getContinuousEffects().preventedByRuleModification(counter, null, game, true);
    }

    private static boolean has(SpellAbility spell, Class<? extends Effect> kind) {
        for (Effect effect : spell.getAllEffects()) {
            if (kind.isInstance(effect)) {
                return true;
            }
        }
        return false;
    }

    /**
     * The spell ability the opponent casts the card with on our turn: the card's own when it is an instant or has
     * flash, else the first other spell ability with instant timing (Rout's "pay {2} more to cast it as though it had
     * flash"). Null when the card has no way to be cast at instant speed. PlayerImpl.cast checks no timing, so this
     * method makes the choice.
     */
    static SpellAbility instantSpeed(Card card, Game game) {
        SpellAbility main = card.getSpellAbility();
        if (main != null && (card.isInstant(game) || card.hasAbility(FlashAbility.getInstance(), game))) {
            return main;
        }
        for (Ability ability : card.getAbilities(game)) {
            if (ability instanceof SpellAbility spell && spell != main && spell.getTiming() == TimingRule.INSTANT) {
                return spell;
            }
        }
        return null;
    }

    /**
     * True when the card is a spell on the stack whose first target is {@code target}; with no target, true when the
     * card is a spell on the stack.
     */
    private static boolean aimsAt(Game game, Card card, UUID target) {
        Spell spell = game.getStack().getSpell(card.getId());
        return spell != null && (target == null || target.equals(spell.getSpellAbility().getTargets().getFirstTarget()));
    }

    /**
     * Takes a settled disruption card out of the game: out of the hand, graveyard, library or exile it is in, with
     * no event, so no disruption card stays in any zone. False when it is in no zone already, or on the stack or the
     * battlefield (an instant or a sorcery that left the stack is never there).
     */
    static boolean remove(Game game, Player owner, UUID cardId) {
        Card card = game.getCard(cardId);
        Zone zone = card == null ? null : game.getState().getZone(cardId);
        if (zone == null) {
            return false;
        }
        switch (zone) {
            case HAND -> owner.getHand().remove(cardId);
            case GRAVEYARD -> owner.getGraveyard().remove(cardId);
            case LIBRARY -> owner.getLibrary().remove(cardId, game);
            case EXILED -> game.getExile().removeCard(card);
            default -> {
                return false;
            }
        }
        card.setZone(Zone.OUTSIDE, game);
        return true;
    }
}
