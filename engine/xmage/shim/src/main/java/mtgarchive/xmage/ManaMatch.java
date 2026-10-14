package mtgarchive.xmage;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Comparator;
import java.util.List;
import java.util.Set;
import java.util.TreeSet;
import mage.Mana;
import mage.abilities.costs.mana.ColoredManaCost;
import mage.abilities.costs.mana.ColorlessManaCost;
import mage.abilities.costs.mana.GenericManaCost;
import mage.abilities.costs.mana.ManaCost;

/**
 * Which source pays one cost part, so that every other colored pip still has a source of its
 * own: a maximum bipartite matching of pips to sources (roadmap item 22, phase 3E). XMage's
 * ComputerPlayer taps the first compatible producer, so it can spend the only blue source on
 * {B}. The retired engine's GreedyPayment matched the same way.
 */
final class ManaMatch {
    /**
     * One untapped permanent with a tap mana ability that costs no mana, or a card in hand with a
     * mana ability that works from hand (Simian Spirit Guide), and the symbols one activation can make.
     */
    record Source(String id, Set<String> colors, boolean hand) {
        Source(String id, Set<String> colors) {
            this(id, colors, false);
        }
    }

    private ManaMatch() {
    }

    /**
     * The id of the source to tap for {@code part} ("W", "U", "B", "R", "G", "C", or "1" for one
     * generic), such that {@code otherPips} still match distinct other sources; null when no
     * source does. Sources with fewer colors go first, then by id, so the choice is stable. A card
     * in hand counts only when the permanents alone have no plan, and then after every permanent:
     * the activation exiles the card (the retired engine's GreedyPayment.Solve did the same).
     */
    static String pick(String part, List<String> otherPips, List<Source> sources) {
        List<Source> board = sources.stream().filter(s -> !s.hand()).toList();
        String id = pickFrom(part, otherPips, board);
        return id != null || board.size() == sources.size() ? id : pickFrom(part, otherPips, sources);
    }

    private static String pickFrom(String part, List<String> otherPips, List<Source> sources) {
        List<Source> order = new ArrayList<>(sources);
        order.sort(Comparator.comparing(Source::hand)
                .thenComparingInt((Source s) -> s.colors().size()).thenComparing(Source::id));
        boolean generic = part.equals("1");
        for (Source s : order) {
            if (!generic && !s.colors().contains(part)) {
                continue;
            }
            List<Source> rest = new ArrayList<>(order);
            rest.remove(s);
            if (covers(otherPips, rest)) {
                return s.id();
            }
        }
        return null;
    }

    /** Each pip matches a distinct source (augmenting paths). */
    static boolean covers(List<String> pips, List<Source> sources) {
        int[] owner = new int[sources.size()];
        Arrays.fill(owner, -1);
        for (int p = 0; p < pips.size(); p++) {
            if (!augment(p, pips, sources, owner, new boolean[sources.size()])) {
                return false;
            }
        }
        return true;
    }

    private static boolean augment(int p, List<String> pips, List<Source> sources, int[] owner, boolean[] seen) {
        for (int s = 0; s < sources.size(); s++) {
            if (seen[s] || !sources.get(s).colors().contains(pips.get(p))) {
                continue;
            }
            seen[s] = true;
            if (owner[s] < 0 || augment(owner[s], pips, sources, owner, seen)) {
                owner[s] = p;
                return true;
            }
        }
        return false;
    }

    /** The plain symbol of a cost part, or null for a hybrid, Phyrexian, X or snow part (XMage pays those). */
    static String symbol(ManaCost cost) {
        if (cost instanceof ColoredManaCost) {
            // A Phyrexian part reads "U/P": not a plain symbol.
            String text = cost.getText().replace("{", "").replace("}", "");
            return text.length() == 1 ? text : null;
        }
        if (cost instanceof GenericManaCost) {
            return "1";
        }
        if (cost instanceof ColorlessManaCost) {
            return "C";
        }
        return null;
    }

    /** The symbols one net mana can be: each color it holds, C for colorless, and all five for any. */
    static Set<String> colors(Mana mana) {
        Set<String> out = new TreeSet<>();
        if (mana.getAny() > 0) {
            out.addAll(List.of("W", "U", "B", "R", "G"));
        }
        if (mana.getWhite() > 0) {
            out.add("W");
        }
        if (mana.getBlue() > 0) {
            out.add("U");
        }
        if (mana.getBlack() > 0) {
            out.add("B");
        }
        if (mana.getRed() > 0) {
            out.add("R");
        }
        if (mana.getGreen() > 0) {
            out.add("G");
        }
        if (mana.getColorless() > 0) {
            out.add("C");
        }
        return out;
    }
}
