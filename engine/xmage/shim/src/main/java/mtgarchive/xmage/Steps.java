package mtgarchive.xmage;

import mage.constants.PhaseStep;
import mage.constants.Zone;

/** XMage steps and zones as the wire names them. */
public final class Steps {

    private Steps() {
    }

    /**
     * The view step for an XMage step: the pilot view's TurnStep.
     * Combat and the second main phase read as main.
     */
    public static String of(PhaseStep step) {
        if (step == null) {
            return "mulligan";
        }
        switch (step) {
            case UNTAP:
                return "untap";
            case UPKEEP:
                return "upkeep";
            case DRAW:
                return "draw";
            case END_TURN:
                return "end";
            case CLEANUP:
                return "cleanup";
            default:
                return "main";
        }
    }

    /** The steps in which our seat gets action decisions: the upkeep, main and end-step windows. */
    public static boolean isActionWindow(PhaseStep step) {
        return step == PhaseStep.UPKEEP || step == PhaseStep.PRECOMBAT_MAIN || step == PhaseStep.END_TURN;
    }

    /** hand, command, battlefield, graveyard, exile, library, stack; outside for a missing zone. */
    public static String zone(Zone zone) {
        if (zone == null) {
            return "outside";
        }
        switch (zone) {
            case EXILED:
                return "exile";
            case COMMAND:
                return "command_zone";
            default:
                return zone.name().toLowerCase(java.util.Locale.ROOT);
        }
    }
}
