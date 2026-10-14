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
/// The no-illegal-candidates property test (ADR 0001) and its
/// color-identity extension, run by <see cref="IndexFuzzHarness"/> over
/// every index endpoint the host maps, against the corpus
/// <see cref="IndexCorpus"/> materialized through the real pipeline. Plus
/// the harness's own guarantees: it discovers endpoints by the request base
/// type, and it catches a query path that bypasses the boundary.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NoIllegalCandidatesTests(PostgresFixture postgres)
{
    private const int Seed = 36;

    [Fact]
    public async Task No_index_endpoint_returns_an_illegal_candidate()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        using var app = new ArchiveApp(autoMigrate: false, connectionString);

        var report = await IndexFuzzHarness.RunAsync(app, connectionString, Seed, alwaysColorIdentity: false);

        Assert.Contains("/_seam/candidates", report.Routes);
        Assert.True(report.Violations.Count == 0, report.Describe());
    }

    [Fact]
    public async Task No_index_endpoint_returns_a_color_identity_violation()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        using var app = new ArchiveApp(autoMigrate: false, connectionString);

        var report = await IndexFuzzHarness.RunAsync(app, connectionString, Seed, alwaysColorIdentity: true);

        Assert.Contains("/_seam/candidates", report.Routes);
        Assert.True(report.Violations.Count == 0, report.Describe());
    }

    [Fact]
    public void The_harness_discovers_index_endpoints_by_their_request_base_type()
    {
        using var app = new ArchiveApp();

        var discovered = IndexFuzzHarness.Discover(app);

        Assert.Contains(("/_seam/candidates", typeof(SeamProbe.CandidateProbeRequest)), discovered);
        Assert.DoesNotContain(discovered, d => d.Route == "/_seam/versioned-echo");
    }

    [Fact]
    public async Task The_harness_catches_a_query_path_that_bypasses_the_boundary()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        using var app = new ArchiveApp(autoMigrate: false, connectionString,
            services => services.AddSingleton<IEndpointMapper, BypassingProbe>());

        var report = await IndexFuzzHarness.RunAsync(app, connectionString, Seed, alwaysColorIdentity: true);

        Assert.NotEmpty(report.Violations);
        Assert.All(report.Violations, v => Assert.Equal("/_seam/candidates-bypass", v.Route));
        Assert.Contains(report.Violations, v => v.Detail.Contains("is not legal as"));
        Assert.Contains(report.Violations, v => v.Detail.Contains("is stale"));
        Assert.Contains(report.Violations, v => v.Detail.Contains("escapes the deck identity"));
        Assert.Contains(report.Violations, v => v.Detail.Contains("versions.format must echo 'commander'"));
        Assert.Contains(report.Violations, v => v.Detail.Contains("versions.legality_as_of must date a Brawl format"));
        Assert.Contains(report.Violations, v => v.Detail.Contains("versions.banlist_version must be absent in a Brawl format"));
    }

    /// <summary>A candidate query reading card rows directly — the bypass the boundary forbids.</summary>
    public sealed class BypassingProbe : IEndpointMapper
    {
        public void Map(IEndpointRouteBuilder app)
            => app.MapPost("/_seam/candidates-bypass", async (
                BypassRequest request,
                ArchiveDbContext db,
                BanlistVersionAxis banlist,
                CancellationToken cancellationToken) =>
            {
                var pin = await banlist.ResolveAsync(db, request.BanlistVersion, cancellationToken);
                var hits = await db.Cards
                    .OrderBy(c => c.Name)
                    .Select(c => new { c.OracleId, c.Name })
                    .ToListAsync(cancellationToken);
                return TypedResults.Ok(new SeamProbe.CandidateProbeResponse(
                    [.. hits.Select(h => new SeamProbe.ProbeCandidate(Citation.OfCard(h.OracleId, h.Name)))],
                    new VersionsBlock { BanlistVersion = pin?.Wire }));
            });

        public sealed record BypassRequest : CandidateQueryRequest;
    }
}
