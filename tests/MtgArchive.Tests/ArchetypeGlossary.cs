using System.Text.RegularExpressions;

namespace MtgArchive.Tests;

/// <summary>
/// The plugin's archetype glossary, read from its one source
/// file <c>plugin/mtg-archive/references/archetype-glossary.md</c>. The
/// eval's glossary pass applies the file's own rule: pull by each mapped
/// tag and type in the popularity order, then search by the phrasing,
/// pulled cards first. This class copies nothing from the file. A term,
/// tag, or phrasing change there is the change the eval measures.
/// </summary>
public static class ArchetypeGlossary
{
    /// <summary>The page size of each structured pull and the k of the semantic call, per the file.</summary>
    public const int PullLimit = 60;

    public sealed record Entry(
        string Term, string Definition, IReadOnlyList<string> Tags, IReadOnlyList<string> Types, string Phrasing);

    /// <summary>One query's expansion: the entry that matched and the query
    /// with the term replaced by the phrasing.</summary>
    public sealed record Expansion(Entry Entry, string Query);

    public static string FilePath()
        => Path.Combine(RepoRoot.Find(), "plugin", "mtg-archive", "references", "archetype-glossary.md");

    public static IReadOnlyList<Entry> Load() => Parse(File.ReadAllText(FilePath()));

    /// <summary>The rows of the terms table: every pipe-delimited line
    /// after the header row and its separator. Blank tags or types are no
    /// pull.</summary>
    public static IReadOnlyList<Entry> Parse(string markdown)
    {
        var rows = markdown.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|'))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .ToList();
        var header = rows.FindIndex(cells => cells[0] == "term");
        return [.. rows.Skip(header + 2)
            .Where(cells => cells.Length == 5)
            .Select(cells => new Entry(cells[0], cells[1], List(cells[2]), List(cells[3]), cells[4]))];
    }

    /// <summary>The longest term the query contains as whole words, case
    /// folded, a plural allowed ("board wipes"), or null when no term
    /// applies. The phrasing replaces the term and the rest of the wording
    /// stays.</summary>
    public static Expansion? Expand(string query, IReadOnlyList<Entry> entries)
    {
        foreach (var entry in entries.OrderByDescending(e => e.Term.Length))
        {
            var term = new Regex($@"\b{Regex.Escape(entry.Term)}(?:e?s)?\b", RegexOptions.IgnoreCase);
            if (term.IsMatch(query))
            {
                return new Expansion(entry, term.Replace(query, entry.Phrasing, 1));
            }
        }

        return null;
    }

    private static IReadOnlyList<string> List(string cell)
        => [.. cell.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
