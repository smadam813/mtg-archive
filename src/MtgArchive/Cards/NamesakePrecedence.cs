namespace MtgArchive.Cards;

/// <summary>
/// The one namesake rule for exact-name matching, in two steps. First, live
/// rows win over stale rows: a stale row can share its name with its live
/// replacement (Scryfall migrates oracle_ids). Second, non-token rows win
/// over token rows: the corpus keeps tokens, and a token can share its name
/// with a real card (Llanowar Elves). A row whose layout is null counts as
/// non-token, so a corpus that has not re-ingested since the layout column
/// arrived stays ambiguous. When no row survives a step, every row stands,
/// so collection rows and ban history always resolve to a card.
/// <c>GET /cards?name=</c>, the collection-import name resolver, the
/// curated-file lookup, and banlist replay all use this rule. Change the
/// precedence here only.
/// </summary>
public static class NamesakePrecedence
{
    private static readonly HashSet<string> TokenLayouts = ["token", "double_faced_token", "emblem"];

    public static List<T> Candidates<T>(List<T> matches, Func<T, bool> stale, Func<T, string?> layout)
    {
        var live = PreferOrAll(matches, m => !stale(m));
        return PreferOrAll(live, m => layout(m) is not { } l || !TokenLayouts.Contains(l));
    }

    private static List<T> PreferOrAll<T>(List<T> matches, Func<T, bool> preferred)
    {
        var kept = matches.Where(preferred).ToList();
        return kept.Count > 0 ? kept : matches;
    }
}
