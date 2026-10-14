using System.Net.Http.Json;
using System.Text.Json;

namespace MtgArchive.Tests;

/// <summary>
/// Asserts the wire-format conventions over the real HTTP pipeline via the
/// test-host-composed seam probe endpoint (POST /_seam/versioned-echo).
/// </summary>
public class WireConventionTests
{
    private static async Task<JsonDocument> Probe(object body)
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/_seam/versioned-echo", body);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Wire_is_snake_case_end_to_end()
    {
        using var doc = await Probe(new { });

        Assert.True(doc.RootElement.TryGetProperty("collection_scope", out _));
        Assert.True(doc.RootElement.TryGetProperty("versions", out var versions));
        Assert.True(versions.TryGetProperty("banlist_version", out _));
    }

    [Fact]
    public async Task Citation_is_type_id_label_with_snake_case_type_values()
    {
        using var doc = await Probe(new { });

        var citation = doc.RootElement.GetProperty("citation");
        Assert.Equal("ban_event", citation.GetProperty("type").GetString());
        Assert.True(citation.TryGetProperty("id", out _));
        Assert.True(citation.TryGetProperty("label", out _));
        Assert.Equal(3, citation.EnumerateObject().Count());
    }

    [Fact]
    public async Task Collection_scope_defaults_to_false_when_omitted()
    {
        using var doc = await Probe(new { });

        Assert.False(doc.RootElement.GetProperty("collection_scope").GetBoolean());
    }

    [Fact]
    public async Task Collection_scope_binds_from_snake_case_request_body()
    {
        using var doc = await Probe(new Dictionary<string, object> { ["collection_scope"] = true });

        Assert.True(doc.RootElement.GetProperty("collection_scope").GetBoolean());
    }

    [Fact]
    public async Task Versions_block_omits_axes_that_were_not_in_play()
    {
        using var doc = await Probe(new { });

        var versions = doc.RootElement.GetProperty("versions");
        Assert.Single(versions.EnumerateObject());
    }
}
