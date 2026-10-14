namespace MtgArchive.Simulation.Engine;

/// <summary>
/// The engine-neutral game state that the pilot policy reads, and the only
/// one (glossary: Pilot view). An engine adapter builds a new view for each
/// decision, so a view never changes under the pilot. It holds what the
/// player knows: the zones, the mana, the resolved casts, what each search
/// in play can find now, and the legal actions. It never holds the order of
/// the unseen library or a disruption profile (ADR 0003, ADR 0005).
/// </summary>
/// <param name="Turn">The current turn, 1 on the first.</param>
/// <param name="Step">The step the decision comes in.</param>
/// <param name="Life">The life total of the player.</param>
/// <param name="LandDropUsed">The land drop of the turn is spent.</param>
/// <param name="Mana">The pool and the sources that can pay now.</param>
/// <param name="Hand">The hand, in the engine's order.</param>
/// <param name="Battlefield">The permanents, in the engine's order.</param>
/// <param name="Graveyard">The graveyard, oldest first.</param>
/// <param name="Exile">The exile zone, oldest first.</param>
/// <param name="CommandZone">The command zone. The cast action of a
/// commander carries its cost with the tax.</param>
/// <param name="ResolvedCasts">Every cast that resolved, in order.</param>
/// <param name="Finds">For each card in hand, in the command zone, on the
/// battlefield, or in the pick of the decision that has a library search:
/// the library cards that search can find now, keyed by the name of the
/// card that searches. The lists come in no promised order; a policy must
/// not depend on it.</param>
/// <param name="Actions">The legal actions of a main-phase decision, in the
/// engine's order; empty for every other decision. Pass is implicit.</param>
/// <param name="FeatureLookup">The card feature profile of a face; read it
/// through <see cref="Features"/>.</param>
public sealed record PilotView(
    int Turn,
    TurnStep Step,
    int Life,
    bool LandDropUsed,
    ManaBoard Mana,
    IReadOnlyList<DeckCard> Hand,
    IReadOnlyList<ViewPermanent> Battlefield,
    IReadOnlyList<DeckCard> Graveyard,
    IReadOnlyList<DeckCard> Exile,
    IReadOnlyList<DeckCard> CommandZone,
    IReadOnlyList<ResolvedCast> ResolvedCasts,
    IReadOnlyDictionary<string, IReadOnlyList<DeckCard>> Finds,
    IReadOnlyList<EngineAction> Actions,
    Func<DeckCard, CardFeatureProfile> FeatureLookup) : IGoalBoard
{
    /// <summary>The card feature profile of a face, joined by its name.</summary>
    public CardFeatureProfile Features(DeckCard face) => FeatureLookup(face);

    public IEnumerable<DeckCard> BattlefieldCards => Battlefield.Select(p => p.Card);
}

/// <summary>A permanent in the pilot view. <see cref="Id"/> is stable
/// within one decision; <see cref="Card"/> is the face it entered as.</summary>
public sealed record ViewPermanent(string Id, DeckCard Card, bool Tapped);

/// <summary>What a legal action does.</summary>
public enum EngineActionKind
{
    /// <summary>The land drop of the turn, on the face <see cref="EngineAction.Face"/> names.</summary>
    PlayLand,

    /// <summary>A cast from hand.</summary>
    Cast,

    /// <summary>A cast from the command zone, tax included in the cost.</summary>
    CastCommander,

    /// <summary>An activated ability of a permanent.</summary>
    Activate,
}

/// <summary>One legal action in the pilot view.</summary>
/// <param name="Id">Stable within the decision; the pilot answers with it.</param>
/// <param name="Kind">What the action does.</param>
/// <param name="Face">The face played or cast, or the card of the permanent
/// for an activation.</param>
/// <param name="Cost">The full mana cost, commander tax included; empty for
/// a land drop.</param>
/// <param name="PermanentId">The id of the activated permanent; null
/// otherwise.</param>
/// <param name="Features">What the action does: the profile of the face for
/// a land drop or a cast, the features of the ability itself for an
/// activation.</param>
/// <param name="ManaAdded">The mana the action adds once it resolves: one
/// tap of the permanent a land drop or a cast puts onto the battlefield,
/// the mana a spell adds, or the mana a mana ability adds. Empty for any
/// other activation.</param>
/// <param name="SpendsSource">An activation taps or sacrifices its permanent.</param>
public sealed record EngineAction(
    string Id,
    EngineActionKind Kind,
    DeckCard Face,
    ManaCost Cost,
    string? PermanentId,
    CardFeatureProfile Features,
    IReadOnlyList<string> ManaAdded,
    bool SpendsSource);
