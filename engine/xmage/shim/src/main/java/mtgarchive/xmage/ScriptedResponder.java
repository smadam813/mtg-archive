package mtgarchive.xmage;

import java.util.List;

/**
 * A service stand-in inside the JVM, for tests and the determinism self-test. It keeps every hand,
 * bottoms and discards the first cards in hand order, takes the first option of every pick and
 * continues after every event. PASS passes every action and every response; FIRST takes the first listed action,
 * and answers each respond decide with its first action.
 */
public final class ScriptedResponder implements Decider {

    public enum Mode { PASS, FIRST }

    private final Mode mode;

    public ScriptedResponder(Mode mode) {
        this.mode = mode;
    }

    @Override
    public Messages.Reply ask(Messages.InGame message) {
        if (message instanceof Messages.DecideKeep) {
            return Messages.Reply.ofKeep(message.seq(), true);
        }
        if (message instanceof Messages.DecideCount) {
            Messages.DecideCount d = (Messages.DecideCount) message;
            return Messages.Reply.ofCards(d.seq(), List.copyOf(d.view().hand().subList(0, d.count())));
        }
        if (message instanceof Messages.DecidePick) {
            Messages.DecidePick d = (Messages.DecidePick) message;
            return Messages.Reply.ofOption(d.seq(), d.pick().options().get(0).id());
        }
        if (message instanceof Messages.DecideAction) {
            Messages.DecideAction d = (Messages.DecideAction) message;
            String first = d.view().actions().isEmpty() ? null : d.view().actions().get(0).id();
            return Messages.Reply.ofAction(d.seq(), mode == Mode.FIRST ? first : null);
        }
        if (message instanceof Messages.DecideRespond d) {
            String first = d.view().actions().isEmpty() ? null : d.view().actions().get(0).id();
            return Messages.Reply.ofAction(d.seq(), mode == Mode.FIRST ? first : null);
        }
        return Messages.Reply.ofDo(message.seq(), "continue");
    }
}
