using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MtgArchive.Tests;

/// <summary>
/// Version-parameter convention over the HTTP pipeline: optional, defaults to
/// latest, echoed in the versions block, unknown pin is a 4xx problem+json —
/// never a silent fallback.
/// </summary>
public class VersionPinTests
{
    private static async Task<HttpResponseMessage> Probe(object body)
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();
        return await client.PostAsJsonAsync("/_seam/versioned-echo", body);
    }

    [Fact]
    public async Task Omitted_pin_resolves_to_latest_and_is_echoed()
    {
        using var response = await Probe(new { });

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("2025-09-22", doc.RootElement.GetProperty("versions").GetProperty("banlist_version").GetString());
    }

    [Fact]
    public async Task Known_pin_is_used_and_echoed()
    {
        using var response = await Probe(new Dictionary<string, object> { ["banlist_version"] = "2025-04-21" });

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("2025-04-21", doc.RootElement.GetProperty("versions").GetProperty("banlist_version").GetString());
    }

    [Fact]
    public async Task Unknown_pin_is_a_4xx_problem_json_with_extension_members()
    {
        using var response = await Probe(new Dictionary<string, object> { ["banlist_version"] = "1999-01-01" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("title", out _));
        Assert.Equal("banlist_version", doc.RootElement.GetProperty("parameter").GetString());
        Assert.Equal("1999-01-01", doc.RootElement.GetProperty("value").GetString());
    }
}
