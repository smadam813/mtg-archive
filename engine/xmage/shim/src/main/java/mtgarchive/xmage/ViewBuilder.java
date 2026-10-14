package mtgarchive.xmage;

import mage.MageObject;
import mage.Mana;
import mage.abilities.Ability;
import mage.abilities.Abilities;
import mage.abilities.Mode;
import mage.abilities.effects.Effect;
import mage.abilities.effects.SearchEffect;
import mage.abilities.mana.ActivatedManaAbilityImpl;
import mage.cards.Card;
import mage.constants.CommanderCardType;
import mage.constants.Zone;
import mage.filter.FilterCard;
import mage.game.Game;
import mage.game.permanent.Permanent;
import mage.players.Player;

import java.util.ArrayList;
import java.util.Collection;
import java.util.Comparator;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.TreeSet;
import java.util.UUID;

/** The pilot view: what our seat knows, in decklist names, and no XMage types. */
final class ViewBuilder {

    private ViewBuilder() {
    }

    /** An event's view: no finds, because the service asks the pilot nothing on an event. */
    static Messages.View forEvent(Game game, Player me, GameCtx ctx) {
        return build(game, me, ctx, List.of(), Map.of());
    }

    /**
     * A decision's view: finds for each search in hand, on the battlefield, in the command zone and on
     * the cards the decision shows (roadmap carried-forward item 7).
     */
    static Messages.View forDecision(Game game, Player me, GameCtx ctx, List<Messages.WireAction> actions,
                                     Collection<? extends MageObject> shown) {
        return build(game, me, ctx, actions, finds(game, me, ctx.names, shown));
    }

    private static Messages.View build(Game game, Player me, GameCtx ctx, List<Messages.WireAction> actions,
                                       Map<String, List<String>> finds) {
        Names names = ctx.names;
        UUID my = me.getId();
        List<Messages.ViewPermanent> battlefield = new ArrayList<>();
        List<Messages.ManaSource> manaSources = new ArrayList<>();
        for (Permanent p : game.getBattlefield().getAllActivePermanents(my)) {
            String id = "p:" + p.getId();
            battlefield.add(new Messages.ViewPermanent(id, names.card(p), Names.face(p), p.isTapped()));
            if (!p.isTapped()) {
                List<String> options = manaOptions(p.getAbilities().getActivatedManaAbilities(Zone.BATTLEFIELD), my, game);
                if (!options.isEmpty()) {
                    manaSources.add(new Messages.ManaSource(id, options));
                }
            }
        }
        battlefield.sort(Comparator.comparing(Messages.ViewPermanent::card).thenComparing(Messages.ViewPermanent::id));
        manaSources.sort(Comparator.comparing(Messages.ManaSource::id));

        List<Messages.HandMana> handMana = new ArrayList<>();
        for (Card card : me.getHand().getCards(game)) {
            List<String> options = manaOptions(card.getAbilities().getActivatedManaAbilities(Zone.HAND), my, game);
            if (!options.isEmpty()) {
                handMana.add(new Messages.HandMana(names.card(card), options));
            }
        }

        List<String> exile = names.cards(game.getExile().getCardsOwned(game, my));

        List<String> commandZone = new ArrayList<>();
        for (UUID id : game.getCommandersIds(me, CommanderCardType.ANY, false)) {
            if (game.getState().getZone(id) == Zone.COMMAND) {
                commandZone.add(names.card(game, id));
            }
        }
        commandZone.sort(null);

        return new Messages.View(ctx.ourTurns, ctx.step(game), me.getLife(), !me.canPlayLand(),
                me.getManaPool().getMana().toString(),
                names.cards(me.getHand().getCards(game)), names.cards(me.getGraveyard().getCards(game)),
                exile, commandZone, battlefield, me.getLibrary().size(), manaSources, handMana, List.copyOf(ctx.resolved),
                finds, List.copyOf(actions));
    }

    /** The mana strings the abilities can add now ("{C}{C}"), without repeats, in ability order. */
    static List<String> manaOptions(Abilities<ActivatedManaAbilityImpl> abilities, UUID me, Game game) {
        Set<String> out = new LinkedHashSet<>();
        for (ActivatedManaAbilityImpl a : abilities) {
            if (!a.canActivate(me, game).canActivate()) {
                continue;
            }
            for (Mana mana : a.getNetMana(game)) {
                String text = mana.toString();
                if (!text.isEmpty()) {
                    out.add(text);
                }
            }
        }
        return new ArrayList<>(out);
    }

    /**
     * For each source with a library search: the library cards that XMage's own search filter matches
     * now, keyed by the source's name, names sorted. The sources are our hand, our permanents, our
     * commanders in the command zone, and the cards a decision shows. Taken from the spike's
     * PilotPlayer.finds.
     */
    static Map<String, List<String>> finds(Game game, Player me, Names names, Collection<? extends MageObject> shown) {
        Map<String, Set<String>> found = new TreeMap<>();
        List<MageObject> sources = new ArrayList<>(me.getHand().getCards(game));
        sources.addAll(game.getBattlefield().getAllActivePermanents(me.getId()));
        for (UUID id : game.getCommandersIds(me, CommanderCardType.ANY, false)) {
            Card commander = game.getCard(id);
            if (commander != null && game.getState().getZone(id) == Zone.COMMAND) {
                sources.add(commander);
            }
        }
        sources.addAll(shown);
        List<Card> library = me.getLibrary().getCards(game);
        for (MageObject src : sources) {
            for (Ability a : src.getAbilities()) {
                for (Mode m : a.getModes().values()) {
                    for (Effect e : m.getEffects()) {
                        if (!(e instanceof SearchEffect)) {
                            continue;
                        }
                        FilterCard f = ((SearchEffect) e).getTarget().getFilter();
                        Set<String> matched = found.computeIfAbsent(names.card(src), k -> new TreeSet<>());
                        for (Card c : library) {
                            try {
                                if (f.match(c, me.getId(), a, game)) {
                                    matched.add(names.card(c));
                                }
                            } catch (RuntimeException ignored) {
                                // a filter that needs a resolving source: it finds nothing now
                            }
                        }
                    }
                }
            }
        }
        Map<String, List<String>> out = new TreeMap<>();
        found.forEach((k, v) -> out.put(k, new ArrayList<>(v)));
        return out;
    }
}
