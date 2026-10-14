using System.Collections.Immutable;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Banlist;
using MtgArchive.Data;

namespace MtgArchive.Tests;

/// <summary>
/// Test-host endpoints registered through the service's
/// <see cref="IEndpointMapper"/> seam (<see cref="ArchiveApp"/> maps them;
/// the service assembly never sees them), so integration tests can assert
/// the shared conventions over the real HTTP pipeline before the domain
/// endpoints exist. <c>POST /_seam/versioned-echo</c> exercises the wire
/// machinery (snake_case, versions echo, citation shape, version-pin
/// resolution) without a database; its citation id is a fixture, so the
/// fetchable-id contract does not apply. <c>POST /_seam/candidates</c> is a
/// minimal index endpoint — the <see cref="CandidateQueryRequest"/> base,
/// <see cref="CandidateQuery.ResolveAsync"/>, structured predicates composed
/// onto <c>legal_candidates</c>, scoped availability, the
/// <c>candidates[*].card</c> response shape — so the no-illegal-candidates
/// harness has a real query path to
/// fuzz before the card search endpoints land, and a worked example of the
/// composition every index endpoint follows.
/// </summary>
public static class SeamProbe
{
    public static readonly ImmutableArray<string> KnownBanlistVersions = ["2025-04-21", "2025-09-22"];

    public sealed class Mapper : IEndpointMapper
    {
        public void Map(IEndpointRouteBuilder app)
        {
            app.MapPost("/_seam/versioned-echo", (ProbeRequest request) =>
            {
                var banlistVersion = VersionParameter.Resolve(
                    "banlist_version", request.BanlistVersion, KnownBanlistVersions[^1], KnownBanlistVersions);
                return TypedResults.Ok(new ProbeResponse(
                    new VersionsBlock { BanlistVersion = banlistVersion },
                    request.CollectionScope,
                    new Citation(CitationType.BanEvent, "probe-ban-event", "Seam probe citation")));
            });

            app.MapPost("/_seam/candidates", async (
                CandidateProbeRequest request,
                ArchiveDbContext db,
                BanlistVersionAxis banlist,
                CancellationToken cancellationToken) =>
            {
                var query = await CandidateQuery.ResolveAsync(db, banlist, request, cancellationToken);
                var candidates = query.Candidates;
                if (request.CardTypes is { Length: > 0 } types)
                {
                    candidates = candidates.Where(c => c.CardTypes.Any(t => types.Contains(t)));
                }

                if (request.NameContains is { } fragment)
                {
                    candidates = candidates.Where(c => c.Name.Contains(fragment));
                }

                var hits = await candidates
                    .OrderBy(c => c.Name).ThenBy(c => c.OracleId)
                    .Take(Math.Max(0, request.Limit))
                    .Select(c => new
                    {
                        c.OracleId,
                        c.Name,
                        Available = request.CollectionScope
                            ? ArchiveDbContext.AvailableCopies(c.CopyLimit, c.OracleId)
                            : null,
                    })
                    .ToListAsync(cancellationToken);
                return TypedResults.Ok(new CandidateProbeResponse(
                    [.. hits.Select(h => new ProbeCandidate(Citation.OfCard(h.OracleId, h.Name), h.Available))],
                    query.Versions));
            });
        }
    }

    public sealed record ProbeRequest
    {
        public string? BanlistVersion { get; init; }

        public bool CollectionScope { get; init; }
    }

    public sealed record ProbeResponse(VersionsBlock Versions, bool CollectionScope, Citation Citation);

    public sealed record CandidateProbeRequest : CandidateQueryRequest
    {
        public string[]? CardTypes { get; init; }

        public string? NameContains { get; init; }

        public int Limit { get; init; } = 50;
    }

    public sealed record CandidateProbeResponse(IReadOnlyList<ProbeCandidate> Candidates, VersionsBlock Versions);

    public sealed record ProbeCandidate(Citation Card, int? Available = null);
}
