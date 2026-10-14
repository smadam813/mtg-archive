namespace MtgArchive.Simulation.Engine;

/// <summary>Why a pick asks: the pilot answers each purpose by its own rule.</summary>
public enum PickPurpose
{
    /// <summary>A library search: pick the card it finds.</summary>
    Tutor,

    /// <summary>A scry: a card to put on the bottom, or keep the rest.</summary>
    Scry,

    /// <summary>The card to go under the others on top of the library.</summary>
    OrderTop,

    /// <summary>"You may shuffle": <see cref="PickOption.ShuffleId"/> or <see cref="PickOption.NoShuffleId"/>.</summary>
    Shuffle,

    /// <summary>A card from hand to put on top of the library.</summary>
    PutBack,

    /// <summary>A card name (Pithing Needle).</summary>
    Name,

    /// <summary>The simultaneous trigger to resolve next.</summary>
    TriggerOrder,

    /// <summary>Any other pick: the pilot takes the first option.</summary>
    Other,
}

/// <summary>One option of a pick. <see cref="Card"/> is the card it stands
/// for; the find-nothing, keep-the-rest, and shuffle options have none.</summary>
public sealed record PickOption(string Id, string Label, DeckCard? Card)
{
    /// <summary>The id of the fail-to-find option a constrained search offers.</summary>
    public const string FindNothingId = "fail to find";

    /// <summary>The id of the scry option that puts no more cards on the bottom.</summary>
    public const string KeepRestId = "keep the rest";

    /// <summary>The ids of the two answers to "you may shuffle".</summary>
    public const string ShuffleId = "shuffle";

    public const string NoShuffleId = "do not shuffle";

    public bool IsNothing => Card is null;
}

/// <summary>A pick the engine asks the pilot for. <see cref="Seen"/> holds
/// the library cards a scry or a look shows, top first; null for every
/// other pick.</summary>
public sealed record PickChoice(PickPurpose Purpose, IReadOnlyList<PickOption> Options, IReadOnlyList<DeckCard>? Seen)
{
    public PickOption? Find(string id) => Options.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));
}

/// <summary>Keep <see cref="Hand"/> or mulligan again. A keep now leaves
/// <see cref="KeepSize"/> cards after the London bottom.</summary>
public sealed record MulliganChoice(IReadOnlyList<DeckCard> Hand, int MulligansTaken, int KeepSize);

/// <summary>Put <see cref="Count"/> cards from <see cref="Hand"/> on the
/// bottom of the library (CR 103.5), first under first.</summary>
public sealed record BottomChoice(IReadOnlyList<DeckCard> Hand, int Count);
