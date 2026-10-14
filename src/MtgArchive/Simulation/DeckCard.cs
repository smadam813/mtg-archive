using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;
using MtgArchive.Ingest;

namespace MtgArchive.Simulation;

/// <summary>
/// One resolved decklist card with everything tier-1 mana math reads: the
/// stored source profile and the typed card columns. Oracle text
/// is deliberately absent — profiles are the only mana-derivation input, and
/// nothing downstream re-parses text at query time.
/// </summary>
public sealed record DeckCard
{
    /// <summary>The card row id: what the derived tables key on. Zero on a
    /// card built outside the database, as the check corpus does.</summary>
    public int Id { get; init; }

    public required Guid OracleId { get; init; }

    public required string Name { get; init; }

    public required int Quantity { get; init; }

    public bool IsCommander { get; init; }

    public bool IsLand { get; init; }

    public bool IsBasic { get; init; }

    /// <summary>The front face stays on the battlefield: the pilot reads
    /// this to tell a permanent from a spell. On a <see cref="BackFace"/>,
    /// this and the other typed fields describe the back face.</summary>
    public bool IsPermanent { get; init; }

    /// <summary>Front-face card types ("Land", "Artifact", ...): what a
    /// <c>search_library</c> filter reads.</summary>
    public string[] CardTypes { get; init; } = [];

    public bool IsCreature => CardTypes.Contains("Creature", StringComparer.Ordinal);

    /// <summary>Front-face supertypes ("Basic", "Legendary", "Snow").</summary>
    public string[] Supertypes { get; init; } = [];

    public string[] Subtypes { get; init; } = [];

    public decimal ManaValue { get; init; }

    /// <summary>Front-face mana cost in Scryfall symbol form ("{1}{W}{W}").</summary>
    public string? ManaCost { get; init; }

    public string[]? ProducedColors { get; init; }

    /// <summary>See <see cref="SourceProfile.FetchLandTypesContract"/>.</summary>
    public string[]? FetchLandTypes { get; init; }

    public bool FetchBasicOnly { get; init; }

    /// <summary>See <see cref="SourceProfile.FetchActivationContract"/>.</summary>
    public FetchActivation? FetchActivation { get; init; }

    /// <summary>See <see cref="SourceProfile.IdentityBoundContract"/>.</summary>
    public bool IdentityBound { get; init; }

    /// <summary>See <see cref="SourceProfile.OpponentDependentContract"/>.</summary>
    public bool OpponentDependent { get; init; }

    /// <summary>See <see cref="SourceProfile.TokenToAnotherPlayerContract"/>.</summary>
    public bool TokenToAnotherPlayer { get; init; }

    /// <summary>The card's color identity. The commanders' color identities
    /// set the deck's color identity.</summary>
    public string[] ColorIdentity { get; init; } = [];

    /// <summary>The front face's own name on a multi-face card ("Esika,
    /// God of the Tree"); null on a single-face card.</summary>
    public string? FrontFaceName { get; init; }

    /// <summary>The back face of a modal double-faced card, with its own
    /// name, cost, mana value, types, and land-face colors; null on every
    /// other card. The engine casts or plays it through <see cref="AsBackFace"/>.</summary>
    public DeckCard? BackFace { get; init; }

    /// <summary>The whole card behind a back face; only <see cref="AsBackFace"/>
    /// sets it. Every zone but the stack and the battlefield holds the whole
    /// card, with its front-face characteristics (CR 712.8).</summary>
    public DeckCard? WholeCard { get; init; }

    /// <summary>The card as the library, hand, graveyard, and command zone hold it.</summary>
    public DeckCard Whole() => WholeCard ?? this;

    /// <summary>The card answers to a goal's card name: its full name, or
    /// the name of the face it shows.</summary>
    public bool Answers(string name) => name == Name || name == FrontFaceName || name == WholeCard?.Name;

    /// <summary>The back face as the stack and the battlefield see it. A
    /// land face taps for its own colors. Null without a back face.</summary>
    public DeckCard? AsBackFace() => BackFace is null ? null : BackFace with { WholeCard = this };

    /// <summary>A nonland front face with a land back face (<c>Shatterskull
    /// Smashing // Shatterskull, the Hammer Pass</c>).</summary>
    public bool IsMdfcLand => !IsLand && BackFace is { IsLand: true };

    /// <summary>A land on both faces (<c>Brightclimb Pathway // Grimclimb Pathway</c>).</summary>
    public bool IsPathway => IsLand && BackFace is { IsLand: true };

    /// <summary>The card has a land face: the land window, the land count,
    /// and tier 1 count it as a land.</summary>
    public bool HasLandFace => IsLand || IsMdfcLand;

    /// <summary>The faces a land drop may play: the card when it is a land,
    /// and its land back face.</summary>
    public IEnumerable<DeckCard> LandFaces()
    {
        if (IsLand)
        {
            yield return this;
        }

        if (BackFace is { IsLand: true })
        {
            yield return AsBackFace()!;
        }
    }

    /// <summary>Every color any face makes: tier 1 counts a card by all its faces.</summary>
    public IEnumerable<string> AnyFaceColors => (ProducedColors ?? []).Concat(BackFace?.ProducedColors ?? []);
}

public static class DeckCards
{
    /// <summary>Commanders first (quantity 1, flagged), then one row per
    /// distinct entry oracle_id with its summed quantity. Each group sorts
    /// by name, whatever the entry order.</summary>
    public static async Task<IReadOnlyList<DeckCard>> LoadAsync(
        ArchiveDbContext db, ResolvedDecklist decklist, CancellationToken cancellationToken = default)
    {
        var ids = decklist.Commanders.Concat(decklist.Entries.Select(e => e.OracleId)).Distinct().ToList();
        var cards = await db.Cards
            .Where(c => ids.Contains(c.OracleId))
            .Select(c => new
            {
                c.Id,
                c.OracleId,
                c.Name,
                c.ManaValue,
                c.ColorIdentity,
                // Front face only: the combined columns union every face, so a
                // spell // land MDFC would read as a land (see CanBeCommander).
                ManaCost = c.Faces.Where(f => f.FaceIndex == 0).Select(f => f.ManaCost).FirstOrDefault() ?? c.ManaCost,
                TypeLine = c.Faces.Where(f => f.FaceIndex == 0).Select(f => f.TypeLine).FirstOrDefault() ?? c.TypeLine,
                FrontFaceName = c.Faces.Where(f => f.FaceIndex == 0).Select(f => f.Name).FirstOrDefault(),
                FrontColors = c.Faces.Where(f => f.FaceIndex == 0).Select(f => f.ProducedColors).FirstOrDefault(),
                Back = c.Faces.Where(f => f.FaceIndex == 1 && c.Layout == "modal_dfc").FirstOrDefault(),
                Profile = db.SourceProfiles.Where(p => p.CardId == c.Id).FirstOrDefault(),
            })
            .ToDictionaryAsync(c => c.OracleId, cancellationToken);

        DeckCard Row(Guid oracleId, int quantity, bool isCommander)
        {
            var card = cards[oracleId];
            var row = Typed(
                new DeckCard
                {
                    Id = card.Id,
                    OracleId = oracleId,
                    Name = card.Name,
                    Quantity = quantity,
                    IsCommander = isCommander,
                    ManaValue = card.ManaValue,
                    ManaCost = card.ManaCost,
                    ProducedColors = card.Profile?.ProducedColors,
                    FetchLandTypes = card.Profile?.FetchLandTypes,
                    FetchBasicOnly = card.Profile?.FetchBasicOnly ?? false,
                    FetchActivation = card.Profile?.FetchActivation,
                    IdentityBound = card.Profile?.IdentityBound ?? false,
                    OpponentDependent = card.Profile?.OpponentDependent ?? false,
                    TokenToAnotherPlayer = card.Profile?.TokenToAnotherPlayer ?? false,
                    ColorIdentity = card.ColorIdentity,
                    FrontFaceName = card.FrontFaceName,
                },
                card.TypeLine);
            if (card.Back is not { } back)
            {
                return row;
            }

            var backFace = Typed(
                row with
                {
                    Name = back.Name,
                    FrontFaceName = null,
                    ManaCost = back.ManaCost,
                    ManaValue = back.ManaValue ?? 0,
                    ProducedColors = null,
                    FetchLandTypes = null,
                    FetchBasicOnly = false,
                    FetchActivation = null,
                    IdentityBound = false,
                    OpponentDependent = false,
                    TokenToAnotherPlayer = false,
                },
                back.TypeLine);
            if (!row.IsLand && !backFace.IsLand)
            {
                return row with { BackFace = backFace };
            }

            // With a land face, only a land face makes mana, and only its own
            // colors. The profile's union would make a spell face
            // a mana source and a pathway tap for both colors. A land face
            // keeps the union until an ingest fills its face colors.
            var union = card.Profile?.ProducedColors;
            return row with
            {
                ProducedColors = row.IsLand ? card.FrontColors ?? union : null,
                BackFace = backFace with { ProducedColors = backFace.IsLand ? back.ProducedColors ?? union : null },
            };
        }

        static DeckCard Typed(DeckCard card, string typeLine)
        {
            var (supertypes, cardTypes, subtypes) = CardDerivations.ParseTypeLine(typeLine);
            return card with
            {
                IsLand = cardTypes.Contains("Land"),
                IsBasic = supertypes.Contains("Basic"),
                IsPermanent = cardTypes.Any(CardDerivations.PermanentTypes.Contains),
                CardTypes = cardTypes,
                Supertypes = supertypes,
                Subtypes = subtypes,
            };
        }

        return
        [
            .. ByName(decklist.Commanders.Select(id => Row(id, 1, isCommander: true))),
            .. ByName(decklist.Entries.Select(e => e.OracleId).Distinct()
                .Select(id => Row(id, decklist.QuantityOf(id), isCommander: false))),
        ];
    }

    /// <summary>A fixed order, so no output follows the request's entry order.</summary>
    internal static IEnumerable<DeckCard> ByName(IEnumerable<DeckCard> cards)
        => cards.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.OracleId);
}
