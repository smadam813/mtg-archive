using MtgArchive.Simulation.Engine;

namespace MtgArchive.Data;

/// <summary>
/// One card face's card feature profile (spec §5.1; glossary: Card feature
/// profile), as the <c>card_feature_derive</c> ingest phase derives it from
/// Oracle text with <see cref="Ingest.CardFeatureDerivation"/>. Every face
/// of every card has a row. Face 0 is the front face, or the whole card when
/// it has no face rows. Rebuilt every ingest into the staging twin and
/// published by <see cref="Ingest.DerivedStateSwap"/>. The XMage adapter
/// reads it through <see cref="CardFeatureProfiles"/> and never parses
/// Oracle text at run time.
/// </summary>
public sealed class CardFeatureProfileRow
{
    public required int CardId { get; set; }

    public required int FaceIndex { get; set; }

    public bool Develops { get; set; }

    public bool Draws { get; set; }

    public string[] TapMana { get; set; } = [];

    public string[] HandMana { get; set; } = [];

    public string? SpellMana { get; set; }

    /// <summary>Null when the face has no tutor. Then
    /// <see cref="TutorVia"/> and <see cref="TutorActivationCost"/> are null,
    /// <see cref="TutorEntersTapped"/> is false and
    /// <see cref="TutorDelayTurns"/> is 0.</summary>
    public TutorDestination? TutorDestination { get; set; }

    public bool TutorEntersTapped { get; set; }

    public TutorVia? TutorVia { get; set; }

    public string? TutorActivationCost { get; set; }

    public int TutorDelayTurns { get; set; }

    public bool HasActivatedAbility { get; set; }

    public const string UnprofiledContract =
        "True when a sentence of the face mentions draw, search or add and "
        + "matches no closed template. The row then holds only the features "
        + "the templates proved, unmatched_sentence names the first such "
        + "sentence, the ingest report lists the face, and engine coverage "
        + "lists its card.";

    /// <summary>See <see cref="UnprofiledContract"/>.</summary>
    public bool Unprofiled { get; set; }

    public string? UnmatchedSentence { get; set; }

    /// <summary>The response counter's reach (ADR 0013); null when the face
    /// counters nothing. Then the counter lists are empty, the amount is
    /// null and the game-loss flag is false.</summary>
    public CounterReach? ResponseCounterReach { get; set; }

    public string[] ResponseCounterTypes { get; set; } = [];

    public string[] ResponseCounterExcludedTypes { get; set; } = [];

    public string[] ResponseCounterColors { get; set; } = [];

    public string? ResponseCounterUnlessPays { get; set; }

    public bool ResponseCounterLosesGame { get; set; }

    /// <summary>The one-shot protection grants, in text order. The scope of
    /// each grant is at the same index in
    /// <see cref="ResponseGrantScopes"/>.</summary>
    public string[] ResponseGrants { get; set; } = [];

    public string[] ResponseGrantScopes { get; set; } = [];

    public const string ResponseUnprofiledContract =
        "True when a sentence of the face mentions an answer and matches no "
        + "response family. The row then holds only the response features the "
        + "families proved, response_unmatched_sentence names the first such "
        + "sentence, the ingest report lists the face, and the deck feature "
        + "profile lists its card among the unprofiled responses. Separate "
        + "from unprofiled.";

    /// <summary>See <see cref="ResponseUnprofiledContract"/>.</summary>
    public bool ResponseUnprofiled { get; set; }

    public string? ResponseUnmatchedSentence { get; set; }

    /// <summary>The row as the pilot reads it.</summary>
    public CardFeatureProfile ToProfile() => new(
        Develops,
        Draws,
        TapMana,
        HandMana,
        SpellMana,
        TutorDestination is { } destination
            ? new TutorFeature(
                destination,
                TutorEntersTapped,
                TutorVia ?? throw new InvalidOperationException($"card {CardId} face {FaceIndex}: a tutor destination with no tutor via"),
                TutorActivationCost,
                TutorDelayTurns)
            : null,
        HasActivatedAbility,
        ResponseOf());

    /// <summary>The stored response feature; null when the row holds no
    /// counter and no grant.</summary>
    private ResponseFeature? ResponseOf()
    {
        if (ResponseGrants.Length != ResponseGrantScopes.Length)
        {
            throw new InvalidOperationException(
                $"card {CardId} face {FaceIndex}: {ResponseGrants.Length} response grants with {ResponseGrantScopes.Length} scopes");
        }

        var counter = ResponseCounterReach is { } reach
            ? new CounterFeature(reach, ResponseCounterTypes, ResponseCounterExcludedTypes, ResponseCounterColors, ResponseCounterUnlessPays, ResponseCounterLosesGame)
            : null;
        IReadOnlyList<ProtectionGrant> grants = [.. ResponseGrants.Zip(ResponseGrantScopes, (grant, scope) => new ProtectionGrant(grant, scope))];
        return counter is null && grants.Count == 0 ? null : new ResponseFeature(counter, grants);
    }
}

/// <summary>The staging twin the card-feature phase rebuilds into.</summary>
public sealed class CardFeatureProfileRowStaging
{
    public required int CardId { get; set; }

    public required int FaceIndex { get; set; }

    public bool Develops { get; set; }

    public bool Draws { get; set; }

    public string[] TapMana { get; set; } = [];

    public string[] HandMana { get; set; } = [];

    public string? SpellMana { get; set; }

    public TutorDestination? TutorDestination { get; set; }

    public bool TutorEntersTapped { get; set; }

    public TutorVia? TutorVia { get; set; }

    public string? TutorActivationCost { get; set; }

    public int TutorDelayTurns { get; set; }

    public bool HasActivatedAbility { get; set; }

    public bool Unprofiled { get; set; }

    public string? UnmatchedSentence { get; set; }

    public CounterReach? ResponseCounterReach { get; set; }

    public string[] ResponseCounterTypes { get; set; } = [];

    public string[] ResponseCounterExcludedTypes { get; set; } = [];

    public string[] ResponseCounterColors { get; set; } = [];

    public string? ResponseCounterUnlessPays { get; set; }

    public bool ResponseCounterLosesGame { get; set; }

    public string[] ResponseGrants { get; set; } = [];

    public string[] ResponseGrantScopes { get; set; } = [];

    public bool ResponseUnprofiled { get; set; }

    public string? ResponseUnmatchedSentence { get; set; }
}
