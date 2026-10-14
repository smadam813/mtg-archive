using System.Globalization;

namespace MtgArchive.CollectionImport;

/// <summary>
/// Parses the plain-text decklist format — exactly
/// <c>&lt;quantity&gt; &lt;exact card name&gt;</c> per line, e.g.
/// <c>1 Pithing Needle</c> or <c>1 Fire // Ice</c> — into the same
/// <see cref="ImportRow"/> model as the ManaBox CSV parser. Rows carry only
/// name and quantity; there are no provenance columns. Blank lines are
/// skipped; any other line that does not fit the format throws
/// <see cref="FormatException"/> naming the line. Names are NFC-normalized
/// for the shared exact-match resolver, which gives these rows (and only
/// these) a front-face fallback.
/// </summary>
public static class DecklistParser
{
    public static IReadOnlyList<ImportRow> Parse(TextReader reader)
    {
        var rows = new List<ImportRow>();
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!TryParseLine(line, out var quantity, out var name))
            {
                throw new FormatException(BadLine(lineNumber, line));
            }

            rows.Add(new ImportRow
            {
                Name = name,
                Quantity = quantity,
                LineNumber = lineNumber,
            });
        }

        return rows;
    }

    /// <summary>Reads one <c>&lt;quantity&gt; &lt;name&gt;</c> line: a
    /// positive integer, one space, and a name that does not start with
    /// white space. The name keeps everything after the space, right-trimmed.</summary>
    public static bool TryParseLine(string line, out int quantity, out string name)
    {
        var separator = line.IndexOf(' ');
        name = separator < 0 ? "" : line[(separator + 1)..].TrimEnd();
        quantity = 0;
        return separator >= 1
            && name.Length > 0
            && !char.IsWhiteSpace(name[0])
            && int.TryParse(line[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out quantity)
            && quantity >= 1;
    }

    /// <summary>The one bad-line message: the line number and its text.</summary>
    public static string BadLine(int lineNumber, string line)
        => $"Line {lineNumber}: expected '<quantity> <exact card name>', got '{line}'.";
}
