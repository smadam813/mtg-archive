using Microsoft.AspNetCore.Http.HttpResults;
using MtgArchive.Api;

namespace MtgArchive.Simulation;

/// <summary>
/// POST /decklist-parse: deck text to the <c>decklist</c> body plus notes
/// (ADR 0012). Text only: it reads no database and resolves no names. A line
/// that fits no shape is the 400 <c>invalid-decklist</c> naming the line.
/// </summary>
public sealed class DecklistParseEndpoints : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/decklist-parse", Results<Ok<DecklistParseResponse>, ProblemHttpResult> (DecklistParseRequest request) =>
        {
            try
            {
                return TypedResults.Ok(DecklistText.Parse(request.Text ?? ""));
            }
            catch (FormatException ex)
            {
                return TypedResults.Problem(Problems.InvalidDecklist(ex.Message));
            }
        });
    }
}
