package mtgarchive.xmage;

import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * The determinism self-test's service stand-in for an interactive game: the FIRST responder, plus a
 * scheduled counterspell, a scheduled removal and a scheduled board wipe. From turn 2 it opposes our next cast with
 * Counterspell, at that spell. From the end of turn 3 it answers our end-of-turn event with Atomize, at our permanents
 * in view order. From the end of turn 4 it opposes with Rout, which takes no target. In one
 * end step Atomize goes first and Rout follows it in the end-step chain, as the service's profile order does. A
 * not_cast outcome leaves its event armed, as the service's schedule does, so the next fire point opposes again. Each
 * card is cast once per game. The FIRST responder answers each respond decide with its first action.
 */
public final class InteractiveResponder implements Decider {

    /** The turn the counterspell unlocks on. */
    static final int COUNTERSPELL_TURN = 2;

    /** The turn the removal unlocks on. */
    static final int REMOVAL_TURN = 3;

    /** The turn the board wipe unlocks on. */
    static final int WIPE_TURN = 4;

    private final ScriptedResponder first = new ScriptedResponder(ScriptedResponder.Mode.FIRST);

    /** The disruption cards the opponent has cast. */
    private final Set<String> spent = new HashSet<>();

    /** The end-step cards proposed in the current end step, so each is proposed at most once per step. */
    private final Set<String> tried = new HashSet<>();

    @Override
    public Messages.Reply ask(Messages.InGame message) {
        if (!spent.contains("Counterspell") && message instanceof Messages.EventCast cast
                && cast.view().turn() >= COUNTERSPELL_TURN) {
            return Messages.Reply.ofOppose(cast.seq(), "Counterspell", List.of(cast.spell().id()));
        }
        if (message instanceof Messages.EventEndOfTurn end && !end.afterDisruption()) {
            tried.clear();
            Messages.Reply next = endStep(end.seq(), end.view());
            if (next != null) {
                return next;
            }
        }
        if (message instanceof Messages.Opposed opposed) {
            if (!"not_cast".equals(opposed.outcome())) {
                spent.add(opposed.card());
            }
            // Only an end-step card is in tried: its opposed event is inside the end-step sequence and takes oppose.
            if (tried.contains(opposed.card())) {
                Messages.Reply next = endStep(opposed.seq(), opposed.view());
                if (next != null) {
                    return next;
                }
            }
        }
        return first.ask(message);
    }

    /** The next end-step card in profile order that is due, armed and not tried this step; null when none is. */
    private Messages.Reply endStep(int seq, Messages.View view) {
        if (!spent.contains("Atomize") && !tried.contains("Atomize") && view.turn() >= REMOVAL_TURN
                && !view.battlefield().isEmpty()) {
            tried.add("Atomize");
            return Messages.Reply.ofOppose(seq, "Atomize", view.battlefield().stream().map(Messages.ViewPermanent::id).toList());
        }
        if (!spent.contains("Rout") && !tried.contains("Rout") && view.turn() >= WIPE_TURN) {
            tried.add("Rout");
            return Messages.Reply.ofOppose(seq, "Rout", List.of());
        }
        return null;
    }
}
