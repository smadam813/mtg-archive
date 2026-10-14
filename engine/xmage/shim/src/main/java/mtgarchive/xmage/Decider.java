package mtgarchive.xmage;

/** The service's side of one game: it answers each decide and event with one reply. */
public interface Decider {

    /**
     * Sends the message and returns the reply with the same seq.
     *
     * @throws DecisionFailure when no valid reply arrives (timeout, closed connection, wrong seq)
     */
    Messages.Reply ask(Messages.InGame message);
}
