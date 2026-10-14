package mtgarchive.xmage;

import mage.MageObject;
import mage.abilities.ActivatedAbility;
import mage.abilities.SpellAbility;
import mage.cards.Card;
import mage.constants.PhaseStep;
import mage.constants.Zone;
import mage.game.Game;
import mage.game.PutToBattlefieldInfo;
import mage.game.permanent.Permanent;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Deque;
import java.util.List;

/**
 * The steps of one scenario and the answers they give. It is the scenario's Decider: it keeps every
 * hand, and it answers each pick from the next choose step. Its priority hook runs the cast and
 * activate steps in our first main phase, each on an empty stack. When the steps are done and the
 * stack is empty, it takes the result and ends the game.
 */
final class ScenarioScript implements Decider {

    private final Deque<Messages.ScenarioAction> steps;
    private final Messages.ScenarioStart start;

    final List<Messages.PickRecord> picks = new ArrayList<>();

    GameCtx ctx;

    Messages.ScenarioResult result;

    /** The 1-based number of the last step taken, for error messages. */
    private int step;

    /** True once our zones are set up (see {@link #setUp}). */
    private boolean setUp;

    /**
     * True while {@link #setUp} runs game.cheat: a start permanent's own "as it enters, name a card"
     * ability (Pithing Needle) asks for its name right there, as a real pick, in battlefield order.
     */
    private boolean settingUp;

    /** The start names still owed to a name pick that {@link #settingUp} answers, in battlefield order. */
    private final Deque<String> startNames = new ArrayDeque<>();

    ScenarioScript(List<Messages.ScenarioAction> actions, Messages.ScenarioStart start) {
        this.steps = new ArrayDeque<>(actions == null ? List.of() : actions);
        this.start = start;
    }

    @Override
    public Messages.Reply ask(Messages.InGame message) {
        if (message instanceof Messages.DecideKeep) {
            return Messages.Reply.ofKeep(message.seq(), true);
        }
        if (message instanceof Messages.DecidePick d) {
            return pick(d);
        }
        throw new DecisionFailure("a scenario answers keep and pick decisions only, got " + message.getClass().getSimpleName());
    }

    /** Our priority in a scenario. True when a step acted, so XMage asks again. */
    boolean priority(Game game, PilotPlayer me) {
        if (ctx.done()) {
            return false;
        }
        if (!setUp) {
            setUp = true;
            setUp(game, me);
            return false;
        }
        if (!game.getStack().isEmpty() || !game.isActivePlayer(me.getId())
                || game.getTurnStepType() != PhaseStep.PRECOMBAT_MAIN) {
            return false;
        }
        Messages.ScenarioAction next = steps.peek();
        if (next == null) {
            result = ScenarioRunner.result(game, me, ctx, this);
            ctx.stop(game);
            return false;
        }
        if ("choose".equals(next.type())) {
            ctx.fail(game, "step " + (step + 1) + " chooses " + next.pick() + ", but no pick is open");
            return false;
        }
        steps.poll();
        step++;
        String what = next.type() + " " + ("cast".equals(next.type()) ? next.card() : next.permanent() + " ability " + next.ability());
        ActivatedAbility ability = "cast".equals(next.type())
                ? castable(game, me, next.card())
                : "activate".equals(next.type()) ? activatable(game, me, next.permanent(), next.ability()) : null;
        if (ability == null) {
            ctx.fail(game, "step " + step + " cannot " + what);
            return false;
        }
        ctx.line(game, "SCENARIO " + what);
        if (!me.activateAbility(ability.copy(), game)) {
            ctx.fail(game, "step " + step + ": XMage refused to " + what);
            return false;
        }
        return true;
    }

    /**
     * Puts our zones in place, at our very first priority (turn 1's upkeep): a permanent cheated onto
     * the battlefield before {@code game.start()} would see turn 1's own untap step and be untapped
     * by it, so the cheat happens here instead, after that step has already passed. Because a start
     * permanent enters in turn 1's upkeep, a start creature has summoning sickness, and "at the
     * beginning of your upkeep" triggers do not fire for it on turn 1.
     */
    private void setUp(Game game, PilotPlayer me) {
        try {
            List<Card> library = ScenarioRunner.cards(start.library());
            // cheat puts each library card on top in list order: give it the bottom card first.
            Collections.reverse(library);
            List<Messages.StartPermanent> permanents = start.battlefield() == null ? List.of() : start.battlefield();
            List<PutToBattlefieldInfo> battlefield = new ArrayList<>();
            List<Card> named = new ArrayList<>();
            for (Messages.StartPermanent p : permanents) {
                Card card = ScenarioRunner.card(p.card());
                battlefield.add(new PutToBattlefieldInfo(card, p.tapped()));
                named.add(card);
                if (p.named() != null) {
                    startNames.add(p.named());
                }
            }
            settingUp = true;
            try {
                game.cheat(me.getId(), library, ScenarioRunner.cards(start.hand()), battlefield,
                        ScenarioRunner.cards(start.graveyard()), List.of(), List.of());
            } finally {
                settingUp = false;
            }
            ScenarioRunner.checkStartNames(game, ctx.deck, permanents, named);
            ctx.logOpening(game, me);
        } catch (RuntimeException e) {
            ctx.fail(game, GameCtx.firstLine(e, null));
        }
    }

    /** The playable cast of a card in hand, by decklist name; null when it cannot be cast now. */
    private ActivatedAbility castable(Game game, PilotPlayer me, String card) {
        for (ActivatedAbility a : me.getPlayable(game, true)) {
            MageObject source = game.getObject(a.getSourceId());
            if (a instanceof SpellAbility && game.getState().getZone(a.getSourceId()) == Zone.HAND
                    && card.equals(ctx.names.card(source))) {
                return a;
            }
        }
        return null;
    }

    /** Activated ability {@code index} of our first permanent named {@code card}, mana abilities included, in XMage's order. */
    private ActivatedAbility activatable(Game game, PilotPlayer me, String card, Integer index) {
        if (index == null) {
            return null;
        }
        for (Permanent p : game.getBattlefield().getAllActivePermanents(me.getId())) {
            if (card.equals(ctx.names.card(p))) {
                List<ActivatedAbility> abilities = new ArrayList<>(p.getAbilities().getActivatedAbilities(Zone.BATTLEFIELD));
                return index >= 0 && index < abilities.size() ? abilities.get(index) : null;
            }
        }
        return null;
    }

    /** The next step answers the pick; it must be a choose step that names an offered option. */
    private Messages.Reply pick(Messages.DecidePick d) {
        if (settingUp) {
            return nameStartPermanent(d);
        }
        String purpose = d.pick().purpose();
        Messages.ScenarioAction next = steps.peek();
        if (next == null || !"choose".equals(next.type())) {
            throw new DecisionFailure("a " + purpose + " pick came, but step " + (step + 1) + " is "
                    + (next == null ? "the end of the script" : "a " + next.type()));
        }
        steps.poll();
        step++;
        List<String> ids = new ArrayList<>();
        d.pick().options().forEach(o -> ids.add(o.id()));
        for (Messages.PickOption option : d.pick().options()) {
            if (matches(option, next.pick())) {
                picks.add(new Messages.PickRecord(purpose, List.copyOf(ids), d.pick().seen(), option.id()));
                return Messages.Reply.ofOption(d.seq(), option.id());
            }
        }
        List<String> labels = new ArrayList<>();
        d.pick().options().forEach(o -> labels.add(o.label()));
        throw new DecisionFailure("step " + step + " chooses " + next.pick() + ", which the " + purpose + " pick does not offer: " + labels);
    }

    /**
     * A start permanent's own "as it enters, name a card" ability (Pithing Needle) asks for its name
     * as a real pick while game.cheat runs, in the battlefield's order: answer it from
     * {@link #startNames} rather than the script, which never sees this pick.
     */
    private Messages.Reply nameStartPermanent(Messages.DecidePick d) {
        String named = startNames.poll();
        if (named != null) {
            for (Messages.PickOption option : d.pick().options()) {
                if (named.equals(option.card())) {
                    return Messages.Reply.ofOption(d.seq(), option.id());
                }
            }
        }
        throw new DecisionFailure(named == null
                ? "a start permanent asks for a card name as it enters, but the start gives it none"
                : "a start permanent names " + named + ", which the " + d.pick().purpose() + " pick does not offer");
    }

    /**
     * A choose step names a card, an option id ("shuffle", "do not shuffle"), or the pilot's stop ids
     * "fail to find" and "keep the rest", which the wire calls none.
     */
    static boolean matches(Messages.PickOption option, String pick) {
        if (pick.equals(option.card()) || pick.equals(option.id())) {
            return true;
        }
        return PilotPlayer.NONE.equals(option.id()) && (pick.equals("fail to find") || pick.equals("keep the rest"));
    }
}
