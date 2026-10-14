namespace MtgArchive.Simulation.Engine;

/// <summary>
/// A multiset of mana symbols: the player's pool, or the part of it a
/// payment spends. Symbols are W U B R G C. The pool persists for the
/// turn and empties at cleanup, a documented simplification of CR 106.4.
/// </summary>
public sealed class ManaPool : IEquatable<ManaPool>
{
    public static readonly string[] Symbols = ["W", "U", "B", "R", "G", "C"];

    private readonly Dictionary<string, int> _counts = Symbols.ToDictionary(s => s, _ => 0, StringComparer.Ordinal);

    public ManaPool()
    {
    }

    public ManaPool(ManaPool other)
    {
        foreach (var symbol in Symbols)
        {
            _counts[symbol] = other._counts[symbol];
        }
    }

    /// <summary>Parses a mana string of colored and colorless symbols ("{C}{C}").
    /// Generic symbols have no place in a pool and are rejected.</summary>
    public static ManaPool Parse(string mana)
    {
        var pool = new ManaPool();
        pool.Add(mana);
        return pool;
    }

    public int this[string symbol] => _counts.TryGetValue(symbol, out var count)
        ? count
        : throw new ArgumentException($"'{symbol}' is not a mana symbol.", nameof(symbol));

    public int Total => _counts.Values.Sum();

    public bool IsEmpty => Total == 0;

    public void AddSymbol(string symbol, int amount = 1)
    {
        if (!_counts.ContainsKey(symbol))
        {
            throw new ArgumentException($"'{symbol}' is not a mana symbol.", nameof(symbol));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        _counts[symbol] += amount;
    }

    /// <summary>Adds every symbol of a mana string ("{C}{C}").</summary>
    public void Add(string mana)
    {
        foreach (var symbol in ManaSymbols.Parse(mana))
        {
            if (symbol.Kind != ManaSymbolKind.Color || symbol.Colors.Count != 1)
            {
                throw new ArgumentException($"'{symbol.Text}' cannot be added to a mana pool.", nameof(mana));
            }

            _counts[symbol.Colors[0]]++;
        }
    }

    public void Add(ManaPool other)
    {
        foreach (var symbol in Symbols)
        {
            _counts[symbol] += other._counts[symbol];
        }
    }

    public bool Contains(ManaPool other) => Symbols.All(s => _counts[s] >= other._counts[s]);

    public void RemoveSymbol(string symbol, int amount = 1)
    {
        if (this[symbol] < amount)
        {
            throw new InvalidOperationException($"The pool holds {this[symbol]} {{{symbol}}}, not {amount}.");
        }

        _counts[symbol] -= amount;
    }

    public void Remove(ManaPool other)
    {
        if (!Contains(other))
        {
            throw new InvalidOperationException($"The pool {this} does not hold {other}.");
        }

        foreach (var symbol in Symbols)
        {
            _counts[symbol] -= other._counts[symbol];
        }
    }

    public void Clear()
    {
        foreach (var symbol in Symbols)
        {
            _counts[symbol] = 0;
        }
    }

    public bool Equals(ManaPool? other) => other is not null && Symbols.All(s => _counts[s] == other._counts[s]);

    public override bool Equals(object? obj) => Equals(obj as ManaPool);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var symbol in Symbols)
        {
            hash.Add(_counts[symbol]);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
        => IsEmpty ? "{}" : string.Concat(Symbols.SelectMany(s => Enumerable.Repeat($"{{{s}}}", _counts[s])));
}
