using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;

namespace MtgArchive.Tests;

/// <summary>
/// GET /rules/{number}: the citation target serving one CR rule
/// chunk or glossary entry verbatim with the CR effective date. Seeds the
/// CR tables of the shared corpus database once — no other suite touches
/// them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RulesReferenceTests(PostgresFixture postgres)
{
    private static readonly DateOnly EffectiveDate = new(2026, 8, 7);

    private const string Chunk601_2 =
        "601.2. To cast a test spell is to take it from where it is and put it on the stack.\n"
        + "\n"
        + "601.2a To propose the casting of a test spell, a player first moves that card to the stack.\n"
        + "\n"
        + "601.2b If the proposed test spell requires targets, the player announces them.\n"
        + "\n"
        + "Example: A test player casts a spell targeting a creature.";

    private const string DeathtouchEntry =
        "Deathtouch\nA test keyword ability. See rule 702.2, “Deathtouch.”";

    [Fact]
    public async Task A_rule_chunk_is_served_verbatim_with_the_effective_date()
    {
        await SeedAsync();
        using var app = NewApp();
        using var client = app.CreateClient();

        var entry = await GetJsonAsync(client, "/rules/601.2");

        Assert.Equal("rule", entry.GetProperty("kind").GetString());
        Assert.Equal("601.2", entry.GetProperty("number").GetString());
        Assert.Equal(Chunk601_2, entry.GetProperty("text").GetString());
        Assert.Equal("2026-08-07",
            entry.GetProperty("versions").GetProperty("cr_effective_date").GetString());
        Assert.False(entry.TryGetProperty("term", out _));
    }

    // A subrule citation ("601.2b") resolves to the chunk containing it —
    // rule chunks are the resolution granularity, and the subrule's text is
    // inside, verbatim.
    [Theory]
    [InlineData("601.2b")]
    [InlineData("601.2.")]
    public async Task Subrule_numbers_and_trailing_dots_resolve_to_the_containing_chunk(string number)
    {
        await SeedAsync();
        using var app = NewApp();
        using var client = app.CreateClient();

        var entry = await GetJsonAsync(client, $"/rules/{number}");

        Assert.Equal("601.2", entry.GetProperty("number").GetString());
        Assert.Equal(Chunk601_2, entry.GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_glossary_entry_is_served_verbatim_by_its_term()
    {
        await SeedAsync();
        using var app = NewApp();
        using var client = app.CreateClient();

        var entry = await GetJsonAsync(client, "/rules/Deathtouch");

        Assert.Equal("glossary", entry.GetProperty("kind").GetString());
        Assert.Equal("Deathtouch", entry.GetProperty("term").GetString());
        Assert.Equal(DeathtouchEntry, entry.GetProperty("text").GetString());
        Assert.Equal("2026-08-07",
            entry.GetProperty("versions").GetProperty("cr_effective_date").GetString());
        Assert.False(entry.TryGetProperty("number", out _));
    }

    [Fact]
    public async Task A_multi_word_glossary_term_resolves_url_encoded()
    {
        await SeedAsync();
        using var app = NewApp();
        using var client = app.CreateClient();

        var entry = await GetJsonAsync(client, $"/rules/{Uri.EscapeDataString("Active Player")}");

        Assert.Equal("Active Player", entry.GetProperty("term").GetString());
    }

    [Theory]
    [InlineData("601.9")]     // no such chunk
    [InlineData("601.2z")]    // chunk exists, subrule does not
    [InlineData("601")]       // section numbers are not chunks
    [InlineData("Lifelink")]  // no such term
    [InlineData("deathtouch")] // exact-term only, like exact-name card lookup
    public async Task Unknown_numbers_and_terms_are_problem_404s(string number)
    {
        await SeedAsync();
        using var app = NewApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync($"/rules/{Uri.EscapeDataString(number)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:mtg-archive:problem:unknown-rule",
            body.RootElement.GetProperty("type").GetString());
        Assert.Equal(number, body.RootElement.GetProperty("number").GetString());
    }

    private ArchiveApp NewApp() => new(autoMigrate: false, postgres.CorpusConnectionString);

    /// <summary>Seeds the shared corpus database's CR tables once.</summary>
    private async Task SeedAsync()
    {
        await using var db = postgres.CreateCorpusContext();
        if (await db.CrDocuments.AnyAsync())
        {
            return;
        }

        db.CrDocuments.Add(new CrDocument
        {
            EffectiveDate = EffectiveDate,
            FullText = $"(test archive)\n\n{Chunk601_2}\n\n{DeathtouchEntry}",
            FullTextSha256 = new string('0', 64),
            SourceUrl = "https://media.example.test/MagicCompRules 20260807.txt",
            FetchedAt = DateTimeOffset.UtcNow,
        });
        db.CrRuleChunks.Add(new CrRuleChunk
        {
            Number = "601.2",
            Text = Chunk601_2,
            CrEffectiveDate = EffectiveDate,
        });
        db.CrGlossaryChunks.Add(new CrGlossaryChunk
        {
            Term = "Deathtouch",
            Text = DeathtouchEntry,
            CrEffectiveDate = EffectiveDate,
        });
        db.CrGlossaryChunks.Add(new CrGlossaryChunk
        {
            Term = "Active Player",
            Text = "Active Player\nThe test player whose turn it is. See rule 102.1.",
            CrEffectiveDate = EffectiveDate,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }
}
