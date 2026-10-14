using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using MtgArchive.Api;
using MtgArchive.Banlist;
using MtgArchive.Data;

namespace MtgArchive.Simulation;

/// <summary>
/// POST /deck-verdict: the deck verdict of one decklist in one format, with
/// no simulation (ADR 0012). The format defaults to Commander and resolves
/// through <see cref="FormatParameter"/>. A Commander verdict reads the
/// resolved banlist pin; a Brawl format takes no pin (a 400) and echoes its
/// legality as-of date instead. A reference that does not resolve refuses
/// the whole request with <c>unresolved_names</c>.
/// </summary>
public sealed class DeckVerdictEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/deck-verdict", async Task<Results<Ok<DeckVerdict>, ProblemHttpResult>> (
            DeckVerdictRequest request,
            ArchiveDbContext db,
            BanlistVersionAxis banlist,
            CancellationToken cancellationToken) =>
        {
            var format = FormatParameter.Resolve(request.Format);
            FormatParameter.RejectBanlistPin(format, request.BanlistVersion);

            ResolvedDecklist decklist;
            try
            {
                decklist = await new DecklistResolver(db).ResolveAsync(request.Decklist, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                return TypedResults.Problem(Problems.InvalidDecklist(ex.Message));
            }

            if (decklist.Unresolved.Count > 0)
            {
                return TypedResults.Problem(Problems.UnresolvedDecklist(decklist.Unresolved));
            }

            var (pin, versions) = await FormatParameter.ResolveDeckVersionsAsync(
                db, banlist, format, request.BanlistVersion, cancellationToken);
            var verdict = await DeckVerdict.ComputeAsync(db, decklist, format, pin, cancellationToken);
            return TypedResults.Ok(verdict with { Versions = versions, NameRewrites = decklist.WireRewrites });
        });
    }
}

/// <summary>POST /deck-verdict body: the decklist, the format, and, in
/// Commander only, the banlist version pin.</summary>
public sealed record DeckVerdictRequest
{
    public required Decklist Decklist { get; init; }

    [Description("commander (default), brawl, competitive_brawl, or standard_brawl. The versions block echoes it. An unknown format is a 400.")]
    public string? Format { get; init; }

    [Description("Commander only; latest by default. A Brawl format has no banlist version, so a pin with one is a 400.")]
    public string? BanlistVersion { get; init; }
}
