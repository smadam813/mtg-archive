using System.Text.RegularExpressions;

namespace MtgArchive.Simulation;

/// <summary>What one mana symbol is.</summary>
public enum ManaSymbolKind
{
    /// <summary>A number: {2}.</summary>
    Generic,

    /// <summary>{X}: the engine counts it as zero.</summary>
    Variable,

    /// <summary>One color or colorless: {W}, {C}.</summary>
    Color,

    /// <summary>Two colors, either one: {W/U}.</summary>
    Hybrid,

    /// <summary>Generic or one color: {2/W}.</summary>
    GenericHybrid,

    /// <summary>A color or 2 life: {W/P}, {W/U/P}.</summary>
    Phyrexian,

    /// <summary>Any symbol the vocabulary above does not cover.</summary>
    Other,
}

/// <summary>
/// One parsed mana symbol. <see cref="Colors"/> holds the colors the symbol
/// offers. <see cref="Amount"/> holds the generic amount of a
/// <see cref="ManaSymbolKind.Generic"/> or
/// <see cref="ManaSymbolKind.GenericHybrid"/> symbol.
/// </summary>
public sealed record ManaSymbol(string Text, ManaSymbolKind Kind, IReadOnlyList<string> Colors, int Amount);

/// <summary>
/// The one mana-cost tokenizer. Every reader of a Scryfall mana cost parses
/// it here, so one grammar serves tier-1 pip demand and the tier-2 pilot.
/// </summary>
public static partial class ManaSymbols
{
    private static readonly HashSet<string> Colors = [.. ManaSourceTable.Colors];

    /// <summary>Reads a cost string ("{1}{W/U}{B/P}") symbol by symbol.</summary>
    public static IEnumerable<ManaSymbol> Parse(string? cost)
    {
        if (string.IsNullOrEmpty(cost))
        {
            yield break;
        }

        foreach (Match match in Symbol().Matches(cost))
        {
            yield return Classify(match.Groups[1].Value);
        }
    }

    /// <summary>True when every symbol in the cost is a real mana symbol:
    /// a number, {X}, a color, hybrid, or Phyrexian.</summary>
    public static bool IsCost(string cost) => Cost().IsMatch(cost);

    private static ManaSymbol Classify(string symbol)
    {
        if (int.TryParse(symbol, out var generic))
        {
            return new ManaSymbol(symbol, ManaSymbolKind.Generic, [], generic);
        }

        if (symbol == "X")
        {
            return new ManaSymbol(symbol, ManaSymbolKind.Variable, [], 0);
        }

        var parts = symbol.Split('/');
        if (parts.Contains("P"))
        {
            return new ManaSymbol(symbol, ManaSymbolKind.Phyrexian, [.. parts.Where(Colors.Contains)], 0);
        }

        if (parts.Length == 2 && parts.All(Colors.Contains))
        {
            return new ManaSymbol(symbol, ManaSymbolKind.Hybrid, parts, 0);
        }

        if (parts.Length == 2 && Colors.Contains(parts[1]))
        {
            int.TryParse(parts[0], out var alternative);
            return new ManaSymbol(symbol, ManaSymbolKind.GenericHybrid, [parts[1]], alternative);
        }

        return Colors.Contains(symbol)
            ? new ManaSymbol(symbol, ManaSymbolKind.Color, [symbol], 0)
            : new ManaSymbol(symbol, ManaSymbolKind.Other, [], 0);
    }

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex Symbol();

    [GeneratedRegex(@"^(\{([0-9]{1,9}|X|[WUBRGC]|[WUBRGC]/[WUBRGC]|[0-9]{1,9}/[WUBRGC]|[WUBRGC]/P|[WUBRGC]/[WUBRGC]/P)\})+$")]
    private static partial Regex Cost();
}
