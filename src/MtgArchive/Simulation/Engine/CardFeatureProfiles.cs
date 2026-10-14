using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;

namespace MtgArchive.Simulation.Engine;

/// <summary>
/// A deck's stored card feature profiles (spec §5.1, §5.2).
/// </summary>
/// <param name="ByFace">The profile of each face, by the face's own name;
/// a single-face card's face is its card name.</param>
/// <param name="Unprofiled">The full decklist names of the cards with an
/// unprofiled face or with no stored row, in ordinal order. Engine coverage
/// reports them.</param>
/// <param name="ResponseCards">The full decklist names of the response
/// cards (glossary): the cards with a face whose response feature can
/// answer a disruption card, in ordinal order. A counter qualifies when its
/// reach covers a card of <see cref="DisruptionCards"/>, so a creature-only
/// or a red-only counter does not. An interactive envelope names them
/// (ADR 0013).</param>
/// <param name="UnprofiledResponses">The full decklist names of the cards
/// with a response-unprofiled face, in ordinal order. They make the answered
/// rate a lower bound.</param>
public sealed record DeckFeatureProfiles(
    IReadOnlyDictionary<string, CardFeatureProfile> ByFace,
    IReadOnlyList<string> Unprofiled,
    IReadOnlyList<string> ResponseCards,
    IReadOnlyList<string> UnprofiledResponses)
{
    /// <summary>A deck with no response card and no unprofiled response: a
    /// frozen parity deck, or a test deck.</summary>
    public DeckFeatureProfiles(IReadOnlyDictionary<string, CardFeatureProfile> byFace, IReadOnlyList<string> unprofiled)
        : this(byFace, unprofiled, [], [])
    {
    }

    /// <summary>The profile of the face a deck card shows: the front face,
    /// or the back face that <see cref="DeckCard.AsBackFace"/> returns.
    /// <see cref="CardFeatureProfile.None"/> for a face with no stored row.</summary>
    public CardFeatureProfile Of(DeckCard face)
        => ByFace.GetValueOrDefault(face.FrontFaceName ?? face.Name, CardFeatureProfile.None);
}

/// <summary>
/// Loads a deck's card feature profiles from <c>card_feature_profile</c>.
/// The XMage adapter (phase 2) calls it once per batch, joins the profiles
/// to the pilot view by card name, and reports the unprofiled cards in
/// engine coverage.
/// </summary>
public static class CardFeatureProfiles
{
    public static async Task<DeckFeatureProfiles> LoadAsync(
        ArchiveDbContext db, IReadOnlyList<DeckCard> deck, CancellationToken cancellationToken)
    {
        var ids = deck.Select(card => card.Id).Distinct().ToList();
        var rows = (await db.CardFeatureProfileRows
                .Where(row => ids.Contains(row.CardId))
                .ToListAsync(cancellationToken))
            .ToLookup(row => row.CardId);

        var byFace = new Dictionary<string, CardFeatureProfile>(StringComparer.Ordinal);
        var unprofiled = new SortedSet<string>(StringComparer.Ordinal);
        var responseCards = new SortedSet<string>(StringComparer.Ordinal);
        var unprofiledResponses = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var card in deck)
        {
            var faces = rows[card.Id].ToDictionary(row => row.FaceIndex);
            var profiles = faces.ToDictionary(face => face.Key, face => face.Value.ToProfile());

            // A face with no row was never derived: the phase has not run
            // since the card or its back face arrived. Its card is
            // unprofiled, never featureless.
            if (!faces.TryGetValue(0, out var front)
                || (card.BackFace is not null && !faces.ContainsKey(1))
                || faces.Values.Any(row => row.Unprofiled))
            {
                unprofiled.Add(card.Name);
            }

            if (faces.Values.Any(row => row.ResponseUnprofiled))
            {
                unprofiledResponses.Add(card.Name);
            }

            if (front is not null)
            {
                byFace[card.FrontFaceName ?? card.Name] = profiles[0];
            }

            if (card.BackFace is { } back && profiles.TryGetValue(1, out var backProfile))
            {
                byFace[back.Name] = backProfile;
            }

            if (profiles.Values.Any(profile => profile.Response is { } response && DisruptionCards.AnswersAny(response)))
            {
                responseCards.Add(card.Name);
            }
        }

        return new DeckFeatureProfiles(byFace, [.. unprofiled], [.. responseCards], [.. unprofiledResponses]);
    }
}
