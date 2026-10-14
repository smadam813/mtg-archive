package mtgarchive.xmage;

import mage.cards.repository.CardInfo;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * The scripted opponent's disruption cards by name. The shim resolves each name once per JVM (ADR 0013). The
 * service owns the card table; the shim only looks the names up. GameRunner resolves them before it sets the run seed: the lookup and the
 * one-time card creation draw object ids from the thread's unseeded stream, so they never shift a game's ids.
 */
final class OpponentCards {

    private static final Map<String, CardInfo> CARDS = new ConcurrentHashMap<>();

    private OpponentCards() {
    }

    /**
     * The card of each name, in order. An unknown name throws IllegalArgumentException, which ends the game as an
     * engine error; it is not cached, so a later game asks again.
     */
    static List<CardInfo> resolve(List<String> names) {
        if (names == null || names.isEmpty()) {
            throw new IllegalArgumentException("the scripted opponent needs at least one card");
        }
        List<CardInfo> cards = new ArrayList<>(names.size());
        for (String name : names) {
            cards.add(CARDS.computeIfAbsent(name, OpponentCards::find));
        }
        return List.copyOf(cards);
    }

    /**
     * Looks the card up and creates it once, so a card class that draws object ids in its static initializer does
     * so here, outside any seeded game (the same rule as DeckLibrary.resolve).
     */
    private static CardInfo find(String name) {
        CardInfo info = DeckLibrary.find(name);
        if (info == null) {
            throw new IllegalArgumentException("unknown opponent card '" + name + "'");
        }
        if (info.createCard() == null) {
            throw new IllegalArgumentException("XMage cannot create opponent card '" + name + "'");
        }
        return info;
    }
}
