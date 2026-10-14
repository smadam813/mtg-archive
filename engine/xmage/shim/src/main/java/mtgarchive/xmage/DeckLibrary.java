package mtgarchive.xmage;

import mage.cards.decks.Deck;
import mage.cards.decks.DeckCardInfo;
import mage.cards.decks.DeckCardLists;
import mage.cards.repository.CardInfo;
import mage.cards.repository.CardRepository;
import mage.game.GameException;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;
import java.util.concurrent.ConcurrentHashMap;

/** Resolves load_deck messages against XMage's card database and caches them by deck_id. */
public final class DeckLibrary {

    /** XMage's card-info cache, shared by every deck load. */
    static final Map<String, CardInfo> CARD_CACHE = new ConcurrentHashMap<>();

    /** The idle opponent: a vanilla legendary 2/2 and 99 Plains (the spike's deck/idle.txt). */
    static final Messages.LoadDeck IDLE_DECK = new Messages.LoadDeck("load_deck",
            List.of("Isamaru, Hound of Konda"), List.of(new Messages.DeckEntry("Plains", 99)));

    private static volatile LoadedDeck idle;

    private final Map<String, LoadedDeck> decks = new ConcurrentHashMap<>();

    /** Resolves the deck once per deck_id; a later load of the same deck returns the cached one. */
    public LoadedDeck load(Messages.LoadDeck request) {
        Messages.LoadDeck canonical = DeckId.canonical(request);
        return decks.computeIfAbsent(DeckId.of(canonical), id -> resolve(id, canonical));
    }

    /** The deck a load_deck produced, or null. */
    public LoadedDeck get(String deckId) {
        return decks.get(deckId);
    }

    static LoadedDeck idle() {
        LoadedDeck deck = idle;
        if (deck == null) {
            synchronized (DeckLibrary.class) {
                if (idle == null) {
                    idle = resolve(DeckId.of(IDLE_DECK), DeckId.canonical(IDLE_DECK));
                }
                deck = idle;
            }
        }
        return deck;
    }

    /**
     * Builds the deck from the canonical (sorted) message, so the entry order of a request never
     * changes a game. Then creates the deck's cards once, before any game that uses them: a card
     * class that draws object ids in its static initializer does so here, not inside a seeded game.
     * These ids come from the calling thread's RandomUtil, which is unseeded or carries a random
     * seed (GameRunner reseeds a thread after each game), so they never shift a game's ids.
     */
    static LoadedDeck resolve(String deckId, Messages.LoadDeck canonical) {
        List<String> unimplemented = new ArrayList<>();
        List<Messages.DeckEntry> blanks = new ArrayList<>();
        Map<String, String> names = new TreeMap<>();
        List<DeckCardInfo> sideboard = new ArrayList<>();
        for (String name : canonical.commanders()) {
            CardInfo info = find(name);
            if (info == null) {
                unimplemented.add(name);
                continue;
            }
            sideboard.add(new DeckCardInfo(info.getName(), info.getCardNumber(), info.getSetCode(), 1));
            names.put(info.getName(), name);
        }
        List<DeckCardInfo> main = new ArrayList<>();
        for (Messages.DeckEntry entry : canonical.cards()) {
            CardInfo info = find(entry.name());
            if (info == null) {
                unimplemented.add(entry.name());
                blanks.add(entry);
                continue;
            }
            main.add(new DeckCardInfo(info.getName(), info.getCardNumber(), info.getSetCode(), entry.quantity()));
            names.put(info.getName(), entry.name());
        }
        DeckCardLists lists = new DeckCardLists();
        lists.setCards(main);
        lists.setSideboard(sideboard);
        unimplemented.sort(Comparator.naturalOrder());
        LoadedDeck deck = new LoadedDeck(deckId, lists, List.copyOf(unimplemented), Collections.unmodifiableMap(names),
                List.copyOf(blanks));
        try {
            Deck.load(deck.lists(), true, false, CARD_CACHE);
        } catch (GameException e) {
            throw new IllegalStateException("XMage cannot build deck " + deckId, e);
        }
        return deck;
    }

    /**
     * The first printing by set code and card number, or null when XMage has no card of that name.
     * findCards takes the full name of a multi-face card ("A // B") as well as a front-face name.
     */
    static CardInfo find(String name) {
        List<CardInfo> infos = new ArrayList<>(CardRepository.instance.findCards(name));
        if (infos.isEmpty()) {
            return null;
        }
        infos.sort(Comparator.comparing(CardInfo::getSetCode).thenComparing(CardInfo::getCardNumber));
        return infos.get(0);
    }
}
