using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.Simulation;

/// <summary>
/// POST /tier1-report: resolve the decklist and the caller's categories in
/// one batch, load the deck cards with their stored source profiles, and
/// return the tier-1 report under the format's game rules. The
/// <c>versions</c> block echoes the format, Commander when the request
/// names none; an unknown format is a 400. A reference that does not
/// resolve refuses the whole request with <c>unresolved_names</c>.
/// </summary>
public sealed class Tier1Endpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/tier1-report", async Task<Results<Ok<Tier1Report>, ProblemHttpResult>> (
            Tier1ReportRequest request,
            ArchiveDbContext db,
            CancellationToken cancellationToken) =>
        {
            var format = FormatParameter.Resolve(request.Format);
            var maxTurn = request.MaxTurn ?? Tier1ReportRequest.DefaultMaxTurn;
            if (maxTurn < 1)
            {
                return TypedResults.Problem(Problems.ParameterBelowOne("max_turn", maxTurn));
            }

            var categories = request.Categories ?? [];
            foreach (var category in categories)
            {
                if (category.K is { } k && k < 1)
                {
                    return TypedResults.Problem(Problems.ParameterBelowOne("k", k));
                }
            }

            var categoryCards = categories.SelectMany(c => c.Cards).ToList();
            ResolvedDecklist decklist;
            DecklistLookup lookup;
            try
            {
                lookup = await new DecklistResolver(db).LookupAsync(
                    DecklistResolver.Cards(request.Decklist).Concat(categoryCards), cancellationToken);
                decklist = DecklistResolver.Resolve(request.Decklist, lookup);
            }
            catch (ArgumentException ex)
            {
                return TypedResults.Problem(Problems.InvalidDecklist(ex.Message));
            }

            var unresolved = decklist.Unresolved
                .Concat(categoryCards
                    .Where(card => lookup.Resolve(card) is null)
                    .Select(card => new UnresolvedCard(card, IsCommander: false)))
                .Distinct()
                .ToList();
            if (unresolved.Count > 0)
            {
                return TypedResults.Problem(Problems.UnresolvedDecklist(unresolved));
            }

            if (decklist.DeckSize > Hypergeometric.MaxDeckSize)
            {
                return TypedResults.Problem(Problems.InvalidDecklist(
                    $"Deck size {decklist.DeckSize} exceeds the {Hypergeometric.MaxDeckSize}-card limit."));
            }

            var deck = await DeckCards.LoadAsync(db, decklist, cancellationToken);
            var report = Tier1Report.Build(
                decklist,
                deck,
                [.. categories.Select(c => new ReportCategory(
                    c.Name, c.Cards.Select(card => lookup.Resolve(card)!.Value).ToHashSet(), c.K ?? 1))],
                maxTurn,
                GameRules.For(format));
            return TypedResults.Ok(report with
            {
                Versions = new VersionsBlock { Format = FormatParameter.WireName(format) },
                NameRewrites = decklist.WireRewrites,
            });
        });
    }
}

/// <summary>
/// POST /tier1-report body: a decklist, optional caller-defined categories,
/// and the turn horizon. A partial list is accepted: there is no size or
/// commander requirement. The service never invents a category.
/// </summary>
public sealed record Tier1ReportRequest
{
    public const int DefaultMaxTurn = 10;

    public required Decklist Decklist { get; init; }

    public IReadOnlyList<CategoryRequest>? Categories { get; init; }

    /// <summary>Last turn reported; default 10.</summary>
    public int? MaxTurn { get; init; }

    [Description("The format whose game rules fix the cards seen by each turn and the mulligan looks: "
        + "commander, brawl, competitive_brawl, or standard_brawl. Commander when omitted, and the versions block echoes it. "
        + "An unknown format is a 400, never a silent fallback.")]
    public string? Format { get; init; }
}

/// <summary>A caller-defined category: its name, member cards (same
/// reference shape as decklist cards), and the k for its draw rows
/// (default 1: "at least one").</summary>
public sealed record CategoryRequest
{
    public required string Name { get; init; }

    public required IReadOnlyList<DecklistCard> Cards { get; init; }

    public int? K { get; init; }
}
