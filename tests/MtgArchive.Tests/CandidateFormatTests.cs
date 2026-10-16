using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MtgArchive.Tests;

/// <summary>
/// The format dimension on candidate queries (ADR 0012):
/// <c>format</c> is a body field, a missing format means Commander, the
/// versions block echoes the format used, and an unknown format is a 400
/// that names the value — never a silent fallback. A Brawl format has no
/// banlist version: a pin with one is a 400, and the versions
/// block dates it with <c>legality_as_of</c> instead. Standard, the 60-card
/// constructed format, is unversioned the same way. Driven over HTTP against the corpus
/// <see cref="IndexCorpus"/> ingests through the real pipeline.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CandidateFormatTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_request_with_no_format_is_commander_and_echoes_it()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        using var app = new ArchiveApp(autoMigrate: false, connectionString);
        using var client = app.CreateClient();

        var unnamed = await PostAsync(client, "/cards/search", new { role = "commander", limit = 200 });
        var named = await PostAsync(client, "/cards/search", new { role = "commander", limit = 200, format = "commander" });

        Assert.Equal("commander", unnamed.GetProperty("versions").GetProperty("format").GetString());
        Assert.Equal("commander", named.GetProperty("versions").GetProperty("format").GetString());
        Assert.NotEmpty(CandidateIds(unnamed));
        Assert.Equal(CandidateIds(unnamed), CandidateIds(named));
    }

    [Fact]
    public void Standard_resolves_to_the_standard_format()
    {
        Assert.Equal(Data.Format.Standard, Api.FormatParameter.Resolve("standard"));
        Assert.False(Api.FormatParameter.IsVersioned(Data.Format.Standard));
    }

    [Theory]
    [InlineData("/cards/search", "modern")]
    [InlineData("/cards/search", "Standard")]
    [InlineData("/cards/search", "Commander")]
    [InlineData("/cards/search", "historic_brawl")]
    [InlineData("/cards/semantic-search", "competitivebrawl")]
    public async Task An_unknown_format_is_a_400_that_names_the_value(string route, string format)
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        using var app = new ArchiveApp(autoMigrate: false, connectionString);
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(route, new { query = "draw a card", format });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:mtg-archive:problem:unknown-format", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("format", problem.RootElement.GetProperty("parameter").GetString());
        Assert.Equal(format, problem.RootElement.GetProperty("value").GetString());
        Assert.Contains($"'{format}'", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(
            ["commander", "brawl", "competitive_brawl", "standard_brawl", "standard"],
            problem.RootElement.GetProperty("known").EnumerateArray().Select(k => k.GetString()!));
    }

    [Theory]
    [InlineData("/cards/search", "brawl")]
    [InlineData("/cards/search", "competitive_brawl")]
    [InlineData("/cards/semantic-search", "standard_brawl")]
    [InlineData("/cards/search", "standard")]
    [InlineData("/cards/semantic-search", "standard")]
    public async Task A_brawl_format_with_a_banlist_version_pin_is_a_400(string route, string format)
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        using var app = new ArchiveApp(autoMigrate: false, connectionString);
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(route, new { query = "draw a card", format, banlist_version = oracle.Latest });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:mtg-archive:problem:unversioned-format", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("banlist_version", problem.RootElement.GetProperty("parameter").GetString());
        Assert.Equal(format, problem.RootElement.GetProperty("format").GetString());
    }

    [Theory]
    [InlineData("brawl")]
    [InlineData("competitive_brawl")]
    [InlineData("standard_brawl")]
    [InlineData("standard")]
    public async Task A_brawl_response_carries_legality_as_of_and_no_banlist_version(string format)
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        using var app = new ArchiveApp(autoMigrate: false, connectionString);
        using var client = app.CreateClient();

        var response = await PostAsync(client, "/cards/search", new { role = "main", limit = 200, format });

        var versions = response.GetProperty("versions");
        Assert.Equal(format, versions.GetProperty("format").GetString());
        Assert.False(versions.TryGetProperty("banlist_version", out _));
        // FakeBulkSource's Oracle Cards stamp at the corpus's second ingest:
        // 2026-01-01 plus one second per fixture write, two writes.
        Assert.Equal(
            new DateTimeOffset(2026, 1, 1, 0, 0, 2, TimeSpan.Zero),
            versions.GetProperty("legality_as_of").GetDateTimeOffset());
        Assert.NotEmpty(CandidateIds(response));
    }

    // Standard has no commander or companion seat, so those roles hold no
    // candidate, and the main pool is exactly the corpus's Standard-legal set.
    [Fact]
    public async Task A_standard_query_returns_the_standard_pool_and_no_commanders()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        using var app = new ArchiveApp(autoMigrate: false, connectionString);
        using var client = app.CreateClient();

        var main = await PostAsync(client, "/cards/search", new { role = "main", limit = 200, format = "standard" });
        var commanders = await PostAsync(client, "/cards/search", new { role = "commander", limit = 200, format = "standard" });
        var companions = await PostAsync(client, "/cards/search", new { role = "companion", limit = 200, format = "standard" });

        Assert.Equal(
            oracle.Legal(null, Data.Role.Main, Data.Format.Standard).Order(),
            CandidateIds(main).Order());
        Assert.NotEqual(
            oracle.Legal(null, Data.Role.Main, Data.Format.StandardBrawl).Order(),
            CandidateIds(main).Order());
        Assert.Empty(CandidateIds(commanders));
        Assert.Empty(CandidateIds(companions));
    }

    [Fact]
    public async Task A_commander_response_carries_no_legality_as_of()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        using var app = new ArchiveApp(autoMigrate: false, connectionString);
        using var client = app.CreateClient();

        var response = await PostAsync(client, "/cards/search", new { role = "main", limit = 200 });

        Assert.False(response.GetProperty("versions").TryGetProperty("legality_as_of", out _));
        Assert.NotNull(response.GetProperty("versions").GetProperty("banlist_version").GetString());
    }

    [Fact]
    public async Task Commander_alternatives_echoes_the_commander_format()
    {
        var connectionString = await IndexCorpus.ConnectionStringAsync(postgres);
        var oracle = await MaterializedLegality.LoadAsync(connectionString);
        using var app = new ArchiveApp(autoMigrate: false, connectionString);
        using var client = app.CreateClient();
        var commander = oracle.Legal(oracle.Latest, Data.Role.Commander).First();

        var response = await PostAsync(client, "/commander-alternatives", new
        {
            decklist = new { commanders = new[] { new { oracle_id = commander } } },
        });

        Assert.Equal("commander", response.GetProperty("versions").GetProperty("format").GetString());
    }

    private static Guid[] CandidateIds(JsonElement response)
        => [.. response.GetProperty("candidates").EnumerateArray()
            .Select(c => c.GetProperty("card").GetProperty("id").GetGuid())];

    private static async Task<JsonElement> PostAsync(HttpClient client, string route, object body)
    {
        var response = await client.PostAsJsonAsync(route, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
