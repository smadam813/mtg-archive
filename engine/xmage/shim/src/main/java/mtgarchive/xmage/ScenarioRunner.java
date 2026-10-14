package mtgarchive.xmage;

import mage.abilities.ActivatedAbility;
import mage.abilities.effects.common.ChooseACardNameEffect;
import mage.cards.Card;
import mage.cards.decks.Deck;
import mage.cards.decks.DeckCardLists;
import mage.cards.repository.CardInfo;
import mage.constants.PhaseStep;
import mage.constants.Zone;
import mage.game.Game;
import mage.game.GameOptions;
import mage.game.events.GameEvent;
import mage.game.events.TableEvent;
import mage.game.permanent.Permanent;
import mage.util.RandomUtil;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

/**
 * Scenario mode (spec §6.2): one card-correctness fixture on a real XMage game. Our seat gets the
 * start zones on turn 1 and draws no card; the script then runs in our first main phase (see
 * {@link ScenarioScript}). Built from XMage's own test set-up (GameImpl.cheat in test mode).
 */
final class ScenarioRunner {

    private ScenarioRunner() {
    }

    /**
     * A scenario's fixed game rules: 40 life, commander damage, no turn-1 draw (the scenario sets the
     * hand and must keep the library as given), no free mulligan. A one-turn game with the full log.
     */
    static Messages.StartGame start(String deckId, long seed) {
        return new Messages.StartGame("start_game", deckId, seed, 1, 40, true, false, 0, false, true);
    }

    /** Plays the scenario on the calling GAME thread. Never throws: a failure is the result's error. */
    static Messages.ScenarioResult run(Messages.Scenario scenario, DeckLibrary decks) {
        try {
            return runSeeded(scenario, decks);
        } catch (Throwable e) {
            return error(GameCtx.firstLine(e, null), List.of(), List.of());
        } finally {
            GameRunner.reseed();
        }
    }

    private static Messages.ScenarioResult runSeeded(Messages.Scenario scenario, DeckLibrary decks) {
        String invalid = invalidStep(scenario.actions());
        if (invalid != null) {
            return error(invalid, List.of(), List.of());
        }
        LoadedDeck deck = decks.load(new Messages.LoadDeck("load_deck",
                scenario.commanders() == null ? List.of() : scenario.commanders(), scenario.cards()));
        if (!deck.unimplemented().isEmpty()) {
            return error("XMage lacks " + String.join(", ", deck.unimplemented()), List.of(), List.of());
        }
        ScenarioScript script = new ScenarioScript(scenario.actions(), scenario.start());
        GameCtx ctx = new GameCtx(start(deck.deckId(), scenario.seed()), deck, script);
        script.ctx = ctx;
        ctx.scenario = script;
        try {
            // Seed before the game, the players and the cards exist: each draws object ids.
            RandomUtil.setSeed(scenario.seed());
            Game game = GameRunner.newGame(ctx.start);
            PilotPlayer me = new PilotPlayer("Pilot", ctx);
            ctx.seatId = me.getId();
            GameRunner.addPlayer(game, me, Deck.load(commandersOnly(deck), true, false, DeckLibrary.CARD_CACHE));
            GameRunner.addPlayer(game, new IdlePlayer("Idle"),
                    Deck.load(DeckLibrary.idle().lists(), true, false, DeckLibrary.CARD_CACHE));
            GameOptions options = new GameOptions();
            options.testMode = true;          // no opening hand is drawn: the scenario sets the hand
            options.skipInitShuffling = true; // the scenario sets the library order
            options.stopOnTurn = 2;
            options.stopAtStep = PhaseStep.UNTAP;
            options.rollbackTurnsAllowed = false;
            game.setGameOptions(options);
            game.addTableEventListener(event -> {
                if (event.getEventType() == TableEvent.EventType.INFO) {
                    ctx.info(game, event.getMessage());
                } else if (event.getEventType() == TableEvent.EventType.ERROR) {
                    ctx.fail(game, GameCtx.firstLine(event.getException(), event.getMessage()));
                }
            });
            // The zones are set up at our own first priority (ScenarioScript.setUp), not here: a
            // permanent cheated onto the battlefield before game.start() sees turn 1's own untap
            // step and would be untapped by it.
            ctx.line(null, "SEED " + scenario.seed());
            game.setStartingPlayerId(me.getId());
            game.start(me.getId());
        } catch (Throwable e) {
            ctx.fail(null, GameCtx.firstLine(e, null));
        }
        if (script.result != null && ctx.error() == null) {
            return script.result;
        }
        return error(ctx.error() != null ? ctx.error() : "the scenario ended before its last step", script.picks, ctx.log());
    }

    /**
     * The first step that lacks a field its type needs, or has an unknown type, as an error message;
     * null when every step is whole. A step is checked here so it never fails as a null in the game.
     */
    static String invalidStep(List<Messages.ScenarioAction> actions) {
        for (int i = 0; actions != null && i < actions.size(); i++) {
            Messages.ScenarioAction a = actions.get(i);
            String type = a == null ? null : a.type();
            String need = type == null ? "a type"
                    : switch (type) {
                        case "cast" -> a.card() == null ? "a card" : null;
                        case "activate" -> a.permanent() == null || a.ability() == null ? "a permanent and an ability" : null;
                        case "choose" -> a.pick() == null ? "a pick" : null;
                        default -> "a known type (cast, activate or choose), not " + type;
                    };
            if (need != null) {
                return "step " + (i + 1) + " needs " + need;
            }
        }
        return null;
    }

    /**
     * Checks that each start permanent with a chosen name has it, as ChooseACardNameEffect stores it
     * (a game value keyed by the permanent's id). The name comes from the card's own "as it enters,
     * name a card" pick during cheat, which ScenarioScript answers from the start names in order. A
     * name on a card with no such pick, or a name the pick did not take, is an error, never a value
     * set behind XMage's back.
     */
    static void checkStartNames(Game game, LoadedDeck deck, List<Messages.StartPermanent> start, List<Card> cards) {
        for (int i = 0; i < start.size(); i++) {
            String named = start.get(i).named();
            if (named == null) {
                continue;
            }
            String xmage = deck.xmageName(named);
            if (xmage == null) {
                throw new IllegalArgumentException("the name " + named + " is not in the scenario's cards");
            }
            Object value = game.getState().getValue(cards.get(i).getId().toString() + ChooseACardNameEffect.INFO_KEY);
            if (!xmage.equals(value)) {
                throw new IllegalArgumentException("start permanent " + start.get(i).card() + " should name " + named
                        + ", but it names " + (value == null ? "nothing (it asks for no name as it enters)" : value));
            }
        }
    }

    /** The state at the end of the script (see {@link Messages.ScenarioResult}). */
    static Messages.ScenarioResult result(Game game, PilotPlayer me, GameCtx ctx, ScenarioScript script) {
        List<Messages.NamedValue> named = new ArrayList<>();
        List<Messages.AbilityState> abilities = new ArrayList<>();
        for (Permanent p : game.getBattlefield().getAllActivePermanents(me.getId())) {
            String id = "p:" + p.getId();
            String card = ctx.names.card(p);
            Object value = game.getState().getValue(p.getId().toString() + ChooseACardNameEffect.INFO_KEY);
            if (value instanceof String name) {
                named.add(new Messages.NamedValue(id, card, ctx.deck.decklistName(name)));
            }
            int index = 0;
            for (ActivatedAbility a : p.getAbilities().getActivatedAbilities(Zone.BATTLEFIELD)) {
                abilities.add(new Messages.AbilityState(id, card, index++, a.isManaActivatedAbility(), locked(game, me, a),
                        a.canActivate(me.getId(), game).canActivate()));
            }
        }
        named.sort(Comparator.comparing(Messages.NamedValue::card).thenComparing(Messages.NamedValue::permanentId));
        abilities.sort(Comparator.comparing(Messages.AbilityState::card).thenComparing(Messages.AbilityState::permanentId)
                .thenComparingInt(Messages.AbilityState::index));
        return new Messages.ScenarioResult("scenario_result", null, ViewBuilder.forEvent(game, me, ctx),
                ctx.names.cards(me.getLibrary().getCards(game)), named, abilities, List.copyOf(script.picks), ctx.log());
    }

    /**
     * True when a rule-modifying effect forbids the activation, whatever its costs and timing. This is
     * the check ActivatedAbilityImpl.canActivate makes last (the ACTIVATE_ABILITY event), on its own.
     */
    private static boolean locked(Game game, PilotPlayer me, ActivatedAbility a) {
        return game.getContinuousEffects().preventedByRuleModification(
                GameEvent.getEvent(GameEvent.EventType.ACTIVATE_ABILITY, a.getId(), a, me.getId()), a, game, true);
    }

    static Messages.ScenarioResult error(String message, List<Messages.PickRecord> picks, List<Messages.LogEntry> log) {
        return new Messages.ScenarioResult("scenario_result", message, null, null, null, null, List.copyOf(picks), List.copyOf(log));
    }

    /** Our deck lists with no card in the 99: the scenario puts every card in place itself. */
    private static DeckCardLists commandersOnly(LoadedDeck deck) {
        DeckCardLists lists = new DeckCardLists();
        lists.setCards(new ArrayList<>());
        lists.setSideboard(deck.lists().getSideboard());
        return lists;
    }

    static List<Card> cards(List<String> names) {
        List<Card> out = new ArrayList<>();
        for (String name : names == null ? List.<String>of() : names) {
            out.add(card(name));
        }
        return out;
    }

    /** A new card object for a decklist name (DeckLibrary.find: the first printing). */
    static Card card(String name) {
        CardInfo info = DeckLibrary.find(name);
        Card card = info == null ? null : info.createCard();
        if (card == null) {
            throw new IllegalArgumentException("XMage has no card " + name);
        }
        return card;
    }
}
