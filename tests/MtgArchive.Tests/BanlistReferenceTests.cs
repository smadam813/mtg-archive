using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MtgArchive.Tests;

/// <summary>
/// The banlist reference endpoints: GET /banlist/versions,
/// GET /banlist/{version} addressed by effective_date, and
/// GET /ban-events/{id} — the resolution targets for banlist-version and
/// ban-event citations — plus the card detail's banlist axis now that
/// replayed versions exist. Replays the fixture events file into a fresh
/// database shared by the class's tests.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BanlistReferenceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = "";
    private Guid _ancestral;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync($"banlist_ref_{Guid.NewGuid():N}");
        await using (var db = PostgresFixture.CreateContext(_connectionString))
        {
            await db.Database.MigrateAsync();
            foreach (var name in new[]
            {
                "Ancestral Recall", "Braids, Cabal Minion", "Sundering Titan", "Lutri, the Spellchaser",
            })
            {
                var card = TestCards.Minimal(Guid.NewGuid());
                card.Name = name;
                db.Cards.Add(card);
                if (name == "Ancestral Recall")
                {
                    _ancestral = card.OracleId;
                }
            }

            await db.SaveChangesAsync();
        }

        await BanlistReplayTests.ReplayAsync(
            _connectionString,
            Path.Combine(AppContext.BaseDirectory, "fixtures", "banlist", "banlist-events.json"));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Versions_lists_every_dated_version_oldest_first()
    {
        using var app = NewApp();
        using var client = app.CreateClient();

        var body = await GetJsonAsync(client, "/banlist/versions");

        var versions = body.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(3, versions.Count);
        Assert.Equal("2004-10-04", versions[0].GetProperty("effective_date").GetString());
        Assert.Equal("day", versions[0].GetProperty("date_precision").GetString());
        Assert.Equal("2007-06-01", versions[1].GetProperty("effective_date").GetString());
        Assert.Equal("month", versions[1].GetProperty("date_precision").GetString());
        Assert.Equal("https://example.org/2026-02-announcement",
            versions[2].GetProperty("announcement_url").GetString());
    }

    [Fact]
    public async Task A_version_serves_its_events_and_the_full_ban_state()
    {
        using var app = NewApp();
        using var client = app.CreateClient();

        var body = await GetJsonAsync(client, "/banlist/2007-06-01");

        Assert.Equal("2007-06-01", body.GetProperty("effective_date").GetString());
        Assert.Equal("month", body.GetProperty("date_precision").GetString());

        // This announcement's own changes, with the verbatim wire literals.
        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal("Braids, Cabal Minion", events[0].GetProperty("card").GetProperty("label").GetString());
        Assert.Equal("unban", events[0].GetProperty("action").GetString());
        Assert.Equal("commander_only", events[0].GetProperty("scope").GetString());
        Assert.Equal("Sundering Titan", events[1].GetProperty("card").GetProperty("label").GetString());
        Assert.Equal("ban", events[1].GetProperty("action").GetString());
        Assert.Equal("all", events[1].GetProperty("scope").GetString());

        // The replayed state: Braids' 2004 commander-only ban is undone by
        // this version's unban; the two full bans stand.
        var banned = body.GetProperty("banned").EnumerateArray().ToList();
        Assert.Equal(["Ancestral Recall", "Sundering Titan"],
            banned.Select(b => b.GetProperty("card").GetProperty("label").GetString()!).ToArray());
        Assert.All(banned, b => Assert.Equal("all", b.GetProperty("scope").GetString()));

        // The card entry is a citation resolvable at GET /cards/{oracle_id}.
        var card = banned[0].GetProperty("card");
        Assert.Equal("card", card.GetProperty("type").GetString());
        Assert.Equal(_ancestral, Guid.Parse(card.GetProperty("id").GetString()!));
    }

    [Fact]
    public async Task The_earliest_version_carries_the_commander_only_scope_and_the_latest_the_companion_scope()
    {
        using var app = NewApp();
        using var client = app.CreateClient();

        var body = await GetJsonAsync(client, "/banlist/2004-10-04");
        var banned = body.GetProperty("banned").EnumerateArray().ToList();
        Assert.Equal(2, banned.Count);
        Assert.Equal("commander_only", banned
            .Single(b => b.GetProperty("card").GetProperty("label").GetString() == "Braids, Cabal Minion")
            .GetProperty("scope").GetString());

        body = await GetJsonAsync(client, "/banlist/2026-02-02");
        banned = body.GetProperty("banned").EnumerateArray().ToList();
        Assert.Equal(["Ancestral Recall", "Lutri, the Spellchaser", "Sundering Titan"],
            banned.Select(b => b.GetProperty("card").GetProperty("label").GetString()!).ToArray());
        Assert.Equal("companion", banned[1].GetProperty("scope").GetString());
    }

    [Fact]
    public async Task Ban_event_citations_resolve_to_card_action_scope_and_announcement()
    {
        using var app = NewApp();
        using var client = app.CreateClient();

        // Follow a citation the way an agent would: from a version's banned
        // state to the dated event that produced it.
        var version = await GetJsonAsync(client, "/banlist/2026-02-02");
        var lutri = version.GetProperty("banned").EnumerateArray()
            .Single(b => b.GetProperty("card").GetProperty("label").GetString() == "Lutri, the Spellchaser");
        var citation = lutri.GetProperty("ban_event");
        Assert.Equal("ban_event", citation.GetProperty("type").GetString());
        Assert.Contains("banned as companion 2026-02-02", citation.GetProperty("label").GetString());

        var banEvent = await GetJsonAsync(client, $"/ban-events/{citation.GetProperty("id").GetString()}");
        Assert.Equal("Lutri, the Spellchaser",
            banEvent.GetProperty("card").GetProperty("label").GetString());
        Assert.Equal("ban", banEvent.GetProperty("action").GetString());
        Assert.Equal("companion", banEvent.GetProperty("scope").GetString());
        var announcement = banEvent.GetProperty("announcement");
        Assert.Equal("2026-02-02", announcement.GetProperty("effective_date").GetString());
        Assert.Equal("https://example.org/2026-02-announcement",
            announcement.GetProperty("announcement_url").GetString());

        // The ids on a version's own event list resolve the same way.
        var listed = (await GetJsonAsync(client, "/banlist/2004-10-04"))
            .GetProperty("events").EnumerateArray().First();
        var resolved = await GetJsonAsync(client, $"/ban-events/{listed.GetProperty("id").GetInt32()}");
        Assert.Equal("Ancestral Recall", resolved.GetProperty("card").GetProperty("label").GetString());
    }

    [Fact]
    public async Task Unknown_versions_and_events_are_problem_404s_never_a_latest_fallback()
    {
        using var app = NewApp();
        using var client = app.CreateClient();

        foreach (var path in new[] { "/banlist/2019-01-01", "/banlist/not-a-date", "/banlist/2007-06" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var problem = await ReadProblemAsync(response);
            Assert.Equal("urn:mtg-archive:problem:unknown-banlist-version",
                problem.GetProperty("type").GetString());
        }

        var missing = await client.GetAsync("/ban-events/999999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("urn:mtg-archive:problem:unknown-ban-event",
            (await ReadProblemAsync(missing)).GetProperty("type").GetString());

        // A non-integer id misses the route constraint before any handler
        // runs; the status-code-pages middleware still owes it problem+json.
        var malformed = await client.GetAsync("/ban-events/not-an-id");
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(404, (await ReadProblemAsync(malformed)).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Card_detail_resolves_the_banlist_axis_to_latest_and_honors_known_pins()
    {
        using var app = NewApp();
        using var client = app.CreateClient();

        var detail = await GetJsonAsync(client, $"/cards/{_ancestral}");
        Assert.Equal("2026-02-02",
            detail.GetProperty("versions").GetProperty("banlist_version").GetString());

        detail = await GetJsonAsync(client, $"/cards/{_ancestral}?banlist_version=2007-06-01");
        Assert.Equal("2007-06-01",
            detail.GetProperty("versions").GetProperty("banlist_version").GetString());

        var unknown = await client.GetAsync($"/cards/{_ancestral}?banlist_version=2019-01-01");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var problem = await ReadProblemAsync(unknown);
        Assert.Equal("banlist_version", problem.GetProperty("parameter").GetString());
    }

    private ArchiveApp NewApp() => new(autoMigrate: false, _connectionString);

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }
}
