namespace MtgArchive.Simulation;

public enum ColorSpread
{
    Mono,
    Dual,
    ThreePlus,
}

/// <summary>
/// One mana source: a deck card that produces mana directly and/or fetches
/// it. <see cref="Colors"/> is every color it can produce (fetches resolved
/// deck-relatively), each counted fully — duals double-count on purpose, and
/// <see cref="Spread"/> is how consumers see that sharing.
/// </summary>
public sealed record ManaSource(
    Guid OracleId,
    string Name,
    int Quantity,
    IReadOnlyList<string> Colors,
    ColorSpread Spread,
    int AvailabilityTurn);

/// <summary>Why the tier-1 table leaves a source out: a closed vocabulary.</summary>
public enum ExclusionReason
{
    /// <summary>See <see cref="Data.SourceProfile.OpponentDependentContract"/>.</summary>
    OpponentDependent,

    /// <summary>See <see cref="Data.SourceProfile.TokenToAnotherPlayerContract"/>.</summary>
    TokenToAnotherPlayer,
}

/// <summary>A source the tier-1 table leaves out, with its reason.</summary>
public sealed record ExcludedSource(Guid OracleId, string Name, int Quantity, ExclusionReason Reason);

/// <summary>
/// The tier-1 mana-source table, built from stored source
/// profiles only. Fetches resolve at query time to the union of colors of
/// the deck's lands matching their criteria, so the same fetch card yields
/// different colors in different decks and nothing in a deck with no match.
/// A card counts by the colors of all its faces, so a pathway makes both.
/// Availability: lands and MDFC lands on their drop turn (turn 1; ETB-tapped ignored),
/// nonland producers at mana value + 1. The commander is not in the library
/// and is never a source. <c>C</c> keeps its own column; the spread bucket
/// counts WUBRG only, so a colorless-only source is mono. An identity-bound
/// source (Command Tower, Exotic Orchard) makes only the colors
/// of the deck's color identity, and its spread counts only those colors.
/// Tier 1 has no opponents, so the table does not count an opponent-dependent
/// source (Smothering Tithe, Land Tax). It lists the
/// source in <see cref="Excluded"/> when the source would make a color in the deck.
/// </summary>
public sealed class ManaSourceTable
{
    public static readonly string[] Colors = ["W", "U", "B", "R", "G", "C"];

    private ManaSourceTable(IReadOnlyList<ManaSource> sources, IReadOnlyList<ExcludedSource> excluded, IReadOnlyList<string> identity)
    {
        Sources = sources;
        Excluded = excluded;
        Identity = identity;
    }

    public IReadOnlyList<ManaSource> Sources { get; }

    /// <summary>The deck's sources that the table leaves out, each with its reason.</summary>
    public IReadOnlyList<ExcludedSource> Excluded { get; }

    /// <summary>The deck's color identity, in WUBRG order. It is the union
    /// of the commanders' color identities. A deck with no commander uses
    /// the union of every card's color identity.</summary>
    public IReadOnlyList<string> Identity { get; }

    /// <summary>Colorless, or a color of the deck's color identity.</summary>
    public bool InIdentity(string color) => InIdentity(color, Identity);

    public static ManaSourceTable Build(IReadOnlyList<DeckCard> deck)
    {
        var commanders = deck.Where(c => c.IsCommander).ToList();
        var identityColors = (commanders.Count > 0 ? commanders : deck).SelectMany(c => c.ColorIdentity).ToHashSet();
        string[] identity = [.. Colors.Where(identityColors.Contains)];
        var lands = deck.Where(c => c.IsLand && !c.IsCommander).ToList();
        var candidates = deck
            .Where(c => !c.IsCommander)
            .Select(c => (Card: c, Colors: ColorsOf(c, lands, identity)))
            .Where(s => s.Colors.Count > 0)
            .ToList();
        var excluded = candidates
            .Where(s => s.Card.OpponentDependent || s.Card.TokenToAnotherPlayer)
            .Select(s => new ExcludedSource(
                s.Card.OracleId,
                s.Card.Name,
                s.Card.Quantity,
                s.Card.OpponentDependent ? ExclusionReason.OpponentDependent : ExclusionReason.TokenToAnotherPlayer))
            .ToList();
        var sources = candidates
            .Where(s => !s.Card.OpponentDependent && !s.Card.TokenToAnotherPlayer)
            .Select(s => new ManaSource(
                s.Card.OracleId,
                s.Card.Name,
                s.Card.Quantity,
                s.Colors,
                SpreadOf(s.Colors),
                s.Card.HasLandFace ? 1 : (int)s.Card.ManaValue + 1))
            .ToList();
        return new ManaSourceTable(sources, excluded, identity);
    }

    /// <summary>Quantity of sources producing <paramref name="color"/> with availability turn ≤ <paramref name="turn"/>.</summary>
    public int Supply(string color, int turn) => Supply([color], turn);

    /// <summary>Quantity of sources producing any of <paramref name="colors"/> with availability turn ≤ <paramref name="turn"/>; a dual counts once.</summary>
    public int Supply(IEnumerable<string> colors, int turn)
        => Available(colors, turn).Sum(s => s.Quantity);

    /// <summary>The sources producing any of <paramref name="colors"/> with
    /// availability turn ≤ <paramref name="turn"/>: one scan that serves both
    /// the supply count and the casting-probability category.</summary>
    public IReadOnlyList<ManaSource> Available(IEnumerable<string> colors, int turn)
    {
        var wanted = colors.ToHashSet();
        return Sources.Where(s => s.AvailabilityTurn <= turn && s.Colors.Any(wanted.Contains)).ToList();
    }

    public int Count(string color) => Supply(color, int.MaxValue);

    /// <summary>
    /// The oracle_ids of sources producing any of <paramref name="colors"/>
    /// with availability turn ≤ <paramref name="turn"/> — the category a
    /// casting probability is the category draw probability of.
    /// </summary>
    public IReadOnlySet<Guid> TurnAvailable(IEnumerable<string> colors, int turn)
        => Available(colors, turn).Select(s => s.OracleId).ToHashSet();

    private static List<string> ColorsOf(DeckCard card, List<DeckCard> lands, string[] identity)
    {
        var fetched = card.FetchLandTypes is not null
            ? lands.Where(l => Fetches(card, l)).SelectMany(l => l.AnyFaceColors)
            : [];
        var all = card.AnyFaceColors.Concat(fetched).ToHashSet();
        return [.. Colors.Where(c => all.Contains(c) && (!card.IdentityBound || InIdentity(c, identity)))];
    }

    private static bool InIdentity(string color, IReadOnlyList<string> identity) => color == "C" || identity.Contains(color);

    /// <summary>The lands a fetch profile finds: basic when the profile is
    /// basic-only, and of one of the profile's types when it names any.</summary>
    private static bool Fetches(DeckCard fetch, DeckCard land)
        => land.CardTypes.Contains("Land", StringComparer.Ordinal)
        && (!fetch.FetchBasicOnly || land.Supertypes.Contains("Basic", StringComparer.Ordinal))
        && (fetch.FetchLandTypes is not { Length: > 0 } types || land.Subtypes.Intersect(types, StringComparer.Ordinal).Any());

    private static ColorSpread SpreadOf(List<string> colors) => colors.Count(c => c != "C") switch
    {
        <= 1 => ColorSpread.Mono,
        2 => ColorSpread.Dual,
        _ => ColorSpread.ThreePlus,
    };
}
