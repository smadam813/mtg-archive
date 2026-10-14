using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;
using Npgsql;

namespace MtgArchive.Tests;

/// <summary>
/// The legality boundary itself: <c>legal_candidates</c> as
/// composed through <see cref="ArchiveDbContext.LegalCandidates"/> — exactly
/// the legal, non-stale set per (version, role), the color-identity mask,
/// structured filters composing into one inlined statement over the
/// legality index — and the shared request resolution every index endpoint
/// starts from (<c>POST /_seam/candidates</c>): latest by default, pins
/// echoed, unknown pins and bad color letters refused as problem 400s.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LegalCandidatesTests(PostgresFixture postgres)
{
    private static readonly Role[] Roles = [Role.Commander, Role.Companion, Role.Main];

    /// <summary>
    /// Either shape the planner uses for the legality index: a plain
    /// <c>Index Scan using ix_legality_version_format_role on legality</c>, or a
    /// <c>Bitmap Index Scan on ix_legality_version_format_role</c> feeding a
    /// <c>Bitmap Heap Scan on legality</c>.
    /// </summary>
    private static readonly Regex LegalityIndexPath = new(
        @"Index (Only )?Scan using ix_legality_version_format_role on legality|Bitmap Heap Scan on legality[\s\S]*Bitmap Index Scan on ix_legality_version_format_role");

    [Fact]
    public async Task Legal_candidates_is_exactly_the_legal_non_stale_set_per_version_and_role()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        await using var db = PostgresFixture.CreateContext(connectionString);

        var sets = new HashSet<string>();
        foreach (var version in oracle.Versions)
        {
            foreach (var role in Roles)
            {
                var expected = oracle.Legal(version, role);
                var actual = await db.LegalCandidates(Format.Commander, DateOnly.Parse(version), role)
                    .Select(c => c.OracleId)
                    .ToListAsync();
                Assert.Equal(expected.Order(), actual.Order());
                Assert.NotEmpty(actual);
                sets.Add(string.Join(",", expected.Order()));
            }
        }

        // The corpus makes the sets differ across versions and roles, so the
        // equality above is not vacuous.
        Assert.True(sets.Count > 1);
        Assert.Contains(oracle.OracleIds, id => !oracle.Legal(oracle.Latest, Role.Main).Contains(id));
    }

    [Fact]
    public async Task An_unreplayed_date_or_no_version_at_all_yields_no_candidates()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        await using var db = PostgresFixture.CreateContext(connectionString);

        Assert.Empty(await db.LegalCandidates(Format.Commander, new DateOnly(1999, 1, 1), Role.Main).ToListAsync());
        Assert.Empty(await db.LegalCandidates(Format.Commander, null, Role.Main).ToListAsync());
    }

    // A Brawl format has one current state, read with no
    // version; a version date addresses nothing in it.
    [Theory]
    [InlineData(Format.Brawl)]
    [InlineData(Format.CompetitiveBrawl)]
    [InlineData(Format.StandardBrawl)]
    public async Task A_brawl_format_is_exactly_its_legal_non_stale_set_per_role(Format format)
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        await using var db = PostgresFixture.CreateContext(connectionString);

        foreach (var role in Roles)
        {
            var expected = oracle.Legal(null, role, format);
            var actual = await db.LegalCandidates(format, null, role).Select(c => c.OracleId).ToListAsync();
            Assert.Equal(expected.Order(), actual.Order());
            Assert.Empty(await db.LegalCandidates(format, DateOnly.Parse(oracle.Latest), role).ToListAsync());
        }

        Assert.NotEmpty(oracle.Legal(null, Role.Main, format));
        Assert.NotEqual(oracle.Legal(oracle.Latest, Role.Main).Order(), oracle.Legal(null, Role.Main, format).Order());
    }

    [Fact]
    public async Task Color_identity_mask_admits_exactly_the_identities_inside_the_deck()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        await using var db = PostgresFixture.CreateContext(connectionString);
        var latest = DateOnly.Parse(oracle.Latest);

        const int azorius = 16 | 8;
        var inAzorius = await db.LegalCandidates(Format.Commander, latest, Role.Main, azorius).Select(c => c.OracleId).ToListAsync();
        var expected = oracle.Legal(oracle.Latest, Role.Main)
            .Where(id => oracle.IdentityOf(id).All(c => c is "W" or "U"));
        Assert.Equal(expected.Order(), inAzorius.Order());
        Assert.NotEmpty(inAzorius);

        // A colorless deck admits colorless cards only.
        var colorless = await db.LegalCandidates(Format.Commander, latest, Role.Main, 0).Select(c => c.OracleId).ToListAsync();
        Assert.NotEmpty(colorless);
        Assert.All(colorless, id => Assert.Empty(oracle.IdentityOf(id)));
    }

    [Fact]
    public async Task Structured_filters_compose_into_one_inlined_statement_over_the_legality_index()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        await using var db = PostgresFixture.CreateContext(connectionString);
        var latest = DateOnly.Parse(oracle.Latest);

        var creatures = await db.LegalCandidates(Format.Commander, latest, Role.Commander)
            .Where(c => c.CardTypes.Contains("Creature"))
            .Select(c => c.OracleId)
            .ToListAsync();
        Assert.NotEmpty(creatures);
        Assert.All(creatures, id => Assert.Contains(id, oracle.Legal(oracle.Latest, Role.Commander)));

        // The function is inlined (no Function Scan), so the planner reaches
        // legality through its index with the caller's predicate in the
        // same plan. Seq scans are disabled only because the corpus is
        // tiny; the assertion is that the index path exists.
        //
        // ANALYZE first, so the plan does not depend on whether autovacuum
        // has run yet. Without statistics the planner guesses
        // one legality row and picks a plain Index Scan. With them it
        // estimates about twenty and picks a Bitmap Index Scan instead.
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using (var analyze = new NpgsqlCommand("ANALYZE card; ANALYZE legality; ANALYZE banlist_version", conn))
        {
            await analyze.ExecuteNonQueryAsync();
        }

        await using var transaction = await conn.BeginTransactionAsync();
        await using (var disableSeqScan = new NpgsqlCommand("SET LOCAL enable_seqscan = off", conn, transaction))
        {
            await disableSeqScan.ExecuteNonQueryAsync();
        }

        // Commander at a version, and a Brawl format with no version
        // both reach legality through the same index.
        foreach (var (format, version) in new[] { ("commander", (DateOnly?)latest), ("brawl", null) })
        {
            await using var cmd = new NpgsqlCommand(
                """
                EXPLAIN SELECT oracle_id FROM legal_candidates($1, $2, 'commander', NULL)
                WHERE card_types @> ARRAY['Creature']
                """, conn, transaction);
            cmd.Parameters.AddWithValue(format);
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Date, Value = (object?)version ?? DBNull.Value });
            var plan = new List<string>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    plan.Add(reader.GetString(0));
                }
            }

            var text = string.Join('\n', plan);
            Assert.False(text.Contains("Function Scan"), $"legal_candidates({format}) was not inlined:\n{text}");
            Assert.True(
                LegalityIndexPath.IsMatch(text),
                $"the {format} plan does not reach legality through ix_legality_version_format_role:\n{text}");
        }
    }

    [Fact]
    public async Task Candidate_queries_default_to_latest_echo_pins_and_refuse_bad_pins_and_colors()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        using var app = new ArchiveApp(autoMigrate: false, connectionString);
        using var client = app.CreateClient();

        var latest = await PostAsync(client, new { });
        Assert.Equal(oracle.Latest, latest.GetProperty("versions").GetProperty("banlist_version").GetString());
        Assert.NotEmpty(latest.GetProperty("candidates").EnumerateArray());

        var pinned = await PostAsync(client, new { banlist_version = "2004-10-04", role = "commander" });
        Assert.Equal("2004-10-04", pinned.GetProperty("versions").GetProperty("banlist_version").GetString());
        Assert.Equal(
            oracle.Legal("2004-10-04", Role.Commander).Order(),
            pinned.GetProperty("candidates").EnumerateArray()
                .Select(c => Guid.Parse(c.GetProperty("card").GetProperty("id").GetString()!)).Order());

        var unknown = await client.PostAsJsonAsync("/_seam/candidates", new { banlist_version = "1999-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
        using (var problem = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync()))
        {
            Assert.Equal("urn:mtg-archive:problem:unknown-version", problem.RootElement.GetProperty("type").GetString());
            Assert.Equal("banlist_version", problem.RootElement.GetProperty("parameter").GetString());
            Assert.Equal("1999-01-01", problem.RootElement.GetProperty("value").GetString());
        }

        var badColors = await client.PostAsJsonAsync("/_seam/candidates", new { color_identity = new[] { "W", "X", "q", "X" } });
        Assert.Equal(HttpStatusCode.BadRequest, badColors.StatusCode);
        Assert.Equal("application/problem+json", badColors.Content.Headers.ContentType?.MediaType);
        using (var problem = JsonDocument.Parse(await badColors.Content.ReadAsStringAsync()))
        {
            Assert.Equal("urn:mtg-archive:problem:invalid-color-identity",
                problem.RootElement.GetProperty("type").GetString());
            Assert.Equal(["X", "q"], problem.RootElement.GetProperty("unknown").EnumerateArray()
                .Select(u => u.GetString()).ToArray());
        }

        // An unknown role literal fails body binding as a problem 400.
        var badRole = await client.PostAsJsonAsync("/_seam/candidates", new { role = "planeswalker" });
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);
        Assert.Equal("application/problem+json", badRole.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/_seam/candidates", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
