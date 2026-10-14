package mtgarchive.xmage;

import mage.cards.decks.Deck;
import mage.cards.repository.CardInfo;
import mage.constants.PhaseStep;
import mage.game.Game;
import mage.game.GameOptions;
import mage.game.events.TableEvent;
import mage.game.mulligan.LondonMulligan;
import mage.players.Player;
import mage.util.RandomUtil;

import java.security.SecureRandom;
import java.util.List;

/** Plays one game for one start_game (game set-up taken from the spike's Main.playOne). */
public final class GameRunner {

    static final int HAND_SIZE = 7;

    /**
     * Stack size for a game thread: XMage recurses deeply, and the spike ran every game thread with
     * 64 MB. A game thread also needs a name that starts with "GAME" (XMage ThreadUtils).
     */
    static final long GAME_THREAD_STACK = 64L * 1024 * 1024;

    /** Reseeds a thread after its game (see {@link #play(GameCtx)}). */
    private static final SecureRandom RESEED = new SecureRandom();

    private GameRunner() {
    }

    /**
     * Plays the game on the calling thread and returns its game_over. Never throws: an XMage
     * exception, a decision failure or an unsupported option ends the game with engine_error.
     */
    public static Messages.GameOver play(Messages.StartGame start, LoadedDeck deck, Decider decider) {
        return play(new GameCtx(start, deck, decider));
    }

    /**
     * Plays the game of this context. Afterwards the thread's RandomUtil gets a random seed, so
     * object ids that the thread draws outside a game (a static initializer, a load_deck) do not
     * continue this game's seeded stream.
     */
    static Messages.GameOver play(GameCtx ctx) {
        try {
            return playSeeded(ctx);
        } finally {
            reseed();
        }
    }

    /**
     * Gives the thread's RandomUtil a random seed after a game or a scenario, so object ids that the
     * thread draws outside a game do not continue that game's seeded stream.
     */
    static void reseed() {
        RandomUtil.setSeed(RESEED.nextLong());
    }

    private static Messages.GameOver playSeeded(GameCtx ctx) {
        Messages.StartGame start = ctx.start;
        LoadedDeck deck = ctx.deck;
        if (start.horizon() < 1) {
            ctx.fail(null, "horizon must be at least 1, got " + start.horizon());
            return ctx.gameOver(null);
        }
        if (start.startLife() < 1) {
            ctx.fail(null, "start_life must be at least 1, got " + start.startLife());
            return ctx.gameOver(null);
        }
        List<CardInfo> opponentCards;
        try {
            // Before the seed: the card lookups and their one-time card creation draw no seeded id.
            opponentCards = start.opponent() == null ? null : OpponentCards.resolve(start.opponent().cards());
        } catch (RuntimeException e) {
            ctx.fail(null, GameCtx.firstLine(e, null));
            return ctx.gameOver(null);
        }
        Game game = null;
        try {
            // Seed before the game, the players and the cards exist: each draws object ids.
            RandomUtil.setSeed(start.seed());
            game = newGame(start);
            PilotPlayer me = new PilotPlayer("Pilot", ctx);
            ctx.seatId = me.getId();
            addPlayer(game, me, withBlanks(Deck.load(deck.lists(), true, false, DeckLibrary.CARD_CACHE), deck));
            addPlayer(game, opponent(opponentCards),
                    Deck.load(DeckLibrary.idle().lists(), true, false, DeckLibrary.CARD_CACHE));
            GameOptions options = new GameOptions();
            // The untap step after our last turn when no one takes an extra turn. It counts every
            // turn, so it is only a backstop: GameCtx.onPriority ends the game after our last turn.
            options.stopOnTurn = (start.horizon() - 1) * 2 + 2;
            options.stopAtStep = PhaseStep.UNTAP;
            options.rollbackTurnsAllowed = false;
            game.setGameOptions(options);
            final Game g = game;
            game.addTableEventListener(event -> {
                if (event.getEventType() == TableEvent.EventType.INFO) {
                    ctx.info(g, event.getMessage());
                } else if (event.getEventType() == TableEvent.EventType.ERROR) {
                    ctx.fail(g, GameCtx.firstLine(event.getException(), event.getMessage()));
                }
            });
            ctx.line(null, "SEED " + start.seed());
            game.setStartingPlayerId(me.getId());
            game.start(me.getId());
            me.settleAtGameOver(game);
        } catch (Throwable e) {
            ctx.fail(null, GameCtx.firstLine(e, null));
        }
        return ctx.gameOver(game);
    }

    /**
     * The opponent seat: the idle opponent for null, else the scripted opponent with those cards. Both keep the
     * idle opponent's name and draw the same object ids, so the seat alone never moves a game.
     */
    static IdlePlayer opponent(List<CardInfo> cards) {
        return cards == null ? new IdlePlayer(IdlePlayer.NAME) : new ScriptedOpponent(IdlePlayer.NAME, cards);
    }

    /** The duel of a start_game, under the game rules the start_game carries. It draws no object id. */
    static GoldfishDuel newGame(Messages.StartGame start) {
        GoldfishDuel game = new GoldfishDuel(new LondonMulligan(start.freeMulligans()), start.startLife(), HAND_SIZE,
                start.startingPlayerDraws());
        game.setCheckCommanderDamage(start.commanderDamage());
        return game;
    }

    static void addPlayer(Game game, Player player, Deck deck) {
        player.init(game);
        game.loadCards(deck.getCards(), player.getId());
        game.loadCards(deck.getSideboard(), player.getId());
        game.addPlayer(player, deck);
    }

    /**
     * Adds one blank card per copy of each card XMage lacks, in decklist-name order, so the library
     * keeps the decklist's size. The game is seeded already, so their object ids are seeded too.
     */
    static Deck withBlanks(Deck built, LoadedDeck deck) {
        for (Messages.DeckEntry blank : deck.blanks()) {
            for (int i = 0; i < blank.quantity(); i++) {
                built.getCards().add(new BlankCard(null, blank.name()));
            }
        }
        return built;
    }
}
