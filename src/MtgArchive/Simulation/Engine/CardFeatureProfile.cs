namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The features the pilot reads for one card face (glossary: Card feature
/// profile). The XMage adapter reads the Oracle-derived row that the
/// <c>card_feature_derive</c> ingest phase writes.
/// </summary>
/// <param name="Develops">The card develops mana: it produces mana, puts a
/// land onto the battlefield, or adds mana as a spell.</param>
/// <param name="Draws">The card draws cards for its controller.</param>
/// <param name="TapMana">Every mana string one tap adds on the battlefield
/// ("{G}", "{C}{C}"). Empty when the face is no free tap source.</param>
/// <param name="HandMana">Every mana string the card adds when exiled from
/// hand for mana (Simian Spirit Guide). Empty on most cards.</param>
/// <param name="SpellMana">The mana a nonpermanent spell adds as it
/// resolves (Dark Ritual: "{B}{B}{B}"); null otherwise.</param>
/// <param name="Tutor">The card's library search; null when it has none.</param>
/// <param name="HasActivatedAbility">The face has an activated ability, a
/// mana ability included. A name pick avoids such a card, so a lock never
/// touches the deck's own cards (today's <c>NameWithoutSelfLock</c> rule).</param>
/// <param name="Response">What the face can answer in a response window
/// (ADR 0013); null when it has no counter and no protection grant.</param>
public sealed record CardFeatureProfile(
    bool Develops,
    bool Draws,
    IReadOnlyList<string> TapMana,
    IReadOnlyList<string> HandMana,
    string? SpellMana,
    TutorFeature? Tutor,
    bool HasActivatedAbility,
    ResponseFeature? Response = null)
{
    /// <summary>The profile of a card with no feature the pilot reads.</summary>
    public static readonly CardFeatureProfile None = new(false, false, [], [], null, null, false);

    /// <summary>The colors <see cref="TapMana"/> can make, as single symbols ("G", "C").</summary>
    public IReadOnlyList<string> ProducedColors
        => TapMana.SelectMany(ManaSymbols.Parse).SelectMany(s => s.Colors).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Compares the mana lists and the response feature by
    /// content. The generated record equality compares the lists by
    /// reference.</summary>
    public bool Equals(CardFeatureProfile? other)
        => other is not null
            && Develops == other.Develops
            && Draws == other.Draws
            && TapMana.SequenceEqual(other.TapMana, StringComparer.Ordinal)
            && HandMana.SequenceEqual(other.HandMana, StringComparer.Ordinal)
            && SpellMana == other.SpellMana
            && Tutor == other.Tutor
            && HasActivatedAbility == other.HasActivatedAbility
            && Response == other.Response;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Develops);
        hash.Add(Draws);
        foreach (var mana in TapMana)
        {
            hash.Add(mana, StringComparer.Ordinal);
        }

        hash.Add(TapMana.Count);
        foreach (var mana in HandMana)
        {
            hash.Add(mana, StringComparer.Ordinal);
        }

        hash.Add(HandMana.Count);
        hash.Add(SpellMana);
        hash.Add(Tutor);
        hash.Add(HasActivatedAbility);
        hash.Add(Response);
        return hash.ToHashCode();
    }
}

/// <summary>Where a search puts the card it finds.</summary>
public enum TutorDestination
{
    Hand,
    Battlefield,
    LibraryTop,
    Graveyard,
}

/// <summary>How the search starts: a cast, an activation from hand, an
/// activation on the battlefield (a fetch land), or a Saga chapter.</summary>
public enum TutorVia
{
    Cast,
    ActivateFromHand,
    ActivateOnBattlefield,

    /// <summary>A Saga's chapter ability (CR 714.2b): the search resolves
    /// when the Saga's lore counters reach the chapter, with no cost of its
    /// own (Urza's Saga, chapter III).</summary>
    SagaChapter,
}

/// <summary>A card's library search, as the pilot reads it.</summary>
/// <param name="Destination">Where the found card goes.</param>
/// <param name="EntersTapped">A search onto the battlefield puts the card
/// in tapped; false for every other destination.</param>
/// <param name="Via">How the search starts.</param>
/// <param name="ActivationCost">The mana cost of the activation in
/// Scryfall symbol form ("{1}"); null for a cast or a free activation.</param>
/// <param name="DelayTurns">The turns between the card entering and the
/// search resolving: chapter − 1 for a Saga chapter (a lore counter as the
/// Saga enters and one after each draw step, CR 714.3b), 0 for every other
/// search.</param>
public sealed record TutorFeature(TutorDestination Destination, bool EntersTapped, TutorVia Via, string? ActivationCost, int DelayTurns = 0);
