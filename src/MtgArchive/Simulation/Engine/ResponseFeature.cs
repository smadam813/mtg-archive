namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The response feature of one card face (ADR 0013; glossary: Card feature
/// profile, Response card): what the face can answer in a response window.
/// The <c>card_feature_derive</c> ingest phase derives it from Oracle text.
/// A face with no counter and no grant has no response feature: the
/// profile's <see cref="CardFeatureProfile.Response"/> is null.
/// </summary>
/// <param name="Counter">The face's counter; null when it counters nothing.</param>
/// <param name="Grants">The face's one-shot protection grants, in text
/// order.</param>
public sealed record ResponseFeature(CounterFeature? Counter, IReadOnlyList<ProtectionGrant> Grants)
{
    /// <summary>Compares the grant list by content. The generated record
    /// equality compares it by reference.</summary>
    public bool Equals(ResponseFeature? other)
        => other is not null && Counter == other.Counter && Grants.SequenceEqual(other.Grants);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Counter);
        foreach (var grant in Grants)
        {
            hash.Add(grant);
        }

        hash.Add(Grants.Count);
        return hash.ToHashCode();
    }
}

/// <summary>What a counter can target: a spell, or an activated or
/// triggered ability.</summary>
public enum CounterReach
{
    Spell,

    /// <summary>An activated or triggered ability (Stifle). It reaches no
    /// disruption card, which is a spell.</summary>
    Ability,
}

/// <summary>
/// A counter, as the derivation reads it from "Counter target … spell".
/// </summary>
/// <param name="Reach">A spell or an ability.</param>
/// <param name="Types">The spell must have one of these card types
/// ("Instant", "Sorcery"); empty for any type.</param>
/// <param name="ExcludedTypes">The spell must have none of these card
/// types ("Creature" for "noncreature"); empty for none.</param>
/// <param name="Colors">The spell must have one of these colors, as
/// single symbols ("U"); empty for any color.</param>
/// <param name="UnlessPays">The "unless its controller pays" amount in
/// Scryfall symbol form ("{1}", "{X}"); null for a hard counter.</param>
/// <param name="LosesGame">The face carries the pact clause: its
/// controller loses the game unless a later payment is made.</param>
public sealed record CounterFeature(
    CounterReach Reach,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> ExcludedTypes,
    IReadOnlyList<string> Colors,
    string? UnlessPays,
    bool LosesGame)
{
    /// <summary>True when the counter can counter a spell with these card
    /// types and colors. The "unless" amount and the pact clause do not
    /// change the reach.</summary>
    public bool Reaches(IReadOnlyCollection<string> cardTypes, IReadOnlyCollection<string> colors)
        => Reach == CounterReach.Spell
            && (Types.Count == 0 || Types.Any(cardTypes.Contains))
            && !ExcludedTypes.Any(cardTypes.Contains)
            && (Colors.Count == 0 || Colors.Any(colors.Contains));

    /// <summary>Compares the lists by content. The generated record
    /// equality compares them by reference.</summary>
    public bool Equals(CounterFeature? other)
        => other is not null
            && Reach == other.Reach
            && Types.SequenceEqual(other.Types, StringComparer.Ordinal)
            && ExcludedTypes.SequenceEqual(other.ExcludedTypes, StringComparer.Ordinal)
            && Colors.SequenceEqual(other.Colors, StringComparer.Ordinal)
            && UnlessPays == other.UnlessPays
            && LosesGame == other.LosesGame;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Reach);
        foreach (var list in new[] { Types, ExcludedTypes, Colors })
        {
            foreach (var item in list)
            {
                hash.Add(item, StringComparer.Ordinal);
            }

            hash.Add(list.Count);
        }

        hash.Add(UnlessPays);
        hash.Add(LosesGame);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One one-shot protection grant (ADR 0013): what it grants and to whom, as
/// wire names. The protection families of <c>ResponseDerivation</c> write
/// only the names below. Static and attached grants get no grant: XMage
/// enforces them with no pilot decision.
/// </summary>
/// <param name="Grant">What the grant gives: <see cref="Hexproof"/>,
/// <see cref="Shroud"/>, <see cref="Indestructible"/>,
/// <see cref="PhaseOut"/>, <see cref="CantBeCountered"/>,
/// <see cref="ProtectionFromEverything"/>,
/// <see cref="ProtectionFromAllColors"/>, or
/// <c>protection_from_&lt;color&gt;</c> and <c>hexproof_from_&lt;color&gt;</c>
/// with a color word ("black").</param>
/// <param name="Scope">Who gets it: <see cref="TargetCreature"/>,
/// <see cref="TargetPermanent"/>, <see cref="Creatures"/> (creatures you
/// control), <see cref="Permanents"/> (permanents you control), or
/// <see cref="Spells"/> (spells you control).</param>
public sealed record ProtectionGrant(string Grant, string Scope)
{
    public const string Hexproof = "hexproof";

    public const string Shroud = "shroud";

    public const string Indestructible = "indestructible";

    public const string PhaseOut = "phase_out";

    public const string CantBeCountered = "cant_be_countered";

    public const string ProtectionFromEverything = "protection_from_everything";

    public const string ProtectionFromAllColors = "protection_from_all_colors";

    public const string TargetCreature = "target_creature";

    public const string TargetPermanent = "target_permanent";

    public const string Creatures = "creatures";

    public const string Permanents = "permanents";

    public const string Spells = "spells";

    /// <summary>The color words of the color grants, by color symbol.</summary>
    public static readonly IReadOnlyDictionary<string, string> ColorWords = new Dictionary<string, string>
    {
        ["W"] = "white",
        ["U"] = "blue",
        ["B"] = "black",
        ["R"] = "red",
        ["G"] = "green",
    };

    /// <summary>"protection_from_black" for "B".</summary>
    public static string ProtectionFrom(string color) => "protection_from_" + ColorWords[color];

    /// <summary>"hexproof_from_black" for "B".</summary>
    public static string HexproofFrom(string color) => "hexproof_from_" + ColorWords[color];

    /// <summary>A grant to every creature or every permanent we control.</summary>
    public bool IsTeam => Scope is Creatures or Permanents;

    /// <summary>
    /// True when the grant answers the disruption card for a victim its
    /// scope covers (the pilot response rule):
    /// <list type="bullet">
    /// <item>Counterspell: "can't be countered" on the spells we control.</item>
    /// <item>Atomize: hexproof, shroud, indestructible or phase-out, or
    /// protection or hexproof from one of the card's colors, on a permanent
    /// scope.</item>
    /// <item>Rout: team indestructible or team phase-out.</item>
    /// </list>
    /// Phase-out also needs the turn rule, which the pilot checks.
    /// </summary>
    public bool Answers(DisruptionCard threat) => threat.Kind switch
    {
        DisruptionKind.Counterspell => Grant == CantBeCountered && Scope == Spells,
        DisruptionKind.TargetedRemoval => Scope is TargetCreature or TargetPermanent or Creatures or Permanents
            && (Grant is Hexproof or Shroud or Indestructible or PhaseOut or ProtectionFromEverything or ProtectionFromAllColors
                || threat.Colors.Any(c => Grant == ProtectionFrom(c) || Grant == HexproofFrom(c))),
        DisruptionKind.BoardWipe => IsTeam && Grant is Indestructible or PhaseOut,
        _ => false,
    };

    /// <summary>True when the scope covers a victim of a card the grant
    /// <see cref="Answers"/>: a creature scope covers only a creature, and
    /// every other scope covers any victim of that card.</summary>
    public bool Covers(DeckCard victim) => Scope is not (TargetCreature or Creatures) || victim.IsCreature;
}
