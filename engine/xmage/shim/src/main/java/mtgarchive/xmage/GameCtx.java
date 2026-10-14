package mtgarchive.xmage;

import mage.game.Game;
import mage.game.permanent.Permanent;
import mage.game.stack.Spell;
import mage.players.Player;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.HashMap;
import java.util.HashSet;
import java.util.HexFormat;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.regex.Pattern;

/**
 * State for one game, shared by every copy of our seat (XMage copies players when it bookmarks or
 * restores game state). Holds the action log: every line goes into the digest; every line but the
 * library order goes into the wire log. Taken from the spike's GameCtx.
 */
final class GameCtx {

    private static final Pattern TAGS = Pattern.compile("<[^>]*>");

    /** The watchdog: a game that sends more decides and events than this ends with engine_error. */
    static final int MAX_DECISIONS = 10_000;

    /** The watchdog: a game that gives our seat priority more often than this ends with engine_error. */
    static final int MAX_PRIORITIES = 200_000;
    private static final Pattern IDS = Pattern.compile("\\[[0-9a-f]{3}\\]");

    final Messages.StartGame start;
    final LoadedDeck deck;
    final Names names;
    private final Decider decider;
    private final int maxDecisions;
    private final int maxPriorities;
    private final StringBuilder digestText = new StringBuilder(16 * 1024);
    private final List<Messages.LogEntry> log = new ArrayList<>();

    final Set<String> failedThisTurn = new HashSet<>();
    /** Our untapped mana sources and the mana in our pool at the last failed action (PilotPlayer.retryAfterNewMana). */
    final Set<UUID> manaAtFailure = new HashSet<>();
    int poolAtFailure;
    final Map<String, Integer> activations = new HashMap<>();
    /** The mana abilities whose own costs PilotPlayer pays now (PilotPlayer.activateAbility). */
    final Set<UUID> payingManaAbilities = new HashSet<>();
    /** The ability keys the activation cap hid this turn; each logs CAP once. */
    final Set<String> capped = new HashSet<>();
    /** The ids of our lands the ETB switch has logged (Diagnostics.ETB). */
    final Set<UUID> seenLands = new HashSet<>();
    final Deque<UUID> pendingBottom = new ArrayDeque<>();
    /** Spells the pilot cast that are still on the stack. */
    final List<PendingCast> pendingCasts = new ArrayList<>();
    /** Spells the pilot cast that resolved, in resolution order. */
    final List<Messages.ResolvedCast> resolved = new ArrayList<>();
    Boolean pendingKeep;
    UUID seatId;
    int ourTurns;
    int mulligansTaken;
    int resolvedReported;
    int endOfTurnReported;
    /** A disrupt moved permanents this end step: the next empty-stack priority sends the after view. */
    boolean afterDisruption;
    /**
     * Interactive only: the service answered this end step's end_of_turn with oppose, and the after view has not gone
     * out yet. Inside the sequence an opposed event takes oppose too (ADR 0013).
     */
    boolean endStepSequence;

    /** The scenario this context plays, or null in a game. */
    ScenarioScript scenario;

    /** The ids of the cards the current look puts back on top: a shuffle pick's seen list. */
    List<UUID> lookedAt = List.of();
    private int seq;
    private int lastXmageTurn = -1;
    private boolean openingLogged;
    private String error;
    private boolean stopped;
    private boolean horizonReached;
    /**
     * True once fail, stop or endAtHorizon has called game.end() on our own initiative (as opposed
     * to a real win or loss ending the game on its own). XMage crowns a winner for any game that
     * ends this way, so seatWonBeforeOurEnd, captured right before that call, is the trustworthy
     * value: a reread of hasWon() afterward would read back as a false win.
     */
    private boolean endedByUs;
    private boolean seatWonBeforeOurEnd;
    private int decisions;
    private int priorities;

    GameCtx(Messages.StartGame start, LoadedDeck deck, Decider decider) {
        this(start, deck, decider, MAX_DECISIONS, MAX_PRIORITIES);
    }

    /** Takes lower watchdog caps, for tests. */
    GameCtx(Messages.StartGame start, LoadedDeck deck, Decider decider, int maxDecisions, int maxPriorities) {
        this.start = start;
        this.deck = deck;
        this.names = new Names(deck);
        this.decider = decider;
        this.maxDecisions = maxDecisions;
        this.maxPriorities = maxPriorities;
    }

    int nextSeq() {
        return ++seq;
    }

    /** True once the game failed, the service answered stop or the horizon ended the game: no more questions go out. */
    boolean done() {
        return error != null || stopped || horizonReached;
    }

    /** Sends one decide or event; returns null when the game is done or the service gave no valid reply. */
    Messages.Reply ask(Game game, Messages.InGame message) {
        if (done()) {
            return null;
        }
        if (++decisions > maxDecisions) {
            fail(game, "game exceeded " + maxDecisions + " decisions");
            return null;
        }
        try {
            return decider.ask(message);
        } catch (DecisionFailure e) {
            fail(game, e.getMessage());
            return null;
        }
    }

    /** Ends the game with engine_error. The first failure's message is the one game_over reports. */
    void fail(Game game, String message) {
        if (error == null) {
            error = message;
        }
        line(game, "ERROR " + message);
        if (game != null) {
            captureWinBeforeOurEnd(game);
            game.end();
        }
    }

    void badReply(Game game, String message) {
        fail(game, "bad reply: " + message);
    }

    /** The service answered stop: end the game now. */
    void stop(Game game) {
        stopped = true;
        line(game, "STOP");
        captureWinBeforeOurEnd(game);
        game.end();
    }

    /**
     * Captures hasWon() for our seat before we call game.end() ourselves (fail, stop or
     * endAtHorizon): XMage crowns a winner for any game that ends this way, on top of a real win or
     * loss, so a reread after game.end() would read back as a false win. Only the first call (the
     * first reason we end the game) counts, matching fail's "first failure wins" rule.
     */
    private void captureWinBeforeOurEnd(Game game) {
        if (!endedByUs) {
            endedByUs = true;
            seatWonBeforeOurEnd = seatWon(game);
        }
    }

    /**
     * Called on every real (not simulated) priority of our seat. The horizon counts our turns, and
     * an extra turn is one of them: a new turn of ours past the horizon ends the game here. XMage's
     * stopOnTurn counts every turn; it stays as a backstop.
     */
    void onPriority(Game game, Player me) {
        if (Diagnostics.etb()) {
            for (Permanent p : game.getBattlefield().getAllActivePermanents(me.getId())) {
                if (p.isLand(game) && seenLands.add(p.getId())) {
                    line(game, "ETB " + names.card(p) + (p.isTapped() ? " tapped" : " untapped"));
                }
            }
        }
        if (++priorities > maxPriorities) {
            fail(game, "game exceeded " + maxPriorities + " priorities");
            return;
        }
        if (!openingLogged) {
            openingLogged = true;
            pendingKeep = null;
            pendingBottom.clear();
            if (scenario == null) {
                logOpening(game, me); // a scenario logs its zones once it has set them up
            }
        }
        int turn = game.getTurnNum();
        if (turn != lastXmageTurn) {
            lastXmageTurn = turn;
            failedThisTurn.clear();
            activations.clear();
            capped.clear();
            if (game.isActivePlayer(me.getId())) {
                if (ourTurns >= start.horizon()) {
                    endAtHorizon(game);
                    return;
                }
                ourTurns++;
            }
        }
    }

    /** The opening hand in the log, and the library order in the digest only. */
    void logOpening(Game game, Player me) {
        line(game, "OPENING hand=" + names.cards(me.getHand().getCards(game)));
        add(game, "LIBRARY " + names.cards(me.getLibrary().getCards(game)), false);
    }

    /** Ends the game before a turn of ours past the horizon: game_over reports horizon. */
    void endAtHorizon(Game game) {
        horizonReached = true;
        line(game, "HORIZON");
        if (game != null) {
            captureWinBeforeOurEnd(game);
            game.end();
        }
    }

    /** A spell the pilot cast, its decklist name, and the face it was cast as. */
    record PendingCast(Spell spell, String card, String face) {
    }

    /** True in an interactive game: start_game seats the scripted opponent (ADR 0013). */
    boolean interactive() {
        return start.opponent() != null;
    }

    /**
     * The scripted opponent's disruption card on the stack, from its cast until our seat settles it with the
     * opposed event: its object id and name, the victims it aims at, and our permanents when it was cast.
     */
    static final class PendingDisruption {
        final UUID cardId;
        final String card;
        final List<Aim> victims;
        /** Our permanents at the cast, with their names: the ones that are gone at the settle are lost. */
        final Map<UUID, String> battlefield;
        /** The response window of this card has opened: it opens once per disruption card. */
        boolean responded;

        PendingDisruption(UUID cardId, String card, List<Aim> victims, Map<UUID, String> battlefield) {
            this.cardId = cardId;
            this.card = card;
            this.victims = victims;
            this.battlefield = battlefield;
        }
    }

    /** One victim: its wire id, decklist name and face, and the spell it is while it is on the stack. */
    static final class Aim {
        final String id;
        final String card;
        final String face;
        Spell spell;

        Aim(String id, String card, String face, Spell spell) {
            this.id = id;
            this.card = card;
            this.face = face;
            this.spell = spell;
        }
    }

    /** The disruption card our seat has not settled yet; null when there is none. */
    PendingDisruption pendingDisruption;

    /**
     * The target hint of a response (ADR 0013): while our response activates, our target choice prefers the
     * disruption card, then its first victim. Null outside a response activation.
     */
    record TargetHint(UUID threat, UUID victim) {
    }

    /** The hint of the response that activates now; null otherwise. */
    TargetHint hint;

    /**
     * Interactive games only: binds each pending spell to the stack's current object of the same id. A restore
     * (ours, or XMage's own on a failed cast) replaces the stack's objects with copies, and a stale object would
     * never see its counter. Tier 2 and tier 3 never call this, so their games do not change.
     */
    void rebind(Game game) {
        for (int i = 0; i < pendingCasts.size(); i++) {
            PendingCast cast = pendingCasts.get(i);
            Spell now = game.getStack().getSpell(cast.spell().getId(), false);
            if (now != null && now != cast.spell()) {
                pendingCasts.set(i, new PendingCast(now, cast.card(), cast.face()));
            }
        }
        if (pendingDisruption != null) {
            for (Aim aim : pendingDisruption.victims) {
                Spell now = aim.spell == null ? null : game.getStack().getSpell(aim.spell.getId(), false);
                if (now != null) {
                    aim.spell = now;
                }
            }
        }
    }

    /**
     * Moves each pending cast that has left the stack to resolved, unless XMage countered it (a
     * fizzle counts as countered). The Spell object stays the live one: the pilot acts only on an
     * empty stack, so no rollback replaces it while it waits. True when a cast resolved.
     */
    boolean collectResolved(Game game) {
        boolean any = false;
        for (Iterator<PendingCast> it = pendingCasts.iterator(); it.hasNext(); ) {
            PendingCast cast = it.next();
            if (game.getStack().getSpell(cast.spell().getId(), false) != null) {
                continue;
            }
            it.remove();
            if (cast.spell().isCountered()) {
                line(game, "COUNTERED " + cast.card());
            } else {
                resolved.add(new Messages.ResolvedCast(cast.card(), cast.face(), ourTurns));
                line(game, "RESOLVED " + cast.card());
                any = true;
            }
        }
        return any;
    }

    String step(Game game) {
        return game == null || ourTurns == 0 ? "mulligan" : Steps.of(game.getTurnStepType());
    }

    void line(Game game, String text) {
        add(game, text, true);
    }

    /** An engine log message (HTML) from the table's INFO events. */
    void info(Game game, String html) {
        if (html == null) {
            return;
        }
        String text = TAGS.matcher(html).replaceAll("").replace('\n', ' ').trim();
        if (!text.isEmpty()) {
            line(game, text);
        }
    }

    /**
     * Our zones at the end of our turn (spike: GameCtx.snapshot).
     * The service places a turn's goal lines after the turn's last such line, by its "END life="
     * prefix (XmageGame.EndSnapshotPrefix); keep the prefix. A disrupted turn has two: one before
     * the disruption and one before the after view.
     */
    void snapshot(Game game, Player me) {
        List<String> bf = new ArrayList<>();
        for (Permanent p : game.getBattlefield().getAllActivePermanents(me.getId())) {
            bf.add(names.card(p) + (p.isTapped() ? "(T)" : ""));
        }
        bf.sort(null);
        line(game, "END life=" + me.getLife()
                + " lib=" + me.getLibrary().size()
                + " hand=" + names.cards(me.getHand().getCards(game))
                + " bf=" + bf
                + " gy=" + names.cards(me.getGraveyard().getCards(game)));
    }

    private void add(Game game, String text, boolean toLog) {
        String step = step(game);
        String norm = IDS.matcher(text).replaceAll("");
        digestText.append('T').append(ourTurns).append(' ').append(step).append(" | ").append(norm).append('\n');
        if (toLog) {
            log.add(new Messages.LogEntry(ourTurns, step, norm));
        }
    }

    /**
     * The game_over message (see {@link #reasonOf}). A game that ends before our last turn for no
     * reason the wire names is an engine_error.
     */
    Messages.GameOver gameOver(Game game) {
        // XMage crowns a winner for any game that ends on our own initiative (fail, stop or
        // endAtHorizon all call game.end()), even though nobody actually won or lost: a reread of
        // hasWon() after that call would read back as a false win. captureWinBeforeOurEnd freezes
        // the true value first; trust that over a post-end reread whenever we ended the game
        // ourselves. XMage's own stopOnTurn backstop (GameRunner's options.stopOnTurn) does not call
        // game.end() itself (GameImpl.checkStopOnTurnOption just records a draw), so it never trips
        // this path; theHorizonCountsOurTurns and theSameSeedPlaysTheSameGame exercise it and stay
        // on the live read below.
        boolean won = endedByUs ? seatWonBeforeOurEnd : seatWon(game);
        String reason = reasonOf(error != null, stopped, seatLost(game), won);
        if (reason.equals("horizon") && ourTurns < start.horizon()) {
            fail(null, "game ended early at turn " + ourTurns + " of " + start.horizon());
            reason = "engine_error";
        }
        Messages.View view = null;
        if (reason.equals("win") || reason.equals("life")) {
            try {
                view = finalView(game);
            } catch (RuntimeException e) {
                fail(null, "cannot build the final view: " + firstLine(e, null));
                reason = "engine_error";
            }
        }
        add(game, "GAME OVER reason=" + reason + " turns=" + ourTurns, false);
        return new Messages.GameOver("game_over", ourTurns, reason, sha(digestText), error,
                start.log() ? List.copyOf(log) : null, view);
    }

    /**
     * The view of a game that a win or a loss ended, in the event form. A win trigger is still on the
     * stack when the winning spell resolves, so no resolved event carried that spell: collect it here.
     * Only win and life reach this, and each needs our seat, so the seat is there.
     */
    private Messages.View finalView(Game game) {
        collectResolved(game);
        return ViewBuilder.forEvent(game, game.getPlayer(seatId), this);
    }

    /**
     * The game_over reason: engine_error, then stopped, then life (our seat lost), then win (our seat
     * won, for example by Thassa's Oracle), else horizon.
     */
    static String reasonOf(boolean failed, boolean stopped, boolean lost, boolean won) {
        return failed ? "engine_error" : stopped ? "stopped" : lost ? "life" : won ? "win" : "horizon";
    }

    private boolean seatLost(Game game) {
        if (game == null || seatId == null) {
            return false;
        }
        Player me = game.getPlayer(seatId);
        return me != null && me.hasLost();
    }

    private boolean seatWon(Game game) {
        if (game == null || seatId == null) {
            return false;
        }
        Player me = game.getPlayer(seatId);
        return me != null && me.hasWon();
    }

    /** game_over for a request that never started a game (unknown deck_id, unsupported option). */
    static Messages.GameOver errorOver(String message) {
        return new Messages.GameOver("game_over", 0, "engine_error", sha(""), message, null, null);
    }

    /** The first line of a stack trace: "java.lang.IllegalStateException: boom". */
    static String firstLine(Throwable e, String fallback) {
        if (e == null) {
            return fallback == null ? "unknown engine error" : fallback;
        }
        return e.toString().lines().findFirst().orElse(e.getClass().getName());
    }

    /** The first failure's message, or null. */
    String error() {
        return error;
    }

    /** The wire log so far. */
    List<Messages.LogEntry> log() {
        return List.copyOf(log);
    }

    /**
     * The digest of the normalized log text (the spike's sha_norm, not truncated): the full SHA-256
     * as 64 lowercase hex digits, as the wire schema's game_over.digest requires.
     */
    static String sha(CharSequence s) {
        try {
            return HexFormat.of().formatHex(
                    MessageDigest.getInstance("SHA-256").digest(s.toString().getBytes(StandardCharsets.UTF_8)));
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }
}
