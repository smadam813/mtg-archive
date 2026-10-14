using System.Net;
using System.Text.Json;
using MtgArchive.Data;

namespace MtgArchive.Tests;

/// <summary>
/// Card reference endpoints: GET /cards/{oracle_id} and the
/// exact-name variant GET /cards?name= — the resolution target for every
/// card citation. The full detail shape is frozen; legality is
/// populated by the rebuild.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CardReferenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Card_detail_serves_faces_rulings_tags_and_copy_limit()
    {
        var oracleId = Guid.NewGuid();
        var name = await SeedAsync(MultiFacedCard(oracleId));
        using var app = NewApp();
        using var client = app.CreateClient();

        var detail = await GetJsonAsync(client, $"/cards/{oracleId}");

        Assert.Equal(oracleId, detail.GetProperty("oracle_id").GetGuid());
        Assert.Equal(name, detail.GetProperty("name").GetString());
        Assert.Equal("Legendary Creature — Human Wizard // Legendary Planeswalker — Liliana",
            detail.GetProperty("type_line").GetString());
        Assert.Equal(3m, detail.GetProperty("mana_value").GetDecimal());
        Assert.Equal(1, detail.GetProperty("copy_limit").GetInt32());
        Assert.False(detail.GetProperty("stale").GetBoolean());

        var faces = detail.GetProperty("faces").EnumerateArray().ToList();
        Assert.Equal(2, faces.Count);
        Assert.Equal(0, faces[0].GetProperty("face_index").GetInt32());
        Assert.Equal("Liliana, Heretical Healer", faces[0].GetProperty("name").GetString());
        Assert.Equal("{1}{B}{B}", faces[0].GetProperty("mana_cost").GetString());
        Assert.Equal("2", faces[0].GetProperty("power").GetString());
        Assert.Equal(1, faces[1].GetProperty("face_index").GetInt32());
        Assert.Equal("3", faces[1].GetProperty("loyalty").GetString());
        Assert.False(faces[1].TryGetProperty("mana_cost", out _));

        var ruling = Assert.Single(detail.GetProperty("rulings").EnumerateArray());
        Assert.Equal("wotc", ruling.GetProperty("source").GetString());
        Assert.Equal("2021-03-19", ruling.GetProperty("published_at").GetString());
        Assert.Equal("Both rows are one card.", ruling.GetProperty("comment").GetString());

        Assert.Equal(["ramp", "removal"],
            detail.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray());
        Assert.Equal(
            [("ramp", false, "median"), ("removal", true, (string?)null)],
            detail.GetProperty("taggings").EnumerateArray().Select(t => (
                t.GetProperty("tag").GetString()!,
                t.GetProperty("inherited").GetBoolean(),
                t.TryGetProperty("weight", out var weight) ? weight.GetString() : null)));

        // Shape frozen: legality is absent until a rebuild, no banlist
        // version exists yet to echo, and the staple block is gone.
        Assert.False(detail.TryGetProperty("legality", out _));
        Assert.False(detail.TryGetProperty("staple", out _));
        var versions = detail.GetProperty("versions");
        Assert.Equal(JsonValueKind.Object, versions.ValueKind);
        Assert.False(versions.TryGetProperty("banlist_version", out _));
    }

    [Fact]
    public async Task Copy_limit_null_is_explicit_on_the_wire_and_seven_dwarves_carry_seven()
    {
        var unlimited = Guid.NewGuid();
        var dwarves = Guid.NewGuid();
        var basic = TestCards.Minimal(unlimited);
        basic.CopyLimit = null;
        var dwarf = TestCards.Minimal(dwarves);
        dwarf.CopyLimit = 7;
        await SeedAsync(basic);
        await SeedAsync(dwarf);
        using var app = NewApp();
        using var client = app.CreateClient();

        // null = unlimited is a meaningful value, so the field is written
        // explicitly instead of omitted like other null members.
        var detail = await GetJsonAsync(client, $"/cards/{unlimited}");
        Assert.True(detail.TryGetProperty("copy_limit", out var copyLimit));
        Assert.Equal(JsonValueKind.Null, copyLimit.ValueKind);

        detail = await GetJsonAsync(client, $"/cards/{dwarves}");
        Assert.Equal(7, detail.GetProperty("copy_limit").GetInt32());
    }

    [Fact]
    public async Task Exact_name_lookup_returns_the_card_detail_and_nothing_fuzzier()
    {
        var oracleId = Guid.NewGuid();
        var name = await SeedAsync(TestCards.Minimal(oracleId));
        using var app = NewApp();
        using var client = app.CreateClient();

        var detail = await GetJsonAsync(client, $"/cards?name={Uri.EscapeDataString(name)}");
        Assert.Equal(oracleId, detail.GetProperty("oracle_id").GetGuid());
        Assert.Equal(JsonValueKind.Array, detail.GetProperty("faces").ValueKind);

        // Exact match only — a case mismatch is a miss, not a fuzzy hit.
        var miss = await client.GetAsync($"/cards?name={Uri.EscapeDataString(name.ToUpperInvariant())}");
        Assert.Equal(HttpStatusCode.NotFound, miss.StatusCode);
    }

    // The corpus is NFC; a decomposed spelling of the same name must match.
    [Fact]
    public async Task Non_ascii_names_match_after_nfc_normalization()
    {
        var oracleId = Guid.NewGuid();
        var basic = TestCards.Minimal(oracleId);
        basic.Name = $"Bespoke Bō {oracleId:N}";
        var name = await SeedAsync(basic);

        var decomposed = name.Replace("ō", "ō");
        Assert.NotEqual(name, decomposed);
        using var app = NewApp();
        using var client = app.CreateClient();

        var detail = await GetJsonAsync(client, $"/cards?name={Uri.EscapeDataString(decomposed)}");
        Assert.Equal(oracleId, detail.GetProperty("oracle_id").GetGuid());
    }

    [Fact]
    public async Task Name_lookup_miss_and_missing_name_are_problem_4xxs()
    {
        using var app = NewApp();
        using var client = app.CreateClient();

        var miss = await client.GetAsync($"/cards?name={Uri.EscapeDataString($"No Such Card {Guid.NewGuid():N}")}");
        Assert.Equal(HttpStatusCode.NotFound, miss.StatusCode);
        var problem = await ReadProblemAsync(miss);
        Assert.Equal("urn:mtg-archive:problem:unknown-card-name", problem.GetProperty("type").GetString());
        Assert.StartsWith("No Such Card ", problem.GetProperty("name").GetString());

        var missing = await client.GetAsync("/cards");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        problem = await ReadProblemAsync(missing);
        Assert.Equal("urn:mtg-archive:problem:missing-parameter", problem.GetProperty("type").GetString());
        Assert.Equal("name", problem.GetProperty("parameter").GetString());
    }

    [Fact]
    public async Task Unknown_card_is_a_problem_404()
    {
        var oracleId = Guid.NewGuid();
        using var app = NewApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync($"/cards/{oracleId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("urn:mtg-archive:problem:unknown-card", problem.GetProperty("type").GetString());
        Assert.Equal(oracleId, problem.GetProperty("oracle_id").GetGuid());
    }

    // A malformed oracle_id fails the route constraint before any handler
    // runs; the status-code-pages middleware still owes it problem+json.
    [Fact]
    public async Task Malformed_oracle_id_is_still_a_problem_404()
    {
        using var app = NewApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/cards/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Duplicate_live_names_are_surfaced_as_corruption_not_disambiguated()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var name = await SeedAsync(TestCards.Minimal(first));
        var twin = TestCards.Minimal(second);
        twin.Name = name;
        await SeedAsync(twin);
        using var app = NewApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync($"/cards?name={Uri.EscapeDataString(name)}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await ReadProblemAsync(response);
        Assert.Equal("urn:mtg-archive:problem:ambiguous-card-name", problem.GetProperty("type").GetString());
        Assert.Equal(name, problem.GetProperty("name").GetString());
        Assert.Equal(new[] { first, second }.Order(),
            problem.GetProperty("oracle_ids").EnumerateArray().Select(id => id.GetGuid()).Order());
    }

    // The Llanowar Elves lookup returned a 500 before.
    [Fact]
    public async Task A_real_card_wins_the_name_lookup_over_its_token_namesake()
    {
        var elvesId = Guid.NewGuid();
        var elves = TestCards.Minimal(elvesId);
        elves.Name = $"Llanowar Elves {elvesId:N}";
        elves.Layout = "normal";
        var name = await SeedAsync(elves);
        var token = TestCards.Minimal(Guid.NewGuid());
        token.Name = name;
        token.Layout = "token";
        await SeedAsync(token);
        using var app = NewApp();
        using var client = app.CreateClient();

        var detail = await GetJsonAsync(client, $"/cards?name={Uri.EscapeDataString(name)}");

        Assert.Equal(elvesId, detail.GetProperty("oracle_id").GetGuid());
    }

    [Fact]
    public async Task Unknown_pinned_banlist_version_is_a_problem_400_never_a_silent_fallback()
    {
        var oracleId = Guid.NewGuid();
        var name = await SeedAsync(TestCards.Minimal(oracleId));
        using var app = NewApp();
        using var client = app.CreateClient();

        foreach (var path in new[]
        {
            $"/cards/{oracleId}?banlist_version=2025-09-22",
            $"/cards?name={Uri.EscapeDataString(name)}&banlist_version=2025-09-22",
        })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await ReadProblemAsync(response);
            Assert.Equal("banlist_version", problem.GetProperty("parameter").GetString());
            Assert.Equal("2025-09-22", problem.GetProperty("value").GetString());
        }
    }

    [Fact]
    public async Task Stale_card_still_resolves_by_oracle_id_and_the_live_row_wins_name_lookup()
    {
        var staleId = Guid.NewGuid();
        var liveId = Guid.NewGuid();
        var stale = TestCards.Minimal(staleId);
        stale.Stale = true;
        var name = await SeedAsync(stale);
        var live = TestCards.Minimal(liveId);
        live.Name = name;
        await SeedAsync(live);
        using var app = NewApp();
        using var client = app.CreateClient();

        // Reference fetches keep resolving stale cards so collection rows
        // and ban history never dangle.
        var detail = await GetJsonAsync(client, $"/cards/{staleId}");
        Assert.True(detail.GetProperty("stale").GetBoolean());

        // A stale row can share its name with its replacement (Scryfall
        // oracle_id migrations); the live row wins the name lookup.
        detail = await GetJsonAsync(client, $"/cards?name={Uri.EscapeDataString(name)}");
        Assert.Equal(liveId, detail.GetProperty("oracle_id").GetGuid());
    }

    private ArchiveApp NewApp() => new(autoMigrate: false, postgres.CorpusConnectionString);

    /// <summary>Seeds into the shared corpus database and returns the card's name.</summary>
    private async Task<string> SeedAsync(Card card)
    {
        await using var db = postgres.CreateCorpusContext();
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return card.Name;
    }

    private static Card MultiFacedCard(Guid oracleId)
    {
        var card = TestCards.Minimal(oracleId, "B");
        card.ManaCost = "{1}{B}{B}";
        card.ManaValue = 3m;
        card.TypeLine = "Legendary Creature — Human Wizard // Legendary Planeswalker — Liliana";
        card.Faces =
        [
            new CardFace
            {
                FaceIndex = 0,
                Name = "Liliana, Heretical Healer",
                ManaCost = "{1}{B}{B}",
                ManaValue = 3m,
                TypeLine = "Legendary Creature — Human Wizard",
                OracleText = "Lifelink",
                Colors = ["B"],
                Power = "2",
                Toughness = "3",
            },
            new CardFace
            {
                FaceIndex = 1,
                Name = "Liliana, Defiant Necromancer",
                TypeLine = "Legendary Planeswalker — Liliana",
                OracleText = "[+2]: Each player discards a card.",
                Colors = ["B"],
                Loyalty = "3",
            },
        ];
        card.Rulings =
        [
            new CardRuling
            {
                Source = "wotc",
                PublishedAt = new DateOnly(2021, 3, 19),
                Comment = "Both rows are one card.",
            },
        ];
        card.OracleTags =
        [
            new CardOracleTag { Tag = "removal", Inherited = true },
            new CardOracleTag { Tag = "ramp", Weight = "median" },
        ];
        return card;
    }

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
