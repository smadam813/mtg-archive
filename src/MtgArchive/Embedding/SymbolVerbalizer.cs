using System.Text.RegularExpressions;
using MtgArchive.Simulation;

namespace MtgArchive.Embedding;

/// <summary>
/// Renders the symbols Scryfall prints in oracle text as words
/// (<see cref="CardDocumentComposition.VerbalizeSymbols"/>). <c>{T}</c> is
/// "tap", <c>{C}{C}</c> is "two colorless mana", <c>{W/P}</c> is
/// "Phyrexian white mana", <c>{E}{E}</c> is "two energy counters". A run of
/// adjacent symbols reads as one phrase. Mana symbols parse through
/// <see cref="ManaSymbols"/>, the one mana-symbol grammar. The function is
/// pure: the same text always gives the same words, and no model is used.
/// A symbol the table does not know passes through verbatim; the
/// verbalizer counts it in <c>unknownSymbols</c> for the ingest report.
/// </summary>
public static partial class SymbolVerbalizer
{
    private static readonly string[] Numbers =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
    ];

    private static readonly Dictionary<string, string> Colors = new()
    {
        ["W"] = "white",
        ["U"] = "blue",
        ["B"] = "black",
        ["R"] = "red",
        ["G"] = "green",
        ["C"] = "colorless",
    };

    /// <summary>Symbols that name one thing each. The plural adds an "s";
    /// the article follows the noun's first letter.</summary>
    private static readonly Dictionary<string, string> Countables = new()
    {
        ["E"] = "energy counter",
        ["TK"] = "ticket",
        ["A"] = "acorn counter",
        ["P"] = "pawprint",
    };

    /// <summary>Symbols with one fixed rendering, never counted.</summary>
    private static readonly Dictionary<string, string> Fixed = new()
    {
        ["T"] = "tap",
        ["Q"] = "untap",
        ["CHAOS"] = "chaos symbol",
        ["PW"] = "a planeswalker symbol",
        ["D"] = "a land drop",
        ["H"] = "one colored mana or two life",
    };

    /// <summary>Mana symbols oracle text prints that <see cref="ManaSymbols"/>
    /// classifies as <see cref="ManaSymbolKind.Other"/>.</summary>
    private static readonly Dictionary<string, string> RareMana = new()
    {
        ["S"] = "snow mana",
        ["Y"] = "Y mana",
        ["Z"] = "Z mana",
        ["L"] = "legendary mana",
        ["∞"] = "infinite mana",
        ["½"] = "half a generic mana",
        ["HW"] = "half a white mana",
        ["HR"] = "half a red mana",
    };

    public static string Verbalize(string oracleText, SymbolCounter? unknownSymbols = null)
        => SymbolRun().Replace(oracleText, match => RenderRun(oracleText, match, unknownSymbols));

    private static string RenderRun(string text, Match run, SymbolCounter? unknownSymbols)
    {
        var symbols = Symbol().Matches(run.Value).Select(m => m.Value).ToList();
        var unknownHere = symbols.Where(s => Phrase(Key(s), 1, null) is null).ToList();
        if (unknownHere.Count > 0)
        {
            // One unknown symbol keeps the whole run verbatim: a half-read
            // cost would mislead more than symbols do.
            foreach (var symbol in unknownHere)
            {
                unknownSymbols?.Add(symbol);
            }

            return run.Value;
        }

        var groups = new List<(string Key, int Count)>();
        foreach (var key in symbols.Select(Key))
        {
            if (groups.Count > 0 && groups[^1].Key == key && IsCountable(key))
            {
                groups[^1] = (key, groups[^1].Count + 1);
            }
            else
            {
                groups.Add((key, 1));
            }
        }

        var before = groups.Count == 1 ? CountWordBefore.Match(text[..run.Index]) : Match.Empty;
        var countWord = before.Success ? before.Groups[1].Value : null;
        var phrases = groups.Select(g => Phrase(g.Key, g.Count, countWord)!).ToList();
        return phrases.Count switch
        {
            1 => phrases[0],
            2 => $"{phrases[0]} and {phrases[1]}",
            _ => $"{string.Join(", ", phrases[..^1])}, and {phrases[^1]}",
        };
    }

    /// <summary>Whether a repeat of the symbol reads as a count ("two
    /// colorless mana") rather than two separate amounts.</summary>
    private static bool IsCountable(string key) => !Fixed.ContainsKey(key) && !IsNumber(key);

    private static string Key(string symbol) => symbol[1..^1].ToUpperInvariant();

    private static bool IsNumber(string key) => key.All(char.IsAsciiDigit);

    /// <summary>The words for <paramref name="count"/> of one symbol, or
    /// null when the table does not know the symbol. <paramref name="countWord"/>
    /// is a count the text already spells before the run ("Pay six {E}"),
    /// so the phrase adds no number of its own.</summary>
    private static string? Phrase(string key, int count, string? countWord)
    {
        if (Fixed.TryGetValue(key, out var fixedPhrase))
        {
            return fixedPhrase;
        }

        if (Countables.TryGetValue(key, out var noun))
        {
            return countWord switch
            {
                null when count > 1 => $"{NumberWord(count)} {noun}s",
                null => $"{Article(noun)} {noun}",
                "one" or "1" when count == 1 => noun,
                _ => $"{noun}s",
            };
        }

        var mana = Mana(key);
        return mana is null ? null : count > 1 && countWord is null ? $"{NumberWord(count)} {mana}" : mana;
    }

    private static string Article(string noun) => "aeiou".Contains(noun[0]) ? "an" : "a";

    /// <summary>The mana phrase for one symbol, or null when the symbol is
    /// not a mana symbol.</summary>
    private static string? Mana(string key)
    {
        if (RareMana.TryGetValue(key, out var rare))
        {
            return rare;
        }

        var symbol = ManaSymbols.Parse($"{{{key}}}").Single();
        var colors = string.Join(" or ", symbol.Colors.Select(c => Colors[c]));
        return symbol.Kind switch
        {
            ManaSymbolKind.Generic => symbol.Amount == 0 ? "zero mana" : $"{NumberWord(symbol.Amount)} generic mana",
            ManaSymbolKind.Variable => "X mana",
            ManaSymbolKind.Color or ManaSymbolKind.Hybrid => $"{colors} mana",
            ManaSymbolKind.GenericHybrid when symbol.Amount > 0 => $"{NumberWord(symbol.Amount)} generic or {colors} mana",
            ManaSymbolKind.Phyrexian when symbol.Colors.Count > 0 => $"Phyrexian {colors} mana",
            _ => null,
        };
    }

    private static string NumberWord(int n) => n < Numbers.Length ? Numbers[n] : n.ToString();

    [GeneratedRegex(@"(?:\{[^{}]+\})+")]
    private static partial Regex SymbolRun();

    [GeneratedRegex(@"\{[^{}]+\}")]
    private static partial Regex Symbol();

    /// <summary>A count already in the text ("Pay six {E}", "Pay X {A}").</summary>
    private static readonly Regex CountWordBefore =
        new($@"\b(\d+|X|{string.Join('|', Numbers)}) $", RegexOptions.Compiled);
}

/// <summary>Occurrences per unknown symbol, most frequent first.</summary>
public sealed class SymbolCounter
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    public int Total { get; private set; }

    public void Add(string symbol)
    {
        _counts[symbol] = _counts.GetValueOrDefault(symbol) + 1;
        Total++;
    }

    public IEnumerable<KeyValuePair<string, int>> Ordered
        => _counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal);
}
