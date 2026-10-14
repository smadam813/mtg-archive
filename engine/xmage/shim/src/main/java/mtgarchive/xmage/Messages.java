package mtgarchive.xmage;

import com.google.gson.annotations.SerializedName;

import java.util.List;
import java.util.Map;

/**
 * The messages of wire protocol version 2 (roadmap, "Wire protocol"; version 2 adds start_game.opponent, the
 * oppose reply, the opposed event and the respond decide, ADR 0013). Components are camelCase;
 * {@link Wire#GSON} writes them in snake_case. The JSON Schema in engine/xmage/protocol is the contract.
 */
public final class Messages {

    public static final int PROTOCOL = 2;

    private Messages() {
    }

    /** A decide or an event: a message inside one game that the service answers once, with the same seq. */
    public interface InGame {
        int seq();

        String kind();
    }

    // ------------------------------------------------------------ service to sidecar

    public record Hello(String type, int protocol) {
    }

    public record DeckEntry(String name, int quantity) {
    }

    public record LoadDeck(String type, List<String> commanders, List<DeckEntry> cards) {
    }

    /**
     * One game. startLife, commanderDamage, startingPlayerDraws and freeMulligans are the game rules
     * (GLOSSARY.md, "Game rules"): the service sends its format's values, and the shim does not know the format.
     * opponent is null for the idle opponent; otherwise it seats the scripted opponent (ADR 0013).
     */
    public record StartGame(String type, String deckId, long seed, int horizon, int startLife, boolean commanderDamage,
                            boolean startingPlayerDraws, int freeMulligans, boolean watchCasts, boolean log,
                            Opponent opponent) {

        /** A game against the idle opponent. */
        public StartGame(String type, String deckId, long seed, int horizon, int startLife, boolean commanderDamage,
                         boolean startingPlayerDraws, int freeMulligans, boolean watchCasts, boolean log) {
            this(type, deckId, seed, horizon, startLife, commanderDamage, startingPlayerDraws, freeMulligans, watchCasts,
                    log, null);
        }

        /** The same game against another opponent. */
        public StartGame withOpponent(Opponent other) {
            return new StartGame(type, deckId, seed, horizon, startLife, commanderDamage, startingPlayerDraws,
                    freeMulligans, watchCasts, log, other);
        }
    }

    /** The scripted opponent: the names of its disruption cards, which the service's card table names. */
    public record Opponent(List<String> cards) {
    }

    /** A card-correctness fixture (spec §6.2): the start zones, then the steps in order. */
    public record Scenario(String type, List<String> commanders, List<DeckEntry> cards, long seed, ScenarioStart start,
                           List<ScenarioAction> actions) {
    }

    /** Our zones before the script; library is top first. */
    public record ScenarioStart(List<String> hand, List<StartPermanent> battlefield, List<String> library,
                                List<String> graveyard) {
    }

    /** A permanent before the script: tapped or not, and the card name it has chosen (Pithing Needle), or null. */
    public record StartPermanent(String card, boolean tapped, String named) {
    }

    /** One step: cast a card from hand, activate ability {@code ability} of a permanent, or answer the next pick. */
    public record ScenarioAction(String type, String card, String permanent, Integer ability, String pick) {
    }

    /**
     * Any reply to a decide or an event. Only the fields of the question's kind are set. {@code card} and
     * {@code targets} go with do=oppose: a card of start_game.opponent and the ordered candidate ids.
     */
    public record Reply(Integer seq, String action, Boolean keep, List<String> cards, String option,
                        @SerializedName("do") String what, List<String> remove, String card, List<String> targets) {

        public static Reply ofAction(int seq, String action) {
            return new Reply(seq, action, null, null, null, null, null, null, null);
        }

        public static Reply ofKeep(int seq, boolean keep) {
            return new Reply(seq, null, keep, null, null, null, null, null, null);
        }

        public static Reply ofCards(int seq, List<String> cards) {
            return new Reply(seq, null, null, cards, null, null, null, null, null);
        }

        public static Reply ofOption(int seq, String option) {
            return new Reply(seq, null, null, null, option, null, null, null, null);
        }

        public static Reply ofDo(int seq, String what) {
            return new Reply(seq, null, null, null, null, what, null, null, null);
        }

        public static Reply ofDisrupt(int seq, List<String> remove) {
            return new Reply(seq, null, null, null, null, "disrupt", remove, null, null);
        }

        /** The scripted opponent casts {@code card} at the first of {@code targets} it can target and affect. */
        public static Reply ofOppose(int seq, String card, List<String> targets) {
            return new Reply(seq, null, null, null, null, "oppose", null, card, targets);
        }
    }

    // ------------------------------------------------------------ sidecar to service

    public record HelloReply(String type, String engine, String version, int threads, int protocol) {
    }

    public record DeckReply(String type, String deckId, List<String> unimplemented) {
    }

    public record DecideAction(String type, int seq, String kind, View view) implements InGame {
    }

    public record DecideKeep(String type, int seq, String kind, View view, Keep keep) implements InGame {
    }

    /** A bottom or a discard: the service names exactly {@code count} cards from the hand. */
    public record DecideCount(String type, int seq, String kind, View view, int count) implements InGame {
    }

    public record DecidePick(String type, int seq, String kind, View view, Pick pick) implements InGame {
    }

    /**
     * The response window (ADR 0013): the scripted opponent's disruption card is on top of the stack. The view lists
     * our legal actions; the service answers {seq, action} with a listed id, or null to pass.
     */
    public record DecideRespond(String type, int seq, String kind, View view, Respond respond) implements InGame {
    }

    /** The disruption card on top of the stack, and the cards it aims at. */
    public record Respond(Threat threat, List<RespondVictim> victims) {
    }

    /** The disruption card: "s:" and its stack id, and its name, a card of start_game.opponent. */
    public record Threat(String id, String card) {
    }

    /** A card the disruption card aims at: a spell ("s:") or a permanent ("p:"), its decklist name and face. */
    public record RespondVictim(String id, String card, String face) {
    }

    public record EventEndOfTurn(String type, int seq, String kind, View view, boolean afterDisruption)
            implements InGame {
    }

    public record EventResolved(String type, int seq, String kind, View view) implements InGame {
    }

    /**
     * Our spell on the stack with its costs paid (spec §7). The shim sends it only when start_game
     * set watch_casts. The service answers continue, stop or counter.
     */
    public record EventCast(String type, int seq, String kind, View view, SpellRef spell) implements InGame {
    }

    /** A spell on the stack: "s:" and its object id, its decklist name, and the face it was cast as. */
    public record SpellRef(String id, String card, String face) {
    }

    /**
     * The fate of one disruption card the scripted opponent tried to cast (ADR 0013). outcome is hit, answered
     * or not_cast; victims are the cards it aimed at, each with hit; lost names our cards it took. The event
     * lists no actions. A not_cast outcome has its reason ({@link EventNotCast}); the other outcomes have none.
     */
    public interface Opposed extends InGame {
        View view();

        String card();

        String outcome();

        List<Victim> victims();

        List<String> lost();
    }

    /** An opposed event with outcome hit or answered. */
    public record EventOpposed(String type, int seq, String kind, View view, String card, String outcome,
                               List<Victim> victims, List<String> lost) implements Opposed {
    }

    /** An opposed event with outcome not_cast, and its reason: no_target or cannot_cast. */
    public record EventNotCast(String type, int seq, String kind, View view, String card, String outcome, String reason,
                               List<Victim> victims, List<String> lost) implements Opposed {
    }

    /** A card a disruption card aimed at: a spell ("s:") or a permanent ("p:"), and whether the card hit it. */
    public record Victim(String id, String card, String face, boolean hit) {
    }

    /**
     * The end of a game. {@code view} is the final view in the event form (no actions, no finds) on a
     * win or a life ending, so the service scores the goal on the state the game ended in; it is null
     * on every other reason.
     */
    public record GameOver(String type, int turns, String reason, String digest, String error, List<LogEntry> log,
                           View view) {
    }

    /**
     * A scenario's end. On an error, view, library, named and abilities are null. library is top first:
     * a scenario is a test harness, and no pilot view ever shows the library order.
     */
    public record ScenarioResult(String type, String error, View view, List<String> library, List<NamedValue> named,
                                 List<AbilityState> abilities, List<PickRecord> picks, List<LogEntry> log) {
    }

    /** A permanent's chosen card name (Pithing Needle). */
    public record NamedValue(String permanentId, String card, String value) {
    }

    /**
     * One activated ability of a permanent: its index in XMage's order and whether it is a mana
     * ability. locked is true when a rule-modifying effect forbids the activation (Pithing Needle).
     * activatable is true when the ability can be activated now: not locked, costs payable, timing met.
     */
    public record AbilityState(String permanentId, String card, int index, boolean mana, boolean locked, boolean activatable) {
    }

    /** One pick a scenario answered: its purpose, the offered option ids, what it showed, and the chosen id. */
    public record PickRecord(String purpose, List<String> options, List<String> seen, String chosen) {
    }

    public record Keep(List<String> hand, int mulligansTaken, int keepSize) {
    }

    /** {@code seen} is null for a search; for a scry or a look it lists the looked-at cards, top first. */
    public record Pick(String purpose, List<PickOption> options, List<String> seen) {
    }

    /** {@code card} is null on the stop option, id "none". */
    public record PickOption(String id, String label, String card) {
    }

    public record LogEntry(int turn, String step, String message) {
    }

    public record View(int turn, String step, int life, boolean landDropUsed, String pool,
                       List<String> hand, List<String> graveyard, List<String> exile, List<String> commandZone,
                       List<ViewPermanent> battlefield, int libraryCount, List<ManaSource> manaSources,
                       List<HandMana> handMana, List<ResolvedCast> resolved, Map<String, List<String>> finds,
                       List<WireAction> actions) {
    }

    public record ViewPermanent(String id, String card, String face, boolean tapped) {
    }

    public record ManaSource(String id, List<String> options) {
    }

    public record HandMana(String card, List<String> options) {
    }

    /** A resolved cast of ours: the card, the face it was cast as ("front" or "back"), and our turn. */
    public record ResolvedCast(String card, String face, int turn) {
    }

    public record WireAction(String id, String kind, String card, String face, String zone, String cost,
                             String permanentId, boolean spendsSource) {
    }
}
