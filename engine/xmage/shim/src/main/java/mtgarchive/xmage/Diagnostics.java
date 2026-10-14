package mtgarchive.xmage;

/**
 * Diagnostic switches for the Obeka turn-4 diagnosis. Each one is a system property that
 * XMAGE_SWITCHES sets; both are off by default, so a normal game does not change.
 */
final class Diagnostics {
    /** Logs "ETB <card> tapped|untapped" the first time the shim sees each of our lands. */
    static final String ETB = "mtgarchive.diag.etb";

    /** Answers yes to "pay 2 life" questions and logs "DIAG pay-life <card>". */
    static final String PAY_LIFE = "mtgarchive.diag.payLife";

    private Diagnostics() {
    }

    static boolean etb() {
        return Boolean.getBoolean(ETB);
    }

    static boolean payLife() {
        return Boolean.getBoolean(PAY_LIFE);
    }

    static boolean isPayLifeQuestion(String message) {
        return message != null && message.toLowerCase(java.util.Locale.ROOT).contains("pay 2 life");
    }
}
