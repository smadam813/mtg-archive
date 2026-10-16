using System.Net;
using System.Text.Json;

namespace MtgArchive.Tests;

/// <summary>
/// Standard serves card legality only so far. Each deck route rejects it
/// with the 400 <c>unserved-format</c> problem before any other work, so no
/// route judges or plays a Standard deck under another format's rules.
/// </summary>
public sealed class UnservedFormatTests
{
    public static TheoryData<string, string> Routes() => new()
    {
        { "/deck-verdict", """{ "decklist": { "entries": [] }, "format": "standard" }""" },
        { "/tier1-report", """{ "decklist": { "entries": [] }, "format": "standard" }""" },
        {
            "/simulate",
            """{ "decklist": { "entries": [] }, "goal": { "kind": "on_battlefield", "card": "Island", "by_turn": 3 }, "format": "standard" }"""
        },
        { "/commander-alternatives", """{ "decklist": { "entries": [] }, "format": "standard" }""" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task A_deck_route_rejects_standard_with_a_problem_that_names_the_served_formats(string route, string body)
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.PostAsync(route, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, text);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(text);
        Assert.Equal("urn:mtg-archive:problem:unserved-format", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("format", problem.RootElement.GetProperty("parameter").GetString());
        Assert.Equal("standard", problem.RootElement.GetProperty("value").GetString());
        Assert.Equal(
            ["commander", "brawl", "competitive_brawl", "standard_brawl"],
            problem.RootElement.GetProperty("served").EnumerateArray().Select(s => s.GetString()!));
    }
}
