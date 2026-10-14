using System.Text.RegularExpressions;
using MtgArchive.CollectionImport;

namespace MtgArchive.Simulation;

/// <summary>
/// Parses deck text into the <see cref="Decklist"/> body plus notes
/// (GLOSSARY.md "Arena export", ADR 0012). It reads two shapes: Arena export
/// text and the plain <c>&lt;qty&gt; &lt;name&gt;</c> list. It resolves no
/// names: the deck routes stay the one place that resolves names.
///
/// <c>Commander</c> lines are the commanders, the <c>Companion</c> line is
/// the companion, and <c>Deck</c> lines are the entries. A line before any
/// header is an entry, so a plain list is all entries. The parser ignores
/// <c>Sideboard</c>, <c>About</c>, and the <c>Name</c> line, and the notes
/// name them. The parser drops the Arena <c>(SET) 123</c> suffix. A line that fits no
/// shape throws <see cref="FormatException"/> with the line number and text,
/// the <see cref="DecklistParser"/> rule.
/// </summary>
public static partial class DecklistText
{
    private enum Section
    {
        Deck,
        Commander,
        Companion,
        Sideboard,
        About,
    }

    public static DecklistParseResponse Parse(string text)
    {
        var commanders = new List<DecklistCard>();
        DecklistCard? companion = null;
        string? companionName = null;
        var entries = new List<DecklistEntry>();
        var sideboard = new List<string>();
        var notes = new List<string>();
        var section = Section.Deck;
        var sawCommander = false;
        var sawSideboard = false;
        var sawAbout = false;
        var suffixes = 0;

        using var reader = new StringReader(text);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (Enum.GetNames<Section>().Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                var header = Enum.Parse<Section>(trimmed, ignoreCase: true);
                section = header;
                sawCommander |= header == Section.Commander;
                sawSideboard |= header == Section.Sideboard;
                sawAbout |= header == Section.About;
                continue;
            }

            if (trimmed.StartsWith("Name ", StringComparison.Ordinal))
            {
                notes.Add($"Ignored the deck Name '{trimmed[5..].Trim()}'.");
                continue;
            }

            if (section == Section.About)
            {
                continue;
            }

            var stripped = SetSuffix().Replace(trimmed, "");
            if (stripped.Length != trimmed.Length)
            {
                suffixes++;
            }

            if (!DecklistParser.TryParseLine(stripped, out var quantity, out var name))
            {
                throw new FormatException(DecklistParser.BadLine(lineNumber, line));
            }

            var card = new DecklistCard { Name = name };
            switch (section)
            {
                case Section.Commander when quantity == 1:
                    commanders.Add(card);
                    break;
                case Section.Companion when quantity == 1 && companion is null:
                    companion = card;
                    companionName = name;
                    break;
                case Section.Deck:
                    entries.Add(new DecklistEntry { Card = card, Quantity = quantity });
                    break;
                case Section.Sideboard:
                    sideboard.Add(name);
                    break;
                default:
                    // A commander with a quantity, or a second companion.
                    throw new FormatException(DecklistParser.BadLine(lineNumber, line));
            }
        }

        if (sawAbout)
        {
            notes.Add("Ignored the About section.");
        }

        if (sawSideboard)
        {
            notes.Add($"Ignored the Sideboard section ({sideboard.Count} lines).");
            if (companionName is not null && sideboard.Contains(companionName))
            {
                notes.Add($"The companion {companionName} also shows under Sideboard. It counts once, as the companion.");
            }
        }

        if (suffixes > 0)
        {
            notes.Add($"Dropped the (SET) number suffix from {suffixes} lines.");
        }

        if (!sawCommander)
        {
            notes.Add("No Commander section, so every card is an entry. Name the commander before you send the decklist.");
        }

        return new DecklistParseResponse(
            new Decklist { Commanders = commanders, Companion = companion, Entries = entries },
            notes);
    }

    /// <summary>The Arena printing suffix: <c> (SET) 123</c> at the end of a line.</summary>
    [GeneratedRegex(@"\s+\([A-Za-z0-9]+\)\s+[^\s()]+$")]
    private static partial Regex SetSuffix();
}

/// <summary>POST /decklist-parse body: the deck text as pasted.</summary>
public sealed record DecklistParseRequest
{
    [System.ComponentModel.Description("Arena export text or a plain '<qty> <name>' list.")]
    public required string Text { get; init; }
}

/// <summary>POST /decklist-parse response: the <c>decklist</c> body the deck
/// routes take, with names as written, and notes that name what the parse
/// ignored or assumed.</summary>
public sealed record DecklistParseResponse(Decklist Decklist, IReadOnlyList<string> Notes);
