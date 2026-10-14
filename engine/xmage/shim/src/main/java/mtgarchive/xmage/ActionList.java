package mtgarchive.xmage;

import mage.MageObject;
import mage.abilities.ActivatedAbility;
import mage.abilities.PlayLandAbility;
import mage.abilities.SpellAbility;
import mage.abilities.costs.Cost;
import mage.abilities.costs.common.SacrificeSourceCost;
import mage.abilities.costs.common.TapSourceCost;
import mage.abilities.mana.ActivatedManaAbilityImpl;
import mage.constants.Zone;
import mage.game.Game;
import mage.players.Player;
import mage.util.CardUtil;
import mage.watchers.common.CommanderPlaysCountWatcher;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * The legal actions of one action decision: XMage's playable list less mana abilities (XMage pays),
 * actions that failed this turn, and a third activation of one ability in a turn. Sorted by kind,
 * card, face and id, so the same state gives the same list.
 */
final class ActionList {

    /** The zones the wire schema allows on an action. */
    private static final Set<String> WIRE_ZONES =
            Set.of("hand", "command_zone", "battlefield", "graveyard", "exile", "library");

    /** One listed action: the XMage ability behind it and its wire form. */
    record Entry(String key, ActivatedAbility ability, Messages.WireAction action) {
    }

    static final Comparator<Messages.WireAction> ORDER = Comparator.comparing(Messages.WireAction::kind)
            .thenComparing(Messages.WireAction::card)
            .thenComparing(Messages.WireAction::face)
            .thenComparing(Messages.WireAction::id);

    private ActionList() {
    }

    static List<Entry> legal(Player me, Game game, GameCtx ctx) {
        List<Entry> out = new ArrayList<>();
        Set<String> keys = new HashSet<>();
        for (ActivatedAbility a : me.getPlayable(game, true)) {
            if (a instanceof ActivatedManaAbilityImpl) {
                continue;
            }
            Zone zone = game.getState().getZone(a.getSourceId());
            // The schema requires permanent_id on activate: an activation off the battlefield (cycling, unearth) is not listed.
            if (isActivation(a) && zone != Zone.BATTLEFIELD) {
                continue;
            }
            // The schema allows six zones on an action: stack and outside are not among them.
            if (!WIRE_ZONES.contains(Steps.zone(zone))) {
                continue;
            }
            // A blank card stands in for a card XMage lacks: it is never cast.
            MageObject source = game.getObject(a.getSourceId());
            if (source != null && ctx.deck.isBlank(ctx.names.card(source))) {
                continue;
            }
            String key = key(a);
            if (ctx.failedThisTurn.contains(key) || !keys.add(key)) {
                continue;
            }
            if (isActivation(a) && ctx.activations.getOrDefault(key, 0) >= PilotPlayer.ACTIVATION_CAP) {
                if (ctx.capped.add(key)) {
                    ctx.line(game, "CAP " + ctx.names.card(source)); // roadmap carried-forward item 3
                }
                continue;
            }
            out.add(new Entry(key, a, describe(a, zone, game, ctx)));
        }
        out.sort(Comparator.comparing(Entry::action, ORDER));
        return out;
    }

    /** The seeded source object id and the ability's original id: stable across game copies. */
    static String key(ActivatedAbility a) {
        return a.getSourceId() + "/" + a.getOriginalId();
    }

    static boolean isActivation(ActivatedAbility a) {
        return !(a instanceof SpellAbility) && !(a instanceof PlayLandAbility);
    }

    private static Messages.WireAction describe(ActivatedAbility a, Zone zone, Game game, GameCtx ctx) {
        MageObject source = game.getObject(a.getSourceId());
        String kind = a instanceof PlayLandAbility ? "land"
                : a instanceof SpellAbility ? (zone == Zone.COMMAND ? "cast_commander" : "cast")
                : "activate";
        String cost;
        switch (kind) {
            case "land":
                cost = "";
                break;
            case "cast_commander":
                cost = CostText.withTax(a.getManaCosts().getText(), 2 * commanderCasts(game, a));
                break;
            default:
                cost = a.getManaCosts().getText();
        }
        boolean activate = kind.equals("activate");
        boolean spendsSource = false;
        if (activate) {
            for (Cost c : a.getCosts()) {
                spendsSource |= c instanceof TapSourceCost || c instanceof SacrificeSourceCost;
            }
        }
        return new Messages.WireAction("a:" + key(a), kind, ctx.names.card(source), Names.face(source),
                Steps.zone(zone), cost, activate ? "p:" + a.getSourceId() : null, spendsSource);
    }

    /** Casts from the command zone so far: the commander tax is 2 per cast (CR 903.8). */
    private static int commanderCasts(Game game, ActivatedAbility a) {
        CommanderPlaysCountWatcher watcher = game.getState().getWatcher(CommanderPlaysCountWatcher.class);
        return watcher == null ? 0 : watcher.getPlaysCount(CardUtil.getMainCardId(game, a.getSourceId()));
    }
}
