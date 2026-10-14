using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;

namespace MtgArchive.Ingest;

/// <summary>
/// Derives every card's source profile (<see cref="SourceProfileDerivation"/>)
/// from the corpus the card upsert phase just landed, so tier-1 queries and
/// card search read stored profiles and never re-parse Oracle text. Every
/// land gets a row; a nonland gets one only with a produced or fetch facet.
/// An unmatched card gets no facet from that parse and an ingest-report entry:
/// a search-land, an enters-tapped land, or an opponent or combat source. The phase never
/// guesses and never drops one silently. The phase rebuilds the full table into the staging twin and
/// publishes through <see cref="DerivedStateSwap"/>, so rerunning ingest is
/// the whole recovery path. The phase also sets the land-face colors of
/// modal double-faced cards (<see cref="DeriveFaceColorsAsync"/>).
/// </summary>
public sealed class SourceProfilePhase(ArchiveDbContext db) : IIngestPhase
{
    private static readonly DerivedStateSwap.SwappedTable[] SwappedTables =
    [
        new("source_profile", Constraints: ["pk_source_profile", "fk_source_profile_card"], Indexes: []),
    ];

    public string Name => IngestPhases.SourceProfileDerive;

    public async Task RunAsync(IngestContext context, CancellationToken cancellationToken)
    {
        var cards = await db.Cards
            .Select(c => new
            {
                c.Id,
                c.OracleId,
                c.Name,
                c.TypeLine,
                c.OracleText,
                c.ProducedMana,
                Faces = c.Faces.OrderBy(f => f.FaceIndex)
                    .Select(f => new SourceProfileDerivation.Face(f.Name, f.TypeLine, f.OracleText)).ToList(),
            })
            .ToListAsync(cancellationToken);

        await DerivedStateSwap.TruncateStagingAsync(db, SwappedTables, cancellationToken);
        foreach (var card in cards)
        {
            var text = SourceProfileDerivation.CombinedText(card.Faces.Select(f => f.OracleText).Prepend(card.OracleText));
            var result = SourceProfileDerivation.Derive(card.ProducedMana, text);
            if (result.UnmatchedSearchLand)
            {
                context.Report.UnmatchedSearchLands.Add(new UnmatchedSearchLand(card.OracleId, card.Name));
            }

            var entersTapped = SourceProfileDerivation.EntersTapped(
                SourceProfileDerivation.FacesOf(card.Name, card.TypeLine, card.OracleText, card.Faces));
            if (entersTapped.Unmatched)
            {
                context.Report.UnmatchedEntersTappedLands.Add(new UnmatchedEntersTappedLand(card.OracleId, card.Name));
            }

            if (result.UnmatchedOpponentSource && !entersTapped.Land)
            {
                context.Report.UnmatchedOpponentSources.Add(new UnmatchedOpponentSource(card.OracleId, card.Name));
            }

            if (entersTapped.Land || result.ProducedColors is not null || result.Fetch is not null)
            {
                db.Set<SourceProfileStaging>().Add(new SourceProfileStaging
                {
                    CardId = card.Id,
                    ProducedColors = result.ProducedColors,
                    FetchLandTypes = result.Fetch?.LandTypes,
                    FetchBasicOnly = result.Fetch?.BasicOnly ?? false,
                    FetchActivation = result.Fetch?.Activation,
                    EntersTapped = entersTapped.State,
                    IdentityBound = result.IdentityBound,
                    OpponentDependent = result.OpponentDependent && !entersTapped.Land,
                    TokenToAnotherPlayer = result.TokenToAnotherPlayer && !entersTapped.Land,
                });
            }
        }

        await DeriveFaceColorsAsync(context, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await DerivedStateSwap.PublishAsync(db, SwappedTables, cancellationToken);
    }

    /// <summary>
    /// Face colors: each land face of a modal double-faced card
    /// makes the <c>produced_mana</c> colors its own text adds. A land face
    /// that names none keeps the union and gets a report entry. The card
    /// upsert phase writes the face rows, so this phase sets the column on
    /// those rows. A rerun sets the same values and writes nothing.
    /// </summary>
    private async Task DeriveFaceColorsAsync(IngestContext context, CancellationToken cancellationToken)
    {
        var faces = await db.CardFaces
            .Join(db.Cards.Where(c => c.Layout == "modal_dfc"), f => f.CardId, c => c.Id, (f, c) => new { Face = f, c.Name, c.ProducedMana })
            .ToListAsync(cancellationToken);
        foreach (var row in faces)
        {
            if (!SourceProfileDerivation.IsLandFace(row.Face.TypeLine))
            {
                row.Face.ProducedColors = null;
                continue;
            }

            var colors = SourceProfileDerivation.FaceColors(row.ProducedMana ?? [], row.Face.OracleText);
            if (colors is null)
            {
                context.Report.Errors.Add($"Land face {row.Face.Name} of {row.Name} names no produced_mana color; it keeps the card's union.");
            }

            row.Face.ProducedColors = colors ?? row.ProducedMana;
        }
    }
}
