package mtgarchive.xmage;

import mage.ConditionalMana;
import mage.MageObject;
import mage.Mana;
import mage.abilities.Abilities;
import mage.abilities.ActivatedAbility;
import mage.abilities.Ability;
import mage.abilities.SpellAbility;
import mage.abilities.Mode;
import mage.abilities.costs.mana.ManaCost;
import mage.abilities.costs.mana.ManaCosts;
import mage.abilities.TriggeredAbility;
import mage.abilities.effects.Effect;
import mage.abilities.effects.common.BrainstormEffect;
import mage.abilities.mana.ActivatedManaAbilityImpl;
import mage.cards.Card;
import mage.cards.Cards;
import mage.cards.repository.CardInfo;
import mage.choices.Choice;
import mage.choices.ChoiceHintType;
import mage.constants.AsThoughEffectType;
import mage.constants.Outcome;
import mage.constants.PhaseStep;
import mage.constants.RangeOfInfluence;
import mage.constants.Zone;
import mage.game.Game;
import mage.game.permanent.Permanent;
import mage.game.stack.Spell;
import mage.game.stack.StackObject;
import mage.player.ai.ComputerPlayer;
import mage.players.Library;
import mage.target.Target;
import mage.target.TargetCard;
import mage.target.common.TargetCardInHand;
import mage.target.common.TargetCardInLibrary;
import mage.target.common.TargetDiscard;

import java.io.Serializable;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Comparator;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TreeSet;
import java.util.UUID;

/**
 * Our seat. It forwards the pilot's decisions to the service: keep and bottom, the action in our
 * upkeep, precombat main and end step, library picks, card names, trigger order and discards. It
 * sends end_of_turn, resolved and cast events, and carries out the counter the service asks for,
 * and the end-of-turn disruption. In an interactive game it has the scripted opponent cast its disruption card,
 * forwards the response window and settles the card. It answers every other question in the JVM: XMage pays mana and
 * picks targets, modes and X (ComputerPlayer defaults). Rewritten from the spike's PilotPlayer
 * (branch research/xmage-game-copies).
 */
public class PilotPlayer extends ComputerPlayer {

    /** The same ability at most twice a turn (spike rule): it bounds activation loops. */
    static final int ACTIVATION_CAP = 2;

    /** The id of the pick option that stops a search, a look or a scry. */
    static final String NONE = "none";

    /** The answers each event takes; stop ends the game at once. */
    static final Set<String> PLAIN_REPLIES = Set.of("continue", "stop");

    static final Set<String> CAST_REPLIES = Set.of("continue", "stop", "counter");

    /**
     * With the scripted opponent the cast event and the end-of-turn event before disruption take oppose, never counter
     * or disrupt; inside an end-step sequence the opposed event takes oppose too (ADR 0013).
     */
    static final Set<String> OPPOSE_REPLIES = Set.of("continue", "stop", "oppose");

    static final Set<String> END_OF_TURN_REPLIES = Set.of("continue", "stop", "disrupt");

    /** XMage's question for an optional shuffle (ShuffleLibrarySourceEffect: Ponder). */
    static final String SHUFFLE_QUESTION = "Shuffle your library?";

    /** The ids of the shuffle pick's options, as the pilot's PickOption names them. */
    static final String SHUFFLE = "shuffle";

    static final String NO_SHUFFLE = "do not shuffle";

    /** XMage's names of the mana colors, by symbol. */
    static final Map<String, String> COLOR_NAMES = Map.of("W", "White", "U", "Blue", "B", "Black", "R", "Red", "G", "Green");

    private final transient GameCtx ctx;

    /** The color name of the part the matching pays while it activates a mana ability; null otherwise. */
    private transient String matchedColor;

    public PilotPlayer(String name, GameCtx ctx) {
        super(name, RangeOfInfluence.ALL);
        this.ctx = ctx;
    }

    public PilotPlayer(final PilotPlayer player) {
        super(player);
        this.ctx = player.ctx;
    }

    @Override
    public PilotPlayer copy() {
        return new PilotPlayer(this);
    }

    // ------------------------------------------------------------ mana payment

    /**
     * The maximum matching pays first (it counts a Simian Spirit Guide in hand), then XMage. When
     * neither can, a mana ability that works from hand pays (Simian Spirit Guide), in hand order. ComputerPlayer counts such a card as a mana producer, but it reads
     * each producer's mana abilities in the battlefield zone only (ComputerPlayer.java:683 at
     * xmage_1.4.62V1), so it never pays with one: getPlayable offers the cast, and the payment
     * then fails. A land or a permanent still pays before a card in hand, as in the retired engine.
     */
    @Override
    protected boolean playManaHandling(Ability ability, ManaCost unpaid, Game game) {
        boolean guarded = ability instanceof ActivatedManaAbilityImpl && ctx.payingManaAbilities.add(ability.getId());
        try {
            return payByMatching(ability, unpaid, game) || super.playManaHandling(ability, unpaid, game) || payFromHand(unpaid, game);
        } finally {
            if (guarded) {
                ctx.payingManaAbilities.remove(ability.getId());
            }
        }
    }

    /**
     * A mana ability never pays its own cost. Three Tree Mascot has "{1}: Add one mana of
     * any color. Activate only once each turn." XMage counts the Mascot as a source for a {1} spell
     * when it is our only untapped source. ComputerPlayer then pays its {1} with the same ability at
     * every level. Each level saves a game state, so the recursion ends only when the heap runs
     * out. When this method refuses the activation, the payment fails, and the action fails like
     * any unpayable action.
     */
    @Override
    public boolean activateAbility(ActivatedAbility ability, Game game) {
        if (ability != null && ctx.payingManaAbilities.contains(ability.getId())) {
            return false;
        }
        return super.activateAbility(ability, game);
    }

    /**
     * Roadmap item 22: tap the source that a maximum matching assigns to the last unpaid part,
     * the part XMage's playManaHandling pays on this call. A card in hand with a mana ability that
     * works from hand is a source too, after every permanent (phase 3E, Obeka run 141). False
     * leaves the part to XMage: a simulation, a part with no plain symbol, or no plan that pays
     * every pip.
     */
    private boolean payByMatching(Ability ability, ManaCost unpaid, Game game) {
        if (game.isSimulation() || !(unpaid instanceof ManaCosts<?> costs) || costs.isEmpty()) {
            return false;
        }
        // Mana spent as though it were another type (ComputerPlayer checks each such effect): XMage pays.
        if (!game.getContinuousEffects().asThough(ability.getSourceId(), AsThoughEffectType.SPEND_OTHER_MANA,
                ability, ability.getControllerId(), game).isEmpty()) {
            return false;
        }
        ManaCost cost = costs.get(costs.size() - 1);
        String part = ManaMatch.symbol(cost);
        if (part == null) {
            return false;
        }
        List<String> others = new ArrayList<>();
        for (int i = 0; i < costs.size() - 1; i++) {
            String s = ManaMatch.symbol(costs.get(i));
            if (s == null) {
                return false;
            }
            if (!s.equals("1")) {
                others.add(s);
            }
        }
        Map<String, List<ActivatedManaAbilityImpl>> abilities = new LinkedHashMap<>();
        List<ManaMatch.Source> sources = new ArrayList<>();
        for (Permanent p : game.getBattlefield().getAllActivePermanents(getId())) {
            if (!p.isTapped()) {
                addSource(p.getAbilities(game), p.getId(), Zone.BATTLEFIELD, false, ability, cost, game, abilities, sources);
            }
        }
        // A mana ability that works from hand (Simian Spirit Guide): the matching spends it only when it must.
        for (Card card : getHand().getCards(game)) {
            addSource(card.getAbilities(game), card.getId(), Zone.HAND, true, ability, cost, game, abilities, sources);
        }
        String id = ManaMatch.pick(part, others, sources);
        if (id == null) {
            return false;
        }
        for (ActivatedManaAbilityImpl a : abilities.get(id)) {
            for (Mana m : a.getNetMana(game)) {
                if (!cost.testPay(m)) {
                    continue;
                }
                // The same check as ComputerPlayer.playManaHandling: conditional mana must apply to this spell.
                if (m instanceof ConditionalMana conditional && !conditional.apply(ability, game, getId(), cost)) {
                    continue;
                }
                matchedColor = COLOR_NAMES.get(part);
                try {
                    if (activateAbility(a, game)) {
                        return true;
                    }
                } finally {
                    matchedColor = null;
                }
            }
        }
        return false;
    }

    /**
     * Adds the abilities {@code all} as a source when it has a mana ability that costs no mana (a filter
     * land's hybrid ability: XMage chains it) and makes net mana. Conditional mana counts only when
     * it applies to this spell: the matching must not plan on mana the payment then refuses.
     */
    private void addSource(Abilities<Ability> all, UUID id, Zone zone, boolean hand, Ability ability, ManaCost cost, Game game,
            Map<String, List<ActivatedManaAbilityImpl>> abilities, List<ManaMatch.Source> sources) {
        Set<String> colors = new TreeSet<>();
        List<ActivatedManaAbilityImpl> free = new ArrayList<>();
        for (ActivatedManaAbilityImpl a : all.getAvailableActivatedManaAbilities(zone, getId(), game)) {
            if (!a.getManaCosts().isEmpty()) {
                continue;
            }
            free.add(a);
            for (Mana m : a.getNetMana(game)) {
                if (!(m instanceof ConditionalMana conditional) || conditional.apply(ability, game, getId(), cost)) {
                    colors.addAll(ManaMatch.colors(m));
                }
            }
        }
        // A source with no net mana now would sort first for a generic part and pay nothing.
        if (!free.isEmpty() && !colors.isEmpty()) {
            abilities.put(id.toString(), free);
            sources.add(new ManaMatch.Source(id.toString(), colors, hand));
        }
    }

    /**
     * The color choice of a mana ability that the matching tapped (Command Tower): the color of
     * the matched part. ComputerPlayer.choose takes the first color of W, R, G, U, B that the whole
     * unpaid cost holds, so it can make white for a {U} part and leave {U} unpaid.
     */
    private boolean chooseMatchedColor(Outcome outcome, Choice choice) {
        if (outcome != Outcome.PutManaInPool || matchedColor == null || !choice.isManaColorChoice()
                || !choice.getChoices().contains(matchedColor)) {
            return false;
        }
        choice.setChoice(matchedColor);
        return true;
    }

    private boolean payFromHand(ManaCost unpaid, Game game) {
        // The part to pay, as ComputerPlayer takes it: the last cost of a list.
        ManaCost cost = unpaid instanceof ManaCosts<?> costs && !costs.isEmpty() ? costs.get(costs.size() - 1) : unpaid;
        for (Card card : getHand().getCards(game)) {
            for (ActivatedManaAbilityImpl mana : card.getAbilities(game).getAvailableActivatedManaAbilities(Zone.HAND, getId(), game)) {
                for (Mana net : mana.getNetMana(game)) {
                    // A choice of color ({Any}) is not ours to make here; ComputerPlayer refuses it too.
                    if (net.getAny() == 0 && cost.testPay(net) && activateAbility(mana, game)) {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    // ------------------------------------------------------------ mulligan

    /**
     * XMage asks keep before each mulligan. After a mulligan that is not free, XMage asks for the
     * bottom card first ({@link #bottom}); the shim then asks keep on the seven there and replays
     * the answer here, so the pilot always decides keep on the hand as dealt, as in the retired engine.
     */
    @Override
    public boolean chooseMulligan(Game game) {
        if (game.isSimulation() || ctx.done()) {
            return false;
        }
        Boolean keep = ctx.pendingKeep;
        ctx.pendingKeep = null;
        if (keep == null) {
            keep = askKeep(game);
        }
        if (keep) {
            return false;
        }
        ctx.mulligansTaken++;
        return true;
    }

    @Override
    public boolean chooseTarget(Outcome outcome, Target target, Ability source, Game game) {
        if (!game.isSimulation() && ctx.ourTurns == 0 && source == null
                && outcome == Outcome.Discard && target instanceof TargetCardInHand) {
            return bottom(target, game);
        }
        if (!game.isSimulation() && !ctx.done() && source != null && target instanceof TargetCardInHand && isPutBack(source)) {
            return putBack(target, source, game);
        }
        if (!game.isSimulation() && ctx.hint != null && source != null && chooseHintedTarget(target, source, game)) {
            return true;
        }
        return super.chooseTarget(outcome, target, source, game);
    }

    /** The hand size a keep leaves: 7, less one per mulligan after the free ones. */
    private int keepSize() {
        return GameRunner.HAND_SIZE - Math.max(0, ctx.mulligansTaken - ctx.start.freeMulligans());
    }

    /** Asks keep on the hand as dealt. A failed game keeps. */
    private boolean askKeep(Game game) {
        Messages.Keep keep = new Messages.Keep(ctx.names.cards(getHand().getCards(game)), ctx.mulligansTaken, keepSize());
        Messages.Reply reply = ctx.ask(game,
                new Messages.DecideKeep("decide", ctx.nextSeq(), "keep", decisionView(game, List.of(), List.of()), keep));
        if (reply == null) {
            return true;
        }
        if (reply.keep() == null) {
            ctx.badReply(game, "a keep reply needs keep: true or false");
            return true;
        }
        ctx.line(game, "PILOT " + (reply.keep() ? "keep " : "mulligan ") + keep.hand());
        return reply.keep();
    }

    /** London bottom: XMage asks one card per call; the shim asks the service once for all of them. */
    private boolean bottom(Target target, Game game) {
        if (ctx.pendingBottom.isEmpty() && !ctx.done()) {
            List<UUID> hand = new ArrayList<>(getHand());
            int count = hand.size() - keepSize();
            boolean keep = askKeep(game);
            ctx.pendingKeep = keep;
            List<UUID> chosen = keep ? askCards(game, "bottom", count, hand) : null;
            // A hand the pilot rejects goes back into the library at the next mulligan; bottom its first cards.
            ctx.pendingBottom.addAll(chosen != null ? chosen : hand.subList(0, Math.max(0, count)));
        }
        UUID id = ctx.pendingBottom.poll();
        if (id == null) {
            return super.chooseTarget(Outcome.Discard, target, null, game);
        }
        add(target, id, null, game);
        ctx.line(game, "PILOT bottom " + ctx.names.card(game, id));
        return true;
    }

    // ------------------------------------------------------------ discard

    @Override
    public boolean choose(Outcome outcome, Target target, Ability source, Game game, Map<String, Serializable> options) {
        if (!game.isSimulation() && !ctx.done() && target instanceof TargetDiscard) {
            return discard(target, source, game);
        }
        return super.choose(outcome, target, source, game, options);
    }

    private boolean discard(Target target, Ability source, Game game) {
        Set<UUID> possible = target.possibleTargets(getId(), source, game);
        List<UUID> hand = new ArrayList<>();
        for (UUID id : getHand()) {
            if (possible.contains(id) && !target.getTargets().contains(id)) {
                hand.add(id);
            }
        }
        int count = discardCount(target.getMinNumberOfTargets(), target.getMaxNumberOfTargets(), hand.size());
        List<UUID> picked = count > 0 ? askCards(game, "discard", count, hand) : null;
        if (picked == null) {
            return super.choose(Outcome.Discard, target, source, game, null);
        }
        for (UUID id : picked) {
            add(target, id, source, game);
            ctx.line(game, "PILOT discard " + ctx.names.card(game, id));
        }
        return true;
    }

    /**
     * The cards a discard decision asks for: the target's minimum, as many as the hand holds. The
     * pilot has one discard policy, exactly N cards (the retired engine's cleanup discard), and the wire
     * count is exact. An optional discard (minimum 0) is not forwarded: 0 leaves it to XMage.
     */
    static int discardCount(int min, int max, int candidates) {
        return Math.min(min, Math.min(max, candidates));
    }

    /** A bottom or discard decision; the reply's names map to hand cards in hand order. Null on failure. */
    private List<UUID> askCards(Game game, String kind, int count, List<UUID> candidates) {
        Messages.Reply reply = ctx.ask(game,
                new Messages.DecideCount("decide", ctx.nextSeq(), kind, decisionView(game, List.of(), List.of()), count));
        if (reply == null) {
            return null;
        }
        if (reply.cards() == null || reply.cards().size() != count) {
            ctx.badReply(game, "a " + kind + " reply must name exactly " + count + " cards");
            return null;
        }
        List<UUID> picked = new ArrayList<>();
        for (String name : reply.cards()) {
            UUID found = null;
            for (UUID c : candidates) {
                if (!picked.contains(c) && name.equals(ctx.names.card(game, c))) {
                    found = c;
                    break;
                }
            }
            if (found == null) {
                ctx.badReply(game, "the " + kind + " reply names " + name + ", which is not in hand");
                return null;
            }
            picked.add(found);
        }
        return picked;
    }

    // ------------------------------------------------------------ priority: actions and events

    /** Our seat declares no attackers: a run against the idle opponent has no combat. */
    @Override
    public void selectAttackers(Game game, UUID attackingPlayerId) {
    }

    @Override
    public boolean priority(Game game) {
        if (game.isSimulation()) {
            pass(game);
            return false;
        }
        ctx.onPriority(game, this);
        ctx.lookedAt = List.of(); // a look and its shuffle happen within one resolution
        if (ctx.interactive()) {
            ctx.rebind(game);
        }
        ctx.collectResolved(game);
        if (ctx.scenario != null) {
            if (ctx.scenario.priority(game, this)) {
                return true;
            }
            pass(game);
            return false;
        }
        if (!ctx.done() && responseWindowOpens(game)) {
            if (respond(game)) {
                return true;
            }
            pass(game);
            return false;
        }
        if (!ctx.done() && game.getStack().isEmpty()) {
            if (ctx.pendingDisruption != null && settle(game)) {
                // The end-step chain cast the next disruption card: XMage asks our seat again with it on the stack.
                return true;
            }
            reportResolved(game);
            PhaseStep step = game.getTurnStepType();
            if (!ctx.done() && game.isActivePlayer(getId()) && Steps.isActionWindow(step)) {
                if (step == PhaseStep.END_TURN && ctx.endOfTurnReported == ctx.ourTurns) {
                    // The end-step window has closed. After a disruption, the goal reads this view.
                    reportAfterDisruption(game);
                } else {
                    if (takeAction(game)) {
                        return true;
                    }
                    if (step == PhaseStep.END_TURN && reportEndOfTurn(game)) {
                        // Not a pass: XMage runs state-based actions and puts triggers on the stack,
                        // then asks our seat again.
                        return true;
                    }
                }
            }
        }
        pass(game);
        return false;
    }

    /** Action decisions until the pilot passes. True when an action was taken: XMage asks priority again. */
    private boolean takeAction(Game game) {
        return decideActions(game, null);
    }

    /**
     * The action decisions of one window: the main-phase, upkeep and end-step window ({@code response} null), or
     * the response window. Each decision lists the legal actions; a failed action restores the game and the next
     * decision lists the actions left. True when an action was taken: XMage asks priority again. A response sends
     * no cast event and activates under its target hint.
     */
    private boolean decideActions(Game game, Response response) {
        retryAfterNewMana(game);
        List<ActionList.Entry> legal = ActionList.legal(this, game, ctx);
        // After a failed response the window asks again even with no action left: the service's last reply passes.
        boolean failedResponse = false;
        while ((!legal.isEmpty() || failedResponse) && !ctx.done()) {
            int seq = ctx.nextSeq();
            Messages.View view = decisionView(game, wire(legal), List.of());
            Messages.Reply reply = ctx.ask(game, response == null
                    ? new Messages.DecideAction("decide", seq, "action", view)
                    : new Messages.DecideRespond("decide", seq, "respond", view, response.payload()));
            if (reply == null) {
                return false;
            }
            if (reply.keep() != null || reply.cards() != null || reply.option() != null || reply.what() != null) {
                ctx.badReply(game, "an action reply takes only action: a listed id, or null to pass");
                return false;
            }
            if (reply.action() == null) {
                return false;
            }
            ActionList.Entry chosen = null;
            for (ActionList.Entry e : legal) {
                if (e.action().id().equals(reply.action())) {
                    chosen = e;
                }
            }
            if (chosen == null) {
                ctx.badReply(game, "action " + reply.action() + " is not in the list");
                return false;
            }
            int bookmark = game.bookmarkState();
            boolean activated;
            ctx.hint = response == null ? null : response.hint();
            try {
                activated = activateAbility(chosen.ability().copy(), game);
            } finally {
                ctx.hint = null;
            }
            if (activated) {
                game.removeBookmark(bookmark); // as XMage's own players do: saved states must not pile up
                if (ActionList.isActivation(chosen.ability())) {
                    ctx.activations.merge(chosen.key(), 1, Integer::sum);
                }
                Spell spell = chosen.ability() instanceof SpellAbility
                        ? game.getStack().getSpell(chosen.ability().getSourceId(), false)
                        : null;
                if (spell != null) {
                    ctx.pendingCasts.add(new GameCtx.PendingCast(spell, chosen.action().card(), chosen.action().face()));
                }
                if (response != null) {
                    // The counter predicate never sees a response: no cast event.
                    ctx.line(game, "RESPOND " + chosen.action().card());
                    return true;
                }
                ctx.line(game, "PILOT " + chosen.action().kind() + " " + chosen.action().card());
                if (spell != null && ctx.start.watchCasts()) {
                    reportCast(game, spell, chosen.action());
                }
                return true;
            }
            restoreState(bookmark, chosen.ability().getRule(), game);
            if (ctx.interactive()) {
                ctx.rebind(game); // the restore replaced the stack's objects with copies
            }
            ctx.failedThisTurn.add(chosen.key());
            failedResponse = response != null;
            ctx.manaAtFailure.clear();
            ctx.manaAtFailure.addAll(untappedManaSources(game));
            ctx.poolAtFailure = getManaPool().getMana().count();
            ctx.line(game, (response == null ? "PILOT cannot " + chosen.action().kind() : "RESPOND cannot") + " " + chosen.action().card());
            legal = ActionList.legal(this, game, ctx);
        }
        return false;
    }

    // ------------------------------------------------------------ the response window (ADR 0013)

    /** One response: the respond payload, and the target hint its activation takes. */
    private record Response(Messages.Respond payload, GameCtx.TargetHint hint) {
    }

    /**
     * True at our seat's first priority with the pending disruption card on top of the stack: the window opens once
     * per disruption card. Only an interactive game has a pending disruption card.
     */
    private boolean responseWindowOpens(Game game) {
        GameCtx.PendingDisruption pending = ctx.pendingDisruption;
        if (pending == null || pending.responded || game.getStack().isEmpty()
                || !pending.cardId.equals(game.getStack().getFirst().getSourceId())) {
            return false;
        }
        pending.responded = true;
        return true;
    }

    /**
     * The response window: respond decisions over the legal actions, unchanged, with the disruption card and its
     * victims. No legal action sends no decision. After a failed response one more decision goes out, with no action
     * when none is left, so the service's last reply never names a card that failed. While a response activates, our
     * target choice prefers the disruption card, then the first victim. True when a response was cast or activated.
     */
    private boolean respond(Game game) {
        GameCtx.PendingDisruption pending = ctx.pendingDisruption;
        StackObject threat = game.getStack().getFirst();
        List<Messages.RespondVictim> victims = new ArrayList<>();
        for (GameCtx.Aim aim : pending.victims) {
            victims.add(new Messages.RespondVictim(aim.id, aim.card, aim.face));
        }
        UUID victim = pending.victims.isEmpty() ? null : objectIdOf(pending.victims.get(0).id);
        Messages.Respond payload = new Messages.Respond(new Messages.Threat("s:" + threat.getId(), pending.card), victims);
        return decideActions(game, new Response(payload, new GameCtx.TargetHint(threat.getId(), victim)));
    }

    /**
     * The target hint (ADR 0013): while a response activates, each target takes the disruption card when it accepts
     * it, then the first victim. False leaves the choice to XMage.
     */
    private boolean chooseHintedTarget(Target target, Ability source, Game game) {
        GameCtx.TargetHint hint = ctx.hint;
        for (UUID id : new UUID[]{hint.threat(), hint.victim()}) {
            if (id != null && !target.contains(id) && target.canTarget(getId(), id, source, game)) {
                target.addTarget(id, source, game);
                if (target.contains(id)) {
                    return true;
                }
            }
        }
        return false;
    }

    /**
     * Phase 3E, Obeka runs 112, 141 and 149: an action that failed this turn is offered again once
     * a new mana source is ours (a land drop, a fetched land, a resolved mana rock) or the pool
     * holds more mana than at the last failure. The retired engine offered a cast whenever the mana paid
     * for it. An action that fails again with no new mana stays blocked, so the retry cannot loop.
     */
    private void retryAfterNewMana(Game game) {
        if (ctx.failedThisTurn.isEmpty()) {
            return;
        }
        int pool = getManaPool().getMana().count();
        if (pool < ctx.poolAtFailure) {
            ctx.poolAtFailure = pool; // the pool emptied since the failure: a later rise is new mana
        }
        if (pool > ctx.poolAtFailure
                || !ctx.manaAtFailure.containsAll(untappedManaSources(game))) {
            ctx.failedThisTurn.clear();
        }
    }

    /** Our untapped permanents with a mana ability we can activate now. */
    private Set<UUID> untappedManaSources(Game game) {
        Set<UUID> out = new HashSet<>();
        for (Permanent p : game.getBattlefield().getAllActivePermanents(getId())) {
            if (!p.isTapped() && !p.getAbilities(game).getAvailableActivatedManaAbilities(Zone.BATTLEFIELD, getId(), game).isEmpty()) {
                out.add(p.getId());
            }
        }
        return out;
    }

    /** One resolved event, on an empty stack, when the pilot's spells resolved since the last one. */
    private void reportResolved(Game game) {
        if (ctx.resolved.size() > ctx.resolvedReported) {
            ctx.resolvedReported = ctx.resolved.size();
            event(game, new Messages.EventResolved("event", ctx.nextSeq(), "resolved", eventView(game)), PLAIN_REPLIES);
        }
    }

    /**
     * end_of_turn, once per turn, after the end-step window closes. The service may answer disrupt
     * (spec §7). True after a disruption: the caller does not pass, so XMage checks state-based
     * actions (a commander goes home) and puts the dies triggers on the stack. Once they resolve, the
     * next empty-stack priority of this end step sends the after view.
     * <p>
     * With the scripted opponent the service may answer oppose instead (ADR 0013): the end-step sequence starts. The
     * opponent casts the card here, our seat settles it at its first empty-stack priority, and each opposed event of
     * the sequence may take oppose again. After the last one, the next empty-stack priority sends the after view.
     */
    private boolean reportEndOfTurn(Game game) {
        if (ctx.endOfTurnReported == ctx.ourTurns) {
            return false;
        }
        ctx.endOfTurnReported = ctx.ourTurns;
        ctx.snapshot(game, this);
        Messages.Reply reply = event(game,
                new Messages.EventEndOfTurn("event", ctx.nextSeq(), "end_of_turn", eventView(game), false),
                ctx.interactive() ? OPPOSE_REPLIES : END_OF_TURN_REPLIES);
        if (reply != null && "oppose".equals(reply.what())) {
            ctx.endStepSequence = true;
            ctx.afterDisruption = true;
            oppose(game, reply.card(), reply.targets());
            return true;
        }
        return reply != null && "disrupt".equals(reply.what()) && disrupt(game, reply.remove());
    }

    /**
     * Moves every named permanent to its owner's graveyard in one move, so a board wipe is
     * simultaneous. The move is not a destroy: indestructible and regeneration do nothing (spec §7).
     * False, and an engine error, when a name is not a permanent of ours.
     */
    private boolean disrupt(Game game, List<String> remove) {
        Set<Card> victims = new LinkedHashSet<>();
        for (String id : remove) {
            Permanent permanent = permanentOf(game, id);
            if (permanent == null) {
                ctx.badReply(game, "disrupt names " + id + ", which is not a permanent of ours");
                return false;
            }
            victims.add(permanent);
        }
        for (Card victim : victims) {
            ctx.line(game, "DISRUPT remove " + ctx.names.card(victim));
        }
        moveCards(victims, Zone.GRAVEYARD, null, game);
        ctx.afterDisruption = true;
        return true;
    }

    /** The permanent of our battlefield with the view id "p:" and its object id; null otherwise. */
    private Permanent permanentOf(Game game, String id) {
        if (id == null || !id.startsWith("p:")) {
            return null;
        }
        UUID objectId;
        try {
            objectId = UUID.fromString(id.substring(2));
        } catch (IllegalArgumentException e) {
            return null;
        }
        Permanent permanent = game.getPermanent(objectId);
        return permanent != null && permanent.isControlledBy(getId()) ? permanent : null;
    }

    /**
     * The after view: end_of_turn once more, with after_disruption, after a disruption this end step.
     * A second snapshot goes first: the service puts the turn's goal lines after the last one.
     */
    private void reportAfterDisruption(Game game) {
        if (!ctx.afterDisruption) {
            return;
        }
        ctx.afterDisruption = false;
        ctx.endStepSequence = false;
        ctx.snapshot(game, this);
        event(game, new Messages.EventEndOfTurn("event", ctx.nextSeq(), "end_of_turn", eventView(game), true),
                PLAIN_REPLIES);
    }

    /**
     * The cast event (spec §7): our spell is on the stack with its costs paid. On counter the spell
     * goes to its owner's graveyard and never resolves. Spell.counter moves it without the COUNTER
     * event, so "can't be countered" does not stop it. A commander then goes home through XMage's
     * command-zone choice, and its tax still rises: XMage counted the cast from the command zone.
     */
    private void reportCast(Game game, Spell spell, Messages.WireAction action) {
        Messages.Reply reply = event(game, new Messages.EventCast("event", ctx.nextSeq(), "cast", eventView(game),
                new Messages.SpellRef("s:" + spell.getId(), action.card(), action.face())),
                ctx.interactive() ? OPPOSE_REPLIES : CAST_REPLIES);
        if (reply != null && "counter".equals(reply.what())) {
            ctx.line(game, "DISRUPT counter " + action.card());
            spell.counter(null, game);
        } else if (reply != null && "oppose".equals(reply.what())) {
            oppose(game, reply.card(), reply.targets());
        }
    }

    // ------------------------------------------------------------ interactive disruption (ADR 0013)

    /**
     * The scripted opponent casts {@code card} at the first of {@code targets} it can target and affect, inside
     * this priority (Opposition). On a cast the card waits on the stack, and our seat settles it at its first
     * empty-stack priority. A card that is not cast settles at once: not_cast, and the game is as it was. Inside an
     * end-step sequence the service may answer that not_cast with the next oppose. True when a card is now pending.
     * A game that has ended casts nothing: the card is not cast.
     */
    private boolean oppose(Game game, String card, List<String> targets) {
        ScriptedOpponent opponent = scriptedOpponent(game);
        CardInfo info = opponent == null ? null : opponent.card(card);
        if (info == null) {
            ctx.badReply(game, "oppose names " + card + ", which is not a card of start_game.opponent");
            return false;
        }
        if (ctx.pendingDisruption != null) {
            ctx.badReply(game, "oppose while " + ctx.pendingDisruption.card + " is on the stack: one disruption card at a time");
            return false;
        }
        List<UUID> candidates = new ArrayList<>();
        for (String id : targets) {
            UUID objectId = objectIdOf(id);
            if (objectId == null) {
                ctx.badReply(game, "oppose targets " + id + ", which is not an s: or p: id");
                return false;
            }
            candidates.add(objectId);
        }
        Map<UUID, String> battlefield = new LinkedHashMap<>();
        for (Permanent p : game.getBattlefield().getAllActivePermanents(getId())) {
            battlefield.put(p.getId(), ctx.names.card(p));
        }
        Opposition.Attempt attempt = game.hasEnded()
                ? new Opposition.Attempt(null, null, Opposition.CANNOT_CAST)
                : Opposition.attempt(game, opponent, info, candidates);
        ctx.rebind(game); // the attempt bookmarks, and a failure restores: our pending spells are copies now
        GameCtx.Aim aim = attempt.target() == null ? null : aimAt(game, attempt.target());
        if (attempt.cast()) {
            List<GameCtx.Aim> aims = attempt.target() != null ? (aim == null ? List.of() : List.of(aim)) : creatures(game);
            ctx.pendingDisruption = new GameCtx.PendingDisruption(attempt.card().getId(), card, aims, battlefield);
            ctx.line(game, "OPPOSE " + card + (attempt.target() == null ? "" : " on " + (aim == null ? "?" : aim.card)));
            return true;
        }
        ctx.line(game, "OPPOSE " + card + " not cast (" + attempt.reason() + ")");
        List<Messages.Victim> victims = aim == null ? List.of() : List.of(new Messages.Victim(aim.id, aim.card, aim.face, false));
        return opposeNext(game, event(game, new Messages.EventNotCast("event", ctx.nextSeq(), "opposed", eventView(game), card,
                "not_cast", attempt.reason(), victims, List.of()), opposedReplies()));
    }

    /** The answers an opposed event takes: oppose too, inside an end-step sequence (ADR 0013). */
    private Set<String> opposedReplies() {
        return ctx.endStepSequence ? OPPOSE_REPLIES : PLAIN_REPLIES;
    }

    /** Carries out an oppose answer to an opposed event. True when a card is now pending. */
    private boolean opposeNext(Game game, Messages.Reply reply) {
        return reply != null && "oppose".equals(reply.what()) && oppose(game, reply.card(), reply.targets());
    }

    /**
     * The victims of a disruption card with no target (Rout): our creatures as the card is cast, in battlefield
     * order, tokens and animated permanents included. The card's own text decides which of them die.
     */
    private List<GameCtx.Aim> creatures(Game game) {
        List<GameCtx.Aim> aims = new ArrayList<>();
        for (Permanent p : game.getBattlefield().getAllActivePermanents(getId())) {
            if (p.isCreature(game)) {
                aims.add(new GameCtx.Aim("p:" + p.getId(), ctx.names.card(p), Names.face(p), null));
            }
        }
        return aims;
    }

    /** The victim with this object id: one of our spells on the stack, or one of our permanents. Null otherwise. */
    private GameCtx.Aim aimAt(Game game, UUID objectId) {
        for (GameCtx.PendingCast cast : ctx.pendingCasts) {
            if (cast.spell().getId().equals(objectId)) {
                return new GameCtx.Aim("s:" + objectId, cast.card(), cast.face(), cast.spell());
            }
        }
        Permanent permanent = game.getPermanent(objectId);
        if (permanent != null && permanent.isControlledBy(getId())) {
            return new GameCtx.Aim("p:" + objectId, ctx.names.card(permanent), Names.face(permanent), null);
        }
        return null;
    }

    /**
     * The opposed event of the pending disruption card, at our first empty-stack priority after it left the stack:
     * by then state-based actions and dies triggers have resolved. A spell victim is hit when it was countered; a
     * permanent victim when it left the battlefield. Lost names the countered spells, then our permanents that are
     * gone since the cast. The card then leaves the game, so no disruption card stays in any zone. Inside an end-step
     * sequence the service may answer with the next oppose. True when a card is now pending.
     */
    private boolean settle(Game game) {
        GameCtx.PendingDisruption pending = ctx.pendingDisruption;
        ctx.pendingDisruption = null;
        ScriptedOpponent opponent = scriptedOpponent(game);
        if (opponent != null) {
            opponent.window().close();
            Opposition.remove(game, opponent, pending.cardId);
        }
        List<Messages.Victim> victims = new ArrayList<>();
        List<String> lost = new ArrayList<>();
        boolean hit = false;
        for (GameCtx.Aim aim : pending.victims) {
            boolean victimHit = aim.spell != null
                    ? aim.spell.isCountered()
                    : game.getPermanent(UUID.fromString(aim.id.substring(2))) == null;
            victims.add(new Messages.Victim(aim.id, aim.card, aim.face, victimHit));
            if (victimHit && aim.spell != null) {
                lost.add(aim.card);
            }
            hit |= victimHit;
        }
        for (Map.Entry<UUID, String> before : pending.battlefield.entrySet()) {
            Permanent now = game.getPermanent(before.getKey());
            if (now == null || !now.isControlledBy(getId())) {
                lost.add(before.getValue());
            }
        }
        String outcome = hit ? "hit" : "answered";
        ctx.line(game, "OPPOSED " + pending.card + " " + outcome);
        return opposeNext(game, event(game, new Messages.EventOpposed("event", ctx.nextSeq(), "opposed", eventView(game),
                pending.card, outcome, victims, lost), opposedReplies()));
    }

    /**
     * A game that ends while a disruption card is pending still sends its opposed event, before game_over. An oppose
     * answer then gets not_cast at once: an ended game casts nothing.
     */
    void settleAtGameOver(Game game) {
        if (ctx.pendingDisruption != null && !ctx.done()) {
            settle(game);
        }
    }

    /** The scripted opponent of this game; null against the idle opponent. */
    private ScriptedOpponent scriptedOpponent(Game game) {
        for (UUID id : game.getOpponents(getId())) {
            if (game.getPlayer(id) instanceof ScriptedOpponent opponent) {
                return opponent;
            }
        }
        return null;
    }

    /** The object id of a wire id: "s:" and a spell id, or "p:" and a permanent id; null otherwise. */
    static UUID objectIdOf(String id) {
        if (id == null || !(id.startsWith("s:") || id.startsWith("p:"))) {
            return null;
        }
        try {
            return UUID.fromString(id.substring(2));
        } catch (IllegalArgumentException e) {
            return null;
        }
    }

    /**
     * Sends one event and checks the reply against the answers this event takes. Stop ends the game
     * here. Returns the reply for any other allowed answer; null when the game is done or the reply
     * was bad (the game then ends with engine_error).
     */
    private Messages.Reply event(Game game, Messages.InGame message, Set<String> allowed) {
        Messages.Reply reply = ctx.ask(game, message);
        if (reply == null) {
            return null;
        }
        String what = reply.what();
        if (what == null || !allowed.contains(what)) {
            ctx.badReply(game, "the " + message.kind() + " event takes do=" + String.join(", ", new TreeSet<>(allowed)) + ", got " + what);
            return null;
        }
        boolean disrupt = "disrupt".equals(what);
        if (disrupt ? reply.remove() == null || reply.remove().isEmpty() : reply.remove() != null) {
            ctx.badReply(game, "remove goes with do=disrupt, and disrupt names at least one permanent");
            return null;
        }
        boolean oppose = "oppose".equals(what);
        if (oppose ? reply.card() == null || reply.targets() == null : reply.card() != null || reply.targets() != null) {
            ctx.badReply(game, "card and targets go with do=oppose, and oppose names both");
            return null;
        }
        if ("stop".equals(what)) {
            ctx.stop(game);
            return null;
        }
        return reply;
    }

    // ------------------------------------------------------------ picks

    @Override
    public boolean chooseTarget(Outcome outcome, Cards cards, TargetCard target, Ability source, Game game) {
        return forwardPick(cards, target, source, game) || super.chooseTarget(outcome, cards, target, source, game);
    }

    @Override
    public boolean choose(Outcome outcome, Cards cards, TargetCard target, Ability source, Game game) {
        if (!game.isSimulation() && !ctx.done() && isBottomOrder(target)) {
            return bottomInPickOrder(cards, target, source, game) || super.choose(outcome, cards, target, source, game);
        }
        return forwardPick(cards, target, source, game) || super.choose(outcome, cards, target, source, game);
    }

    /**
     * Forwards a library pick to the service. True when the target has its minimum, or when a search
     * with a stated quality ends short of it (CR 701.23b); false leaves it to XMage.
     */
    private boolean forwardPick(Cards cards, TargetCard target, Ability source, Game game) {
        String purpose = purposeOf(target);
        if (game.isSimulation() || ctx.done() || purpose == null) {
            return false;
        }
        if (purpose.equals("order_top")) {
            noteLook(cards);
            if (oneName(cards, game)) {
                return false; // the order of identical cards is moot: the retired engine asked nothing either
            }
        }
        return pickCards(purpose, cards, target, source, game);
    }

    /**
     * tutor for a library search, scry for a scry, order_top for the order of cards put back on top
     * (XMage: "card ORDER to put on the TOP of your library"), other for another look at library
     * cards, else null.
     */
    static String purposeOf(TargetCard target) {
        if (target instanceof TargetCardInLibrary) {
            return "tutor";
        }
        String message = target.getFilter().getMessage().toLowerCase(Locale.ROOT);
        if (message.contains("(scry)")) {
            return "scry";
        }
        if (message.contains("order to put on the top")) {
            return "order_top";
        }
        return target.getZone() == Zone.LIBRARY ? "other" : null;
    }

    /** XMage's "card ORDER to put on the BOTTOM of your library". */
    static boolean isBottomOrder(TargetCard target) {
        return target.getFilter().getMessage().toLowerCase(Locale.ROOT).contains("order to put on the bottom");
    }

    /**
     * The bottom order of a scry, as the retired engine did it: the first card put on the bottom stays above
     * the next. XMage offers the cards in the order the scry picked them and puts each chosen card
     * down in turn ("last one chosen will be bottommost"), so the answer is the first card offered.
     */
    private boolean bottomInPickOrder(Cards cards, TargetCard target, Ability source, Game game) {
        Set<UUID> possible = target.possibleTargets(getId(), source, game);
        for (UUID id : cards) {
            if (possible.contains(id) && !target.getTargets().contains(id)) {
                add(target, id, source, game);
                return true;
            }
        }
        return false;
    }

    /** Remembers the cards of the current look: a later order call of the same look holds fewer of them. */
    private void noteLook(Cards cards) {
        List<UUID> ids = new ArrayList<>(cards);
        if (!ctx.lookedAt.containsAll(ids)) {
            ctx.lookedAt = List.copyOf(ids);
        }
    }

    private boolean oneName(Cards cards, Game game) {
        Set<String> names = new java.util.HashSet<>();
        for (UUID id : cards) {
            names.add(ctx.names.card(game, id));
        }
        return names.size() <= 1;
    }

    /** A Brainstorm-style effect: its card from hand goes on top of the library. */
    static boolean isPutBack(Ability source) {
        for (Mode mode : source.getModes().values()) {
            for (Effect effect : mode.getEffects()) {
                if (effect instanceof BrainstormEffect) {
                    return true;
                }
            }
        }
        return false;
    }

    /**
     * One put_back pick: a card from hand to put on top, one option per name, as the retired engine offered
     * it. XMage asks once per card, so the last pick ends on top. False when no hand card fits or no
     * option comes back (a failed game or a bad reply): chooseTarget returns it to XMage's caller as
     * is, with no card chosen. The AI player does not pick a card in the pilot's place.
     */
    private boolean putBack(Target target, Ability source, Game game) {
        Set<UUID> possible = target.possibleTargets(getId(), source, game);
        List<UUID> ids = new ArrayList<>();
        List<Messages.PickOption> options = new ArrayList<>();
        Set<String> names = new java.util.HashSet<>();
        for (UUID id : getHand()) {
            String name = ctx.names.card(game, id);
            if (possible.contains(id) && names.add(name)) {
                ids.add(id);
                options.add(new Messages.PickOption("o" + (options.size() + 1), name, name));
            }
        }
        if (options.isEmpty()) {
            return false;
        }
        int index = askOption(game, "put_back", options, null, List.of());
        if (index < 0) {
            return false;
        }
        add(target, ids.get(index), source, game);
        ctx.line(game, "PILOT put_back " + options.get(index).card());
        return true;
    }

    @Override
    public boolean chooseUse(Outcome outcome, String message, String secondMessage, String trueText, String falseText,
                             Ability source, Game game) {
        if (!game.isSimulation() && !ctx.done() && SHUFFLE_QUESTION.equals(message)) {
            Boolean shuffle = askShuffle(game);
            if (shuffle != null) {
                return shuffle;
            }
        }
        if (!game.isSimulation() && Diagnostics.payLife() && Diagnostics.isPayLifeQuestion(message)) {
            ctx.line(game, "DIAG pay-life " + (source == null ? "?" : ctx.names.card(game.getObject(source.getSourceId()))));
            return true;
        }
        return super.chooseUse(outcome, message, secondMessage, trueText, falseText, source, game);
    }

    /**
     * The shuffle pick: do not shuffle, or shuffle. Its seen list is the cards the last look put back
     * on top, top first, which the pilot reads to keep a goal card where it is. Null leaves it to XMage.
     */
    private Boolean askShuffle(Game game) {
        List<String> seen = new ArrayList<>();
        Library library = getLibrary();
        // getTopCards is a LinkedHashSet filled from the library's top: its order is top first.
        // A top card counts only while its id is in the last look (ADR 0003): a stale look shows
        // fewer cards, never an unseen one.
        for (Card card : library.getTopCards(game, ctx.lookedAt.size())) {
            if (!ctx.lookedAt.contains(card.getId())) {
                break;
            }
            seen.add(ctx.names.card(card));
        }
        List<Messages.PickOption> options = List.of(
                new Messages.PickOption(NO_SHUFFLE, "Do not shuffle", null),
                new Messages.PickOption(SHUFFLE, "Shuffle the library", null));
        int index = askOption(game, "shuffle", options, List.copyOf(seen), List.of());
        if (index < 0) {
            return null;
        }
        ctx.line(game, "PILOT shuffle " + options.get(index).id());
        return index == 1;
    }

    /**
     * One pick decision per card, as in the retired engine: the service picks a card or stops (the none
     * option, offered once the target has its minimum; a search with a stated quality offers it
     * below its minimum too, CR 701.23b). True when the target has its minimum, or when such a
     * search ends short of it without a failed game.
     */
    private boolean pickCards(String purpose, Cards cards, TargetCard target, Ability source, Game game) {
        Set<UUID> possible = target.possibleTargets(getId(), source, game);
        boolean search = purpose.equals("tutor");
        // A search shows only the offered options, never the whole library (ADR 0003, controller
        // ruling); a look shows every card this pick has revealed to our seat.
        List<UUID> offered = new ArrayList<>();
        List<MageObject> shown = new ArrayList<>();
        for (UUID id : cards) {
            boolean isOffered = possible.contains(id);
            if (isOffered) {
                offered.add(id);
            }
            if (!search || isOffered) {
                MageObject object = game.getObject(id);
                if (object != null) {
                    shown.add(object);
                }
            }
        }
        List<String> seen = null;
        if (search) {
            // A search must not show the library order: sort by card, face, then object id.
            offered.sort(Comparator.comparing((UUID id) -> ctx.names.card(game, id))
                    .thenComparing(id -> Names.face(game.getObject(id)))
                    .thenComparing(UUID::toString));
        } else {
            // Every looked-at card, top first, whatever is legal and whatever this look already picked.
            seen = new ArrayList<>();
            for (UUID id : cards) {
                seen.add(ctx.names.card(game, id));
            }
            seen = List.copyOf(seen);
        }
        String stopLabel = purpose.equals("scry") ? "Keep the rest on top" : "Find nothing";
        int min = target.getMinNumberOfTargets();
        // CR 701.23b: a player searching a hidden zone for cards with a stated quality need not find
        // them, even when the target is otherwise mandatory. TargetCardInLibrary already tells us
        // this: its constructor sets isRequired() false exactly when its filter has predicates (a
        // stated quality), matching the retired engine's `!search.Filter.IsEmpty` check
        // (EffectInterpreter.cs). A plain "a card" search (Demonic Tutor, Vampiric Tutor) stays
        // mandatory: no none option while the minimum is unmet.
        boolean statedQuality = search && !target.isRequired();
        while (target.getTargets().size() < target.getMaxNumberOfTargets() && !ctx.done()) {
            List<UUID> left = new ArrayList<>();
            for (UUID id : offered) {
                if (!target.getTargets().contains(id)) {
                    left.add(id);
                }
            }
            if (left.isEmpty()) {
                break;
            }
            List<Messages.PickOption> options = new ArrayList<>();
            for (int i = 0; i < left.size(); i++) {
                String name = ctx.names.card(game, left.get(i));
                options.add(new Messages.PickOption("o" + (i + 1), name, name));
            }
            if (target.getTargets().size() >= min || statedQuality) {
                options.add(new Messages.PickOption(NONE, stopLabel, null));
            }
            int index = askOption(game, purpose, options, seen, shown);
            if (index < 0) {
                break;
            }
            UUID id = left.get(index);
            int before = target.getTargets().size();
            add(target, id, source, game);
            if (target.getTargets().size() == before) {
                ctx.fail(game, "XMage refused the " + purpose + " pick " + ctx.names.card(game, id));
                break;
            }
            ctx.line(game, "PILOT " + purpose + " " + ctx.names.card(game, id));
        }
        // A stated-quality search that ended without an engine failure is a legitimate fail-to-find,
        // even short of its (nominal) minimum: XMage's own search effects shuffle the library either way.
        return target.getTargets().size() >= min || (statedQuality && !ctx.done());
    }

    @Override
    public boolean choose(Outcome outcome, Choice choice, Game game) {
        if (!game.isSimulation() && chooseMatchedColor(outcome, choice)) {
            return true;
        }
        if (!game.isSimulation() && !ctx.done() && choice.getHintType() == ChoiceHintType.CARD && !choice.isKeyChoice()) {
            List<String> names = new ArrayList<>();
            for (String name : ctx.deck.names()) {
                String xmage = ctx.deck.xmageName(name);
                if (xmage != null && choice.getChoices().contains(xmage)) {
                    names.add(name);
                }
            }
            List<Messages.PickOption> options = new ArrayList<>();
            for (int i = 0; i < names.size(); i++) {
                options.add(new Messages.PickOption("o" + (i + 1), names.get(i), names.get(i)));
            }
            int index = options.isEmpty() ? -1 : askOption(game, "name", options, null, List.of());
            if (index >= 0) {
                choice.setChoice(ctx.deck.xmageName(names.get(index)));
                ctx.line(game, "PILOT name " + names.get(index));
                return true;
            }
        }
        return super.choose(outcome, choice, game);
    }

    @Override
    public TriggeredAbility chooseTriggeredAbility(List<TriggeredAbility> abilities, Game game) {
        if (!game.isSimulation() && !ctx.done() && abilities.size() > 1) {
            List<Messages.PickOption> options = new ArrayList<>();
            for (int i = 0; i < abilities.size(); i++) {
                String name = ctx.names.card(game, abilities.get(i).getSourceId());
                options.add(new Messages.PickOption("o" + (i + 1), name + ": " + abilities.get(i).getRule(), name));
            }
            int index = askOption(game, "trigger_order", options, null, List.of());
            if (index >= 0) {
                ctx.line(game, "PILOT trigger_order " + options.get(index).card());
                return abilities.get(index);
            }
        }
        return super.chooseTriggeredAbility(abilities, game);
    }

    /** One pick decision. The index of the chosen option; -1 for none, a failed game or a bad reply. */
    private int askOption(Game game, String purpose, List<Messages.PickOption> options, List<String> seen,
                          Collection<? extends MageObject> shown) {
        Messages.Reply reply = ctx.ask(game, new Messages.DecidePick("decide", ctx.nextSeq(), "pick",
                decisionView(game, List.of(), shown), new Messages.Pick(purpose, options, seen)));
        if (reply == null) {
            return -1;
        }
        for (int i = 0; i < options.size(); i++) {
            if (options.get(i).id().equals(reply.option())) {
                return NONE.equals(reply.option()) ? -1 : i;
            }
        }
        ctx.badReply(game, "option " + reply.option() + " is not offered");
        return -1;
    }

    // ------------------------------------------------------------ helpers

    private static List<Messages.WireAction> wire(List<ActionList.Entry> legal) {
        List<Messages.WireAction> out = new ArrayList<>(legal.size());
        for (ActionList.Entry e : legal) {
            out.add(e.action());
        }
        return out;
    }

    private Messages.View decisionView(Game game, List<Messages.WireAction> actions, Collection<? extends MageObject> shown) {
        return ViewBuilder.forDecision(game, this, ctx, actions, shown);
    }

    private Messages.View eventView(Game game) {
        return ViewBuilder.forEvent(game, this, ctx);
    }

    private static void add(Target target, UUID id, Ability source, Game game) {
        if (target.isNotTarget()) {
            target.add(id, game);
        } else {
            target.addTarget(id, source, game);
        }
    }
}
