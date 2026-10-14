using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;

namespace MtgArchive.Ingest;

/// <summary>
/// Derives the card feature profile of every card face
/// (<see cref="CardFeatureDerivation"/>, spec §5.1). It runs after the
/// source-profile phase, which sets the land-face colors it reads. The
/// XMage adapter reads the stored rows and never parses Oracle text at run
/// time. Every face gets a row. An unprofiled face keeps the features the
/// templates proved and gets an ingest-report entry that names its first
/// unmatched sentence. A face with a response feature or a response
/// sentence outside the families gets a response-list entry, profiled or
/// not. The phase rebuilds the full table into the staging
/// twin and publishes through <see cref="DerivedStateSwap"/>, so a rerun
/// ends in the same state.
/// </summary>
public sealed class CardFeaturePhase(ArchiveDbContext db) : IIngestPhase
{
    private static readonly DerivedStateSwap.SwappedTable[] SwappedTables =
    [
        new("card_feature_profile", Constraints: ["pk_card_feature_profile", "fk_card_feature_profile_card"], Indexes: []),
    ];

    public string Name => IngestPhases.CardFeatureDerive;

    public async Task RunAsync(IngestContext context, CancellationToken cancellationToken)
    {
        var cards = await db.Cards
            .OrderBy(c => c.Name).ThenBy(c => c.OracleId)
            .Select(c => new
            {
                c.Id,
                c.OracleId,
                c.Name,
                c.TypeLine,
                c.OracleText,
                c.ProducedMana,
                Faces = c.Faces.OrderBy(f => f.FaceIndex)
                    .Select(f => new CardFeatureDerivation.FaceRow(f.Name, f.TypeLine, f.OracleText, f.ProducedColors)).ToList(),
            })
            .ToListAsync(cancellationToken);

        await DerivedStateSwap.TruncateStagingAsync(db, SwappedTables, cancellationToken);
        foreach (var card in cards)
        {
            var faces = CardFeatureDerivation.FacesOf(card.Name, card.TypeLine, card.OracleText, card.ProducedMana, card.Faces);
            for (var faceIndex = 0; faceIndex < faces.Count; faceIndex++)
            {
                var result = CardFeatureDerivation.Derive(faces[faceIndex]);
                if (result.UnmatchedSentence is { } sentence)
                {
                    context.Report.UnprofiledCards.Add(new UnprofiledCard(card.OracleId, card.Name, faces[faceIndex].Name, sentence));
                }

                if (result.Profile.Response is not null || result.ResponseUnprofiled)
                {
                    context.Report.ResponseCards.Add(new ResponseCard(
                        card.OracleId, card.Name, faces[faceIndex].Name, Profiled: !result.ResponseUnprofiled, result.ResponseUnmatchedSentence));
                }

                db.Set<CardFeatureProfileRowStaging>().Add(StagingRow(card.Id, faceIndex, result));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await DerivedStateSwap.PublishAsync(db, SwappedTables, cancellationToken);
    }

    private static CardFeatureProfileRowStaging StagingRow(int cardId, int faceIndex, CardFeatureDerivation.Result result)
    {
        var profile = result.Profile;
        var counter = profile.Response?.Counter;
        var grants = profile.Response?.Grants ?? [];
        return new CardFeatureProfileRowStaging
        {
            CardId = cardId,
            FaceIndex = faceIndex,
            Develops = profile.Develops,
            Draws = profile.Draws,
            TapMana = [.. profile.TapMana],
            HandMana = [.. profile.HandMana],
            SpellMana = profile.SpellMana,
            TutorDestination = profile.Tutor?.Destination,
            TutorEntersTapped = profile.Tutor?.EntersTapped ?? false,
            TutorVia = profile.Tutor?.Via,
            TutorActivationCost = profile.Tutor?.ActivationCost,
            TutorDelayTurns = profile.Tutor?.DelayTurns ?? 0,
            HasActivatedAbility = profile.HasActivatedAbility,
            Unprofiled = result.Unprofiled,
            UnmatchedSentence = result.UnmatchedSentence,
            ResponseCounterReach = counter?.Reach,
            ResponseCounterTypes = [.. counter?.Types ?? []],
            ResponseCounterExcludedTypes = [.. counter?.ExcludedTypes ?? []],
            ResponseCounterColors = [.. counter?.Colors ?? []],
            ResponseCounterUnlessPays = counter?.UnlessPays,
            ResponseCounterLosesGame = counter?.LosesGame ?? false,
            ResponseGrants = [.. grants.Select(grant => grant.Grant)],
            ResponseGrantScopes = [.. grants.Select(grant => grant.Scope)],
            ResponseUnprofiled = result.ResponseUnprofiled,
            ResponseUnmatchedSentence = result.ResponseUnmatchedSentence,
        };
    }
}
