using System.Globalization;
using System.Text.RegularExpressions;

namespace MtgArchive.Rules;

/// <summary>
/// Parses a Comprehensive Rules TXT into the corpus shapes: the effective
/// date from the header, one chunk per numbered rule (the rule together
/// with its subrules and examples), and one chunk per glossary entry.
///
/// Structure the parser relies on (verified against the published files):
/// the header line "These rules are effective as of &lt;Month D, YYYY&gt;.";
/// a table of contents whose last line is "Credits"; the rules body, where
/// every paragraph is one line ("601.2. …" opens a chunk; subrules like
/// "601.2a …" — including anomalies "119.1d." and "704.5aa" —, "Example: …"
/// paragraphs, and space-indented notes continue it; "601. Casting Spells"
/// section headers close it); then a "Glossary" line, blank-line-separated
/// entries (term line first), and a final "Credits" line.
/// </summary>
public static partial class CrChunker
{
    [GeneratedRegex(@"^These rules are effective as of (?<date>.+?)\.\s*$", RegexOptions.Multiline)]
    private static partial Regex EffectiveDateLine();

    [GeneratedRegex(@"^(?<number>\d+\.\d+)\. ")]
    private static partial Regex ChunkStart();

    [GeneratedRegex(@"^\d+\. ")]
    private static partial Regex SectionHeader();

    /// <summary>
    /// Normalizes raw CR text to the archived form: BOM stripped, LF line
    /// endings. Chunks are verbatim substrings of this form.
    /// </summary>
    public static string Normalize(string rawText)
        => rawText.TrimStart((char)0xFEFF).ReplaceLineEndings("\n");

    /// <summary>
    /// Parses normalized CR text, appending structural problems to
    /// <paramref name="errors"/>. The caller fails the ingest run when any
    /// error lands — a partial parse is never written to the corpus.
    /// </summary>
    public static CrParse Parse(string text, List<string> errors)
    {
        var effectiveDate = ParseEffectiveDate(text, errors);
        var lines = text.Split('\n');

        var tocEnd = Array.FindIndex(lines, line => line.TrimEnd() == "Credits");
        if (tocEnd < 0)
        {
            errors.Add("CR text has no table of contents ending in a 'Credits' line.");
            return new CrParse(effectiveDate, [], []);
        }

        var glossaryStart = Array.FindIndex(lines, tocEnd + 1, line => line.TrimEnd() == "Glossary");
        if (glossaryStart < 0)
        {
            errors.Add("CR text has no 'Glossary' section after the rules body.");
            return new CrParse(effectiveDate, [], []);
        }

        var creditsStart = Array.FindIndex(lines, glossaryStart + 1, line => line.TrimEnd() == "Credits");
        if (creditsStart < 0)
        {
            errors.Add("CR text has no 'Credits' section after the glossary.");
            return new CrParse(effectiveDate, [], []);
        }

        var rules = ChunkRules(lines[(tocEnd + 1)..glossaryStart], errors);
        var glossary = ChunkGlossary(lines[(glossaryStart + 1)..creditsStart], errors);
        if (rules.Count == 0)
        {
            errors.Add("CR rules body produced no chunks.");
        }

        if (glossary.Count == 0)
        {
            errors.Add("CR glossary produced no entries.");
        }

        return new CrParse(effectiveDate, rules, glossary);
    }

    /// <summary>The header's effective date alone — the corpus-state gate's
    /// CR freshness stamp, read without chunking the body.</summary>
    public static DateOnly ParseEffectiveDate(string text, List<string> errors)
    {
        var match = EffectiveDateLine().Match(text);
        if (!match.Success)
        {
            errors.Add("CR header has no 'These rules are effective as of …' line.");
            return default;
        }

        if (!DateOnly.TryParseExact(
                match.Groups["date"].Value, "MMMM d, yyyy",
                CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var date))
        {
            errors.Add($"CR effective date '{match.Groups["date"].Value}' is not a 'Month d, yyyy' date.");
            return default;
        }

        return date;
    }

    private static List<RuleChunk> ChunkRules(string[] bodyLines, List<string> errors)
    {
        var chunks = new List<RuleChunk>();
        var numbers = new HashSet<string>();
        string? number = null;
        List<string>? buffer = null;

        void Close()
        {
            if (number is null || buffer is null)
            {
                return;
            }

            while (buffer.Count > 0 && string.IsNullOrWhiteSpace(buffer[^1]))
            {
                buffer.RemoveAt(buffer.Count - 1);
            }

            if (!numbers.Add(number))
            {
                errors.Add($"CR rules body repeats rule number {number}.");
            }

            chunks.Add(new RuleChunk(number, string.Join('\n', buffer)));
            (number, buffer) = (null, null);
        }

        foreach (var line in bodyLines)
        {
            var start = ChunkStart().Match(line);
            if (start.Success)
            {
                Close();
                number = start.Groups["number"].Value;
                buffer = [line];
            }
            else if (SectionHeader().IsMatch(line))
            {
                // "601. Casting Spells" / "1. Game Concepts" — headers close
                // the open chunk and belong to none.
                Close();
            }
            else
            {
                // Subrules, examples, indented notes, and interior blank
                // lines stay in the chunk; content outside any chunk is
                // section-header surroundings.
                buffer?.Add(line);
            }
        }

        Close();
        return chunks;
    }

    private static List<GlossaryChunk> ChunkGlossary(string[] glossaryLines, List<string> errors)
    {
        var chunks = new List<GlossaryChunk>();
        var terms = new HashSet<string>();
        var buffer = new List<string>();

        void Close()
        {
            if (buffer.Count == 0)
            {
                return;
            }

            if (!terms.Add(buffer[0]))
            {
                errors.Add($"CR glossary repeats term '{buffer[0]}'.");
            }

            chunks.Add(new GlossaryChunk(buffer[0], string.Join('\n', buffer)));
            buffer.Clear();
        }

        foreach (var line in glossaryLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                Close();
            }
            else
            {
                buffer.Add(line);
            }
        }

        Close();
        return chunks;
    }
}

public sealed record CrParse(
    DateOnly EffectiveDate,
    IReadOnlyList<RuleChunk> Rules,
    IReadOnlyList<GlossaryChunk> Glossary);

/// <summary>One numbered rule with its subrules and examples, verbatim.</summary>
public sealed record RuleChunk(string Number, string Text);

/// <summary>One glossary entry: the term line plus its definition, verbatim.</summary>
public sealed record GlossaryChunk(string Term, string Text);
