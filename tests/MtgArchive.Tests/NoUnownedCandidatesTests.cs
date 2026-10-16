using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MtgArchive.Api;
using MtgArchive.Banlist;
using MtgArchive.Data;

namespace MtgArchive.Tests;

/// <summary>
/// The ownership property, sibling of
/// <see cref="NoIllegalCandidatesTests"/>: no collection-scoped query path
/// returns an unowned candidate. The same <see cref="IndexFuzzHarness"/>
/// runs it over every index endpoint the host maps, with
/// <c>collection_scope</c> forced on, against the corpus collection
/// <see cref="IndexCorpus"/> imported through the real
/// <c>PUT /collection</c> — so an endpoint landing later is under this
/// property the moment it maps.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NoUnownedCandidatesTests(PostgresFixture postgres)
{
    // The seed is arbitrary, but each run must exercise every endpoint.
    // Seed 43 drew only empty /cards/search answers in the self-check once
    // Standard joined the format axis.
    private const int Seed = 44;

    [Fact]
    public async Task No_collection_scoped_index_endpoint_returns_an_unowned_candidate()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        using var app = new ArchiveApp(autoMigrate: false, connectionString);

        var report = await IndexFuzzHarness.RunAsync(
            app, connectionString, Seed, alwaysColorIdentity: false, alwaysCollectionScope: true);

        Assert.Contains("/_seam/candidates", report.Routes);
        Assert.True(report.Violations.Count == 0, report.Describe());
    }

    [Fact]
    public async Task The_harness_catches_a_query_path_that_ignores_scope()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        using var app = new ArchiveApp(autoMigrate: false, connectionString,
            services => services.AddSingleton<IEndpointMapper, ScopeIgnoringProbe>());

        var report = await IndexFuzzHarness.RunAsync(
            app, connectionString, Seed, alwaysColorIdentity: false, alwaysCollectionScope: true);

        Assert.NotEmpty(report.Violations);
        Assert.All(report.Violations, v => Assert.Equal("/_seam/candidates-unscoped", v.Route));
        Assert.All(report.Violations, v => Assert.Contains("is not owned", v.Detail));
    }

    /// <summary>
    /// A legality-honouring query that drops the caller's scope — the one
    /// escape the ownership property must catch on its own.
    /// </summary>
    public sealed class ScopeIgnoringProbe : IEndpointMapper
    {
        public void Map(IEndpointRouteBuilder app)
            => app.MapPost("/_seam/candidates-unscoped", async (
                UnscopedRequest request,
                ArchiveDbContext db,
                BanlistVersionAxis banlist,
                CancellationToken cancellationToken) =>
            {
                var query = await CandidateQuery.ResolveAsync(
                    db, banlist, request with { CollectionScope = false }, cancellationToken);
                var hits = await query.Candidates
                    .OrderBy(c => c.Name)
                    .Select(c => new { c.OracleId, c.Name })
                    .ToListAsync(cancellationToken);
                return TypedResults.Ok(new SeamProbe.CandidateProbeResponse(
                    [.. hits.Select(h => new SeamProbe.ProbeCandidate(Citation.OfCard(h.OracleId, h.Name)))],
                    query.Versions));
            });

        public sealed record UnscopedRequest : CandidateQueryRequest;
    }
}
