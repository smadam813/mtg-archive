package mtgarchive.xmage;

import mage.abilities.ActivatedAbility;
import mage.cards.decks.Deck;
import mage.cards.repository.CardInfo;
import mage.constants.PhaseStep;
import mage.game.Game;
import mage.game.GameOptions;
import mage.game.events.TableEvent;
import mage.util.RandomUtil;
import mage.util.ThreadUtils;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.Predicate;

/**
 * A test bench for the upstream XMage behaviors interactive disruption builds on: one real game against
 * the scripted opponent, as GameRunner plays it, with hooks that run inside our seat's priority and see the game.
 * A hook runs once, at the first real priority of our seat where its condition holds; the pilot then plays on.
 */
final class Lab {

    /** A hook's view of the game: the game, our seat, the scripted opponent and the context. */
    record Seat(Game game, PilotPlayer me, ScriptedOpponent opponent, GameCtx ctx) {
    }

    /** One hook: a condition and an action. */
    record Hook(Predicate<Seat> when, java.util.function.Consumer<Seat> then) {
    }

    /**
     * Refuses the first activation of our seat that the predicate matches, or every one with {@link #every}:
     * activateAbility returns false at once, as a failed payment does, so the shim restores the game
     * (the stale-spell regression).
     */
    record Refusal(Predicate<ActivatedAbility> when, boolean[] done, boolean every) {

        Refusal(Predicate<ActivatedAbility> when) {
            this(when, new boolean[1], false);
        }

        /** Refuses every activation the predicate matches, not only the first. */
        static Refusal every(Predicate<ActivatedAbility> when) {
            return new Refusal(when, new boolean[1], true);
        }

        boolean refuses(ActivatedAbility ability) {
            if ((done[0] && !every) || !when.test(ability)) {
                return false;
            }
            done[0] = true;
            return true;
        }
    }

    private Lab() {
    }

    /** Our seat with the hooks: each hook runs once, before the pilot's own priority. */
    static final class LabPilot extends PilotPlayer {

        private final transient List<Hook> hooks;
        private final transient GameCtx labCtx;
        private final transient Refusal refusal;

        LabPilot(GameCtx ctx, List<Hook> hooks, Refusal refusal) {
            super("Pilot", ctx);
            this.labCtx = ctx;
            this.hooks = hooks;
            this.refusal = refusal;
        }

        LabPilot(final LabPilot player) {
            super(player);
            this.labCtx = player.labCtx;
            this.hooks = player.hooks;
            this.refusal = player.refusal;
        }

        @Override
        public boolean activateAbility(ActivatedAbility ability, Game game) {
            if (refusal != null && !game.isSimulation() && refusal.refuses(ability)) {
                return false;
            }
            return super.activateAbility(ability, game);
        }

        @Override
        public LabPilot copy() {
            return new LabPilot(this);
        }

        @Override
        public boolean priority(Game game) {
            if (!game.isSimulation() && !labCtx.done()) {
                Seat seat = new Seat(game, this, opponentOf(game), labCtx);
                for (Hook hook : new ArrayList<>(hooks)) {
                    if (hook.when().test(seat)) {
                        hooks.remove(hook);
                        hook.then().accept(seat);
                    }
                }
            }
            return super.priority(game);
        }

        private ScriptedOpponent opponentOf(Game game) {
            for (java.util.UUID id : game.getOpponents(getId())) {
                if (game.getPlayer(id) instanceof ScriptedOpponent o) {
                    return o;
                }
            }
            return null;
        }
    }

    /** Plays one game as GameRunner does, with our seat's hooks, on a GAME thread. */
    static Messages.GameOver play(Messages.StartGame start, LoadedDeck deck, Decider decider, Hook... hooks) {
        return play(start, deck, decider, null, hooks);
    }

    /** Plays one game as GameRunner does, with our seat's hooks and a refusal of one of its activations. */
    static Messages.GameOver play(Messages.StartGame start, LoadedDeck deck, Decider decider, Refusal refusal, Hook... hooks) {
        GameCtx ctx = new GameCtx(start, deck, decider);
        AtomicReference<Messages.GameOver> over = new AtomicReference<>();
        Thread thread = new Thread(null, () -> {
            try {
                over.set(playSeeded(ctx, List.of(hooks), refusal));
            } finally {
                GameRunner.reseed();
            }
        }, ThreadUtils.THREAD_PREFIX_GAME + "-lab", GameRunner.GAME_THREAD_STACK);
        thread.start();
        try {
            thread.join();
        } catch (InterruptedException e) {
            throw new IllegalStateException(e);
        }
        return over.get();
    }

    private static Messages.GameOver playSeeded(GameCtx ctx, List<Hook> hooks, Refusal refusal) {
        Messages.StartGame start = ctx.start;
        List<CardInfo> opponentCards = start.opponent() == null ? null : OpponentCards.resolve(start.opponent().cards());
        Game game = null;
        try {
            RandomUtil.setSeed(start.seed());
            game = GameRunner.newGame(start);
            LabPilot me = new LabPilot(ctx, new ArrayList<>(hooks), refusal);
            ctx.seatId = me.getId();
            GameRunner.addPlayer(game, me, GameRunner.withBlanks(Deck.load(ctx.deck.lists(), true, false, DeckLibrary.CARD_CACHE), ctx.deck));
            GameRunner.addPlayer(game, GameRunner.opponent(opponentCards),
                    Deck.load(DeckLibrary.idle().lists(), true, false, DeckLibrary.CARD_CACHE));
            GameOptions options = new GameOptions();
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
}
