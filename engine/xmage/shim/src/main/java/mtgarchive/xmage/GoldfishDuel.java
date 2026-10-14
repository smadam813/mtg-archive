package mtgarchive.xmage;

import mage.constants.MultiplayerAttackOption;
import mage.constants.RangeOfInfluence;
import mage.game.CommanderDuel;
import mage.game.mulligan.Mulligan;

/**
 * A Commander duel under the game rules of one start_game (GLOSSARY.md, "Game rules"). XMage's
 * CommanderDuel always skips the starting player's turn-1 draw (CR 103.8a); here start_game decides.
 */
public class GoldfishDuel extends CommanderDuel {

    /**
     * @param turnOneDraw true when the starting player draws on turn 1 (CR 103.8c, a Commander game); false
     *                    on the play (CR 103.8a), and for a scenario, which sets the hand itself and must keep
     *                    the library as given
     */
    public GoldfishDuel(Mulligan mulligan, int startLife, int startHandSize, boolean turnOneDraw) {
        super(MultiplayerAttackOption.LEFT, RangeOfInfluence.ONE, mulligan, startLife, startHandSize);
        this.startingPlayerSkipsDraw = !turnOneDraw;
    }

    public GoldfishDuel(final GoldfishDuel game) {
        super(game);
    }

    @Override
    public GoldfishDuel copy() {
        return new GoldfishDuel(this);
    }
}
