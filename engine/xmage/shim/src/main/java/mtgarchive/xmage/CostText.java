package mtgarchive.xmage;

import java.util.ArrayList;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Mana cost strings in Scryfall symbol form ("{2}{B}"). */
public final class CostText {

    private static final Pattern SYMBOL = Pattern.compile("\\{([^}]*)}");

    private CostText() {
    }

    /**
     * Adds the commander tax to a cost as generic mana: "{2}{B}" with tax 2 is "{4}{B}". With no
     * generic symbol, the tax goes after any X, Y or Z symbols and before the rest: "{X}{R}" with
     * tax 2 is "{X}{2}{R}".
     */
    public static String withTax(String cost, int tax) {
        if (tax <= 0) {
            return cost;
        }
        List<String> symbols = new ArrayList<>();
        Matcher m = SYMBOL.matcher(cost == null ? "" : cost);
        while (m.find()) {
            symbols.add(m.group(1));
        }
        int generic = tax;
        int at = -1;
        List<String> rest = new ArrayList<>();
        for (String s : symbols) {
            if (s.matches("\\d+")) {
                generic += Integer.parseInt(s);
                if (at < 0) {
                    at = rest.size();
                }
            } else {
                rest.add(s);
            }
        }
        if (at < 0) {
            at = 0;
            while (at < rest.size() && rest.get(at).matches("[XYZ]")) {
                at++;
            }
        }
        rest.add(at, Integer.toString(generic));
        StringBuilder out = new StringBuilder();
        for (String s : rest) {
            out.append('{').append(s).append('}');
        }
        return out.toString();
    }
}
