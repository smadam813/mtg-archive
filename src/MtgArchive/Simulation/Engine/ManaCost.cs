namespace MtgArchive.Simulation.Engine;

/// <summary>
/// One symbol of a cost the engine pays. A pip takes one mana of any color
/// in <see cref="Options"/>. A Phyrexian pip also takes 2 life.
/// <see cref="GenericAlternative"/> is the generic amount a {2/W} pip takes
/// instead of its color.
/// </summary>
public sealed record ManaPip(IReadOnlyList<string> Options, bool Phyrexian)
{
    public int? GenericAlternative { get; init; }

    /// <summary>Compares the options by value: the record default compares
    /// the list by reference.</summary>
    public bool Equals(ManaPip? other)
        => other is not null
        && Phyrexian == other.Phyrexian
        && GenericAlternative == other.GenericAlternative
        && Options.SequenceEqual(other.Options);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Phyrexian);
        hash.Add(GenericAlternative);
        foreach (var option in Options)
        {
            hash.Add(option);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// A parsed mana cost: the generic amount plus pips. X counts as zero.
/// </summary>
public sealed record ManaCost(int Generic, IReadOnlyList<ManaPip> Pips)
{
    public static ManaCost Parse(string? cost)
    {
        var generic = 0;
        var pips = new List<ManaPip>();
        foreach (var symbol in ManaSymbols.Parse(cost))
        {
            switch (symbol.Kind)
            {
                case ManaSymbolKind.Generic:
                    generic += symbol.Amount;
                    break;

                case ManaSymbolKind.Phyrexian:
                    pips.Add(new ManaPip(symbol.Colors, Phyrexian: true));
                    break;

                case ManaSymbolKind.GenericHybrid:
                    pips.Add(new ManaPip(symbol.Colors, Phyrexian: false) with { GenericAlternative = symbol.Amount });
                    break;

                case ManaSymbolKind.Color:
                case ManaSymbolKind.Hybrid:
                    pips.Add(new ManaPip(symbol.Colors, Phyrexian: false));
                    break;
            }
        }

        return new ManaCost(generic, pips);
    }

    /// <summary>The cost plus generic mana: the commander tax.</summary>
    public ManaCost Plus(int generic) => this with { Generic = Generic + generic };

    /// <summary>
    /// Whether a spend and a life total pay this cost exactly: every pip
    /// takes one spent symbol it accepts, or 2 life when Phyrexian, or its
    /// generic alternative; what remains of the spend equals the generic
    /// part; no mana and no life is left over.
    /// </summary>
    public bool IsPaidBy(ManaPool spend, int life)
    {
        var remaining = new ManaPool(spend);
        return Assign(0, remaining, Generic, life);
    }

    private bool Assign(int index, ManaPool remaining, int generic, int life)
    {
        if (index == Pips.Count)
        {
            return life == 0 && remaining.Total == generic;
        }

        var pip = Pips[index];
        foreach (var option in pip.Options.Distinct())
        {
            if (remaining[option] == 0)
            {
                continue;
            }

            remaining.RemoveSymbol(option);
            var paid = Assign(index + 1, remaining, generic, life);
            remaining.AddSymbol(option);
            if (paid)
            {
                return true;
            }
        }

        if (pip.Phyrexian && life >= 2 && Assign(index + 1, remaining, generic, life - 2))
        {
            return true;
        }

        return pip.GenericAlternative is { } alternative && Assign(index + 1, remaining, generic + alternative, life);
    }

    /// <summary>Compares the pips by value: the record default compares the
    /// list by reference.</summary>
    public bool Equals(ManaCost? other)
        => other is not null && Generic == other.Generic && Pips.SequenceEqual(other.Pips);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Generic);
        foreach (var pip in Pips)
        {
            hash.Add(pip);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        var generic = Generic > 0 ? $"{{{Generic}}}" : string.Empty;
        return generic + string.Concat(Pips.Select(p =>
            p.Phyrexian ? $"{{{string.Join('/', p.Options)}/P}}"
            : p.GenericAlternative is { } alt ? $"{{{alt}/{p.Options[0]}}}"
            : $"{{{string.Join('/', p.Options)}}}"));
    }
}
