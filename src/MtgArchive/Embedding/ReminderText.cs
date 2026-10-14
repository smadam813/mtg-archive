using System.Text;
using System.Text.RegularExpressions;

namespace MtgArchive.Embedding;

/// <summary>
/// Removes parenthetical reminder text from oracle text
/// (<see cref="CardDocumentComposition.StripReminderText"/>). Oracle text
/// puts reminder text, and nothing else, in parentheses, so the stripper
/// removes every parenthetical, nested ones as one cut. A keyword's name
/// and cost stay ("Infect", "Miracle {1}{U}"). A malformed parenthesis
/// never reaches the embedding: an unclosed "(" cuts to the end of its
/// line, and a stray ")" leaves. The stripper drops a line left empty and
/// closes the space or punctuation around the cut.
/// </summary>
public static partial class ReminderText
{
    public static string Strip(string oracleText)
    {
        var lines = oracleText.Split('\n')
            .Select(StripLine)
            .Where(line => line.Length > 0);
        return string.Join('\n', lines);
    }

    private static string StripLine(string line)
    {
        var kept = new StringBuilder(line.Length);
        var depth = 0;
        foreach (var c in line)
        {
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0)
            {
                kept.Append(c);
            }
        }

        var closed = SpaceBeforePunctuation().Replace(RunOfSpaces().Replace(kept.ToString(), " "), "$1");
        return closed.Trim();
    }

    [GeneratedRegex(" {2,}")]
    private static partial Regex RunOfSpaces();

    [GeneratedRegex(@" +([.,;:])")]
    private static partial Regex SpaceBeforePunctuation();
}
