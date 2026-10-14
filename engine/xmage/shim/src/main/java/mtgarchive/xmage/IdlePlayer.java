package mtgarchive.xmage;

import mage.constants.RangeOfInfluence;
import mage.game.Game;
import mage.player.ai.ComputerPlayer;

/**
 * Do-nothing opponent (spike: IdlePlayer). ComputerPlayer already passes every priority and declares
 * no attackers or blockers; this seat also keeps its opening hand, and it never loses, so a game
 * ends only at the horizon, on a stop, on our loss or on an engine error.
 */
public class IdlePlayer extends ComputerPlayer {

    /** The seat's name; the scripted opponent keeps it (ADR 0013). */
    static final String NAME = "Idle";

    public IdlePlayer(String name) {
        super(name, RangeOfInfluence.ALL);
    }

    public IdlePlayer(final IdlePlayer player) {
        super(player);
    }

    @Override
    public IdlePlayer copy() {
        return new IdlePlayer(this);
    }

    @Override
    public boolean chooseMulligan(Game game) {
        return false;
    }

    @Override
    public boolean priority(Game game) {
        pass(game);
        return false;
    }

    @Override
    public boolean canLose(Game game) {
        return hasLeft();
    }
}
