namespace MtgArchive.Data;

/// <summary>
/// The ingest-derived record of how a card provides mana: its
/// produced colors (verbatim <c>produced_mana</c> — any card with a
/// non-empty array, land, rock, or dork alike), and/or the land types it can
/// fetch, parsed from the closed "search your library for a … land" template
/// family. For a land it also holds the enters-tapped state,
/// parsed from the land faces' closed "enters tapped" template family.
/// Every land has a row; a nonland has one only with a produced or fetch
/// facet. A card whose search-land or enters-tapped text is unmatched gets
/// no facet and an ingest-report entry instead — never a guess. Rebuilt every ingest into
/// the staging twin and published by <see cref="Ingest.DerivedStateSwap"/>;
/// downstream tier-1 code reads these rows and never re-parses Oracle text.
/// </summary>
public sealed class SourceProfile
{
    public required int CardId { get; set; }

    /// <summary>Verbatim <c>produced_mana</c>; null when the card produces nothing.</summary>
    public string[]? ProducedColors { get; set; }

    public const string FetchLandTypesContract =
        "Fetchable land TYPES only (empty = any land, with fetch_basic_only "
        + "for the Basic constraint); null = not a fetch. Color resolution "
        + "happens per deck at query time, never at ingest.";

    /// <summary>See <see cref="FetchLandTypesContract"/>.</summary>
    public string[]? FetchLandTypes { get; set; }

    /// <summary>True when the fetch is restricted to basic lands; false when
    /// <see cref="FetchLandTypes"/> is null.</summary>
    public bool FetchBasicOnly { get; set; }

    public const string EntersTappedContract =
        "Lands only: untapped, tapped, or conditional. The state parses from "
        + "the land faces' closed \"enters tapped\" template family. Null for "
        + "a nonland and for an unmatched land. Search-only: "
        + "the availability turn does not consume it.";

    /// <summary>See <see cref="EntersTappedContract"/>.</summary>
    public EntersTappedState? EntersTapped { get; set; }

    public const string FetchActivationContract =
        "Lands only: untapped or tapped, the state the found land enters in, "
        + "when the fetch is the closed \"{T}, [Pay N life,] Sacrifice this land: "
        + "Search your library for <targets>, put it onto the battlefield [tapped], "
        + "then shuffle\" template. Null for any other fetch shape: a mana cost, "
        + "a trigger, a search to hand, a spell. Tier 1 and the pilot read it; "
        + "XMage plays the land's own text.";

    /// <summary>See <see cref="FetchActivationContract"/>.</summary>
    public FetchActivation? FetchActivation { get; set; }

    public const string IdentityBoundContract =
        "True when every any-color ability of a five-color producer binds it "
        + "to the commander's color identity or to lands the players control. "
        + "Tier 1 clips the source's colors to the deck's color identity.";

    /// <summary>See <see cref="IdentityBoundContract"/>.</summary>
    public bool IdentityBound { get; set; }

    public const string OpponentDependentContract =
        "Nonland sources only: true when every mana-producing ability "
        + "and every fetch ability depends on another player or on combat. "
        + "Tier 1 has no opponents, so it lists the source as excluded and "
        + "does not count it.";

    /// <summary>See <see cref="OpponentDependentContract"/>.</summary>
    public bool OpponentDependent { get; set; }

    public const string TokenToAnotherPlayerContract =
        "Nonland sources only. This is true when every mana-producing ability "
        + "and every fetch ability creates its token for another player "
        + "(\"its controller creates\", \"target opponent creates\", a Gift). "
        + "Tier 1 lists the source as excluded and does not count it.";

    /// <summary>See <see cref="TokenToAnotherPlayerContract"/>.</summary>
    public bool TokenToAnotherPlayer { get; set; }
}

/// <summary>The state the found land enters in, for a land's closed
/// tap-and-sacrifice fetch activation (GLOSSARY.md "Fetch activation").</summary>
public enum FetchActivation
{
    Untapped,
    Tapped,
}

/// <summary>Whether a land enters tapped (GLOSSARY.md "Enters-tapped state").
/// A player-choice clause (shock lands, "you may have this land enter
/// tapped") is <see cref="Untapped"/>; a clause gated on game state is
/// <see cref="Conditional"/>.</summary>
public enum EntersTappedState
{
    Untapped,
    Tapped,
    Conditional,
}

/// <summary>The staging twin the source-profile phase rebuilds into.</summary>
public sealed class SourceProfileStaging
{
    public required int CardId { get; set; }

    public string[]? ProducedColors { get; set; }

    public string[]? FetchLandTypes { get; set; }

    public bool FetchBasicOnly { get; set; }

    public EntersTappedState? EntersTapped { get; set; }

    public FetchActivation? FetchActivation { get; set; }

    public bool IdentityBound { get; set; }

    public bool OpponentDependent { get; set; }

    public bool TokenToAnotherPlayer { get; set; }
}
