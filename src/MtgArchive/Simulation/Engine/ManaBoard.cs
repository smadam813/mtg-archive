namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The mana a player can make now, as the engine reports it: the floating
/// pool, the life total that Phyrexian pips can spend, the untapped
/// permanents that tap for mana, and the cards in hand that exile for mana.
/// <see cref="GreedyPayment"/> pays and predicts over it. The lists keep
/// the engine's order, because the greedy choice breaks ties by that order.
/// </summary>
public sealed record ManaBoard(
    ManaPool Pool,
    int Life,
    IReadOnlyList<BoardManaSource> Sources,
    IReadOnlyList<HandManaOption> HandSources);

/// <summary>An untapped permanent that can tap for mana now, with every
/// mana string one tap can add ("{G}", "{C}{C}"). <see cref="Id"/> is the
/// permanent's id in the pilot view.</summary>
public sealed record BoardManaSource(string Id, IReadOnlyList<string> Options);

/// <summary>A card in hand with a mana ability that works from hand, with
/// every mana string one activation can add. The activation exiles the
/// card. Each copy in hand is its own option.</summary>
public sealed record HandManaOption(DeckCard Card, IReadOnlyList<string> Options);

/// <summary>A way to pay a cost from a <see cref="ManaBoard"/>: the sources
/// to tap, the hand cards to exile, the mana to spend from the pool once
/// they have produced, and the life to spend on Phyrexian pips.</summary>
public sealed record BoardPayment(
    IReadOnlyList<BoardTap> Taps,
    IReadOnlyList<HandTap> FromHand,
    ManaPool Spend,
    int Life);

/// <summary>One tap in a payment: the source and the mana it produces,
/// one of the source's options.</summary>
public sealed record BoardTap(BoardManaSource Source, string Produces);

/// <summary>One hand card in a payment: the option and the mana its
/// ability adds.</summary>
public sealed record HandTap(HandManaOption Source, string Produces);
