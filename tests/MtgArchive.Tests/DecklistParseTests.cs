using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MtgArchive.Tests;

/// <summary>
/// The decklist parse route (ADR 0012): <c>POST /decklist-parse</c>
/// turns Arena export text or a plain <c>&lt;qty&gt; &lt;name&gt;</c> list
/// into the <c>decklist</c> body plus notes. It reads no database, so the
/// tests need no corpus.
/// </summary>
public sealed class DecklistParseTests
{
    private const string ArenaExport = """
        About
        Name Ula Tempo

        Commander
        1 Ula, Tide Caller (BLB) 76

        Companion
        1 Lurrus of the Dream-Den (IKO) 226

        Deck
        1 Sol Ring (C21) 263
        36 Island (FDN) 279
        1 Fire // Ice (MH2) 290

        Sideboard
        1 Lurrus of the Dream-Den (IKO) 226
        2 Negate (FDN) 156
        """;

    [Fact]
    public async Task An_arena_export_parses_into_the_commander_companion_and_entry_seats()
    {
        var parsed = await ParseAsync(ArenaExport);
        var decklist = parsed.GetProperty("decklist");

        Assert.Equal(["Ula, Tide Caller"], Names(decklist.GetProperty("commanders")));
        Assert.Equal("Lurrus of the Dream-Den", decklist.GetProperty("companion").GetProperty("name").GetString());
        Assert.Equal(
            [("Sol Ring", 1), ("Island", 36), ("Fire // Ice", 1)],
            Entries(decklist));
    }

    [Fact]
    public async Task A_companion_under_companion_and_sideboard_appears_once()
    {
        var decklist = (await ParseAsync(ArenaExport)).GetProperty("decklist");

        var names = Names(decklist.GetProperty("commanders"))
            .Append(decklist.GetProperty("companion").GetProperty("name").GetString())
            .Concat(Entries(decklist).Select(e => e.Name));
        Assert.Single(names, n => n == "Lurrus of the Dream-Den");
    }

    [Fact]
    public async Task The_notes_name_the_ignored_sideboard_about_and_name_sections()
    {
        var notes = (await ParseAsync(ArenaExport)).GetProperty("notes").EnumerateArray()
            .Select(n => n.GetString()!)
            .ToList();

        Assert.Contains(notes, n => n.Contains("Sideboard"));
        Assert.Contains(notes, n => n.Contains("About"));
        Assert.Contains(notes, n => n.Contains("Name") && n.Contains("Ula Tempo"));
    }

    [Fact]
    public async Task A_plain_list_parses_with_every_line_as_an_entry()
    {
        var parsed = await ParseAsync("1 Ula, Tide Caller\n\n1 Sol Ring\n36 Island\n");
        var decklist = parsed.GetProperty("decklist");

        Assert.Empty(decklist.GetProperty("commanders").EnumerateArray());
        Assert.False(decklist.TryGetProperty("companion", out _));
        Assert.Equal([("Ula, Tide Caller", 1), ("Sol Ring", 1), ("Island", 36)], Entries(decklist));
        Assert.Contains(parsed.GetProperty("notes").EnumerateArray(), n => n.GetString()!.Contains("Commander"));
    }

    [Theory]
    [InlineData("Deck\n1 Sol Ring\nSol Ring\n", 3, "Sol Ring")]
    [InlineData("1 Sol Ring\n\nMaybeboard\n", 3, "Maybeboard")]
    [InlineData("Commander\n2 Ula, Tide Caller\n", 2, "2 Ula, Tide Caller")]
    [InlineData("Companion\n1 Lurrus of the Dream-Den\n1 Jegantha, the Wellspring\n", 3, "1 Jegantha, the Wellspring")]
    public async Task A_bad_line_is_a_400_that_names_the_line_number_and_text(string text, int line, string content)
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/decklist-parse", new { text });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var detail = problem.RootElement.GetProperty("detail").GetString()!;
        Assert.Contains($"Line {line}:", detail);
        Assert.Contains($"'{content}'", detail);
    }

    /// <summary>Each frozen Brawl acceptance deck parses through
    /// the parse route with its commander seated, its size, and the cards its
    /// query names.</summary>
    [Theory]
    [InlineData("brawl-query-1/deck.txt", "Wrenn and Six", 100, "Ragavan, Nimble Pilferer")]
    [InlineData("brawl-query-2/parity-deck.txt", "The Jolly Balloon Man", 60, "Sacred Foundry")]
    [InlineData("brawl-query-3/deck.txt", "Old Stickfingers", 100, "Demonic Tutor", "Perennial Gravewarden")]
    public async Task Each_brawl_acceptance_deck_parses_through_the_parse_route(string fixture, string commander, int size, params string[] named)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(RepoRoot.Find(), "eval", "acceptance", fixture));

        var parsed = await ParseAsync(text);

        var decklist = parsed.GetProperty("decklist");
        Assert.Equal([commander], Names(decklist.GetProperty("commanders")));
        var entries = Entries(decklist);
        Assert.Equal(size - 1, entries.Sum(e => e.Quantity));
        Assert.Subset(entries.Select(e => e.Name).ToHashSet(), named.ToHashSet());
        Assert.Empty(parsed.GetProperty("notes").EnumerateArray());
    }

    private static async Task<JsonElement> ParseAsync(string text)
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/decklist-parse", new { text });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static string?[] Names(JsonElement cards)
        => [.. cards.EnumerateArray().Select(c => c.GetProperty("name").GetString())];

    private static (string Name, int Quantity)[] Entries(JsonElement decklist)
        => [.. decklist.GetProperty("entries").EnumerateArray()
            .Select(e => (e.GetProperty("card").GetProperty("name").GetString()!, e.GetProperty("quantity").GetInt32()))];
}
