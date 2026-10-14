package mtgarchive.xmage;

/** A decide or event with no valid reply. The game ends with reason engine_error and this message. */
public final class DecisionFailure extends RuntimeException {

    public DecisionFailure(String message) {
        super(message);
    }
}
