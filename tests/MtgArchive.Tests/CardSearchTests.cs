using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MtgArchive.Cards;
using MtgArchive.Data;
using MtgArchive.Embedding;
using Pgvector;

namespace MtgArchive.Tests;

/// <summary>
/// The card query endpoints: POST /cards/search (structured,
/// never an embedding call) and POST /cards/semantic-search (the filtered
/// KNN in one statement, the query prefixed service-side, faces collapsed to
/// cards, k defaulted and capped), both hard-filtered through
/// <c>legal_candidates</c> with lean hits, scoped availability, an unknown
/// pin as a 400, and the embedding-state gate as report content. Each test
/// gets its own migrated database seeded straight into the corpus tables
/// and an in-process Ollama; the property harness fuzzes both endpoints
/// against the real pipeline separately.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CardSearchTests(PostgresFixture postgres)
{
    private const string Version = "2025-04-21";

    private static readonly DateTimeOffset BrawlAsOf = new(2026, 10, 3, 21, 1, 55, TimeSpan.Zero);

    [Fact]
    public async Task Structured_search_filters_pages_and_never_embeds()
    {
        var connectionString = await NewDatabaseAsync();
        var fake = new FakeOllama();
        using var app = NewApp(connectionString, fake);
        using var client = app.CreateClient();
        var elf = Seeded("Llanowar Elves", typeLine: "Creature — Elf Druid", manaValue: 1, rank: 10, tags: ["ramp"], keywords: []);
        var wall = Seeded("Wall of Roots", typeLine: "Creature — Plant Wall", manaValue: 2, rank: 500, tags: ["ramp"], keywords: ["Defender"]);
        var bolt = Seeded("Lightning Bolt", typeLine: "Instant", manaValue: 1, rank: 5, tags: ["removal"]);
        // Propaganda carries pillowfort only through tax attack's parent chain.
        var propaganda = Seeded("Propaganda", typeLine: "Enchantment", manaValue: 3, rank: 20, tags: ["tax attack"]);
        propaganda.Card.OracleTags.Add(new CardOracleTag { Tag = "pillowfort", Inherited = true });
        await SeedAsync(connectionString, elf, wall, bolt, propaganda);
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            db.TagAliases.AddRange(
                new TagAlias { Alias = "fort", Label = "pillowfort" },
                new TagAlias { Alias = "tax", Label = "tax attack" });
            await db.SaveChangesAsync();
        }

        var page = await PostAsync(client, "/cards/search", new
        {
            types = new[] { "Creature" },
            tags = new[] { "ramp" },
            max_mana_value = 2,
            name = "o",
            limit = 1,
        });

        var hit = Assert.Single(page.GetProperty("candidates").EnumerateArray());
        Assert.Equal("card", hit.GetProperty("card").GetProperty("type").GetString());
        Assert.Equal(elf.OracleId, hit.GetProperty("card").GetProperty("id").GetGuid());
        Assert.Equal("Llanowar Elves", hit.GetProperty("card").GetProperty("label").GetString());
        Assert.Equal(10, hit.GetProperty("edhrec_rank").GetInt32());
        var legality = Assert.Single(hit.GetProperty("legality").EnumerateArray());
        Assert.Equal("main", legality.GetProperty("role").GetString());
        Assert.Equal("legal", legality.GetProperty("status").GetString());
        Assert.False(hit.TryGetProperty("rulings", out _));
        Assert.False(hit.TryGetProperty("distance", out _));
        Assert.False(hit.TryGetProperty("available", out _));
        Assert.Equal(Version, page.GetProperty("versions").GetProperty("banlist_version").GetString());

        page = await PostAsync(client, "/cards/search", new
        {
            types = new[] { "Creature" },
            tags = new[] { "ramp" },
            max_mana_value = 2,
            name = "o",
            limit = 1,
            offset = 1,
        });
        Assert.Equal([wall.OracleId], Ids(page));

        // A tag matches as its label, or as an alias of a label the card
        // carries, direct or inherited.
        foreach (var tag in new[] { "pillowfort", "fort", "tax" })
        {
            page = await PostAsync(client, "/cards/search", new { tags = new[] { tag } });
            Assert.Equal([propaganda.OracleId], Ids(page));
        }

        page = await PostAsync(client, "/cards/search", new { tags = new[] { "fort", "ramp" } });
        Assert.Empty(Ids(page));

        page = await PostAsync(client, "/cards/search", new { keywords = new[] { "Defender" } });
        Assert.Equal([wall.OracleId], Ids(page));

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Semantic_search_dedupes_faces_to_cards_nearest_first_with_the_card_search_prefix()
    {
        var connectionString = await NewDatabaseAsync();
        var fake = new FakeOllama();
        using var app = NewApp(connectionString, fake);
        using var client = app.CreateClient();
        var twoFaced = Seeded("Fire // Ice", typeLine: "Instant // Instant", faces: [1024, 512]);
        var single = Seeded("Shock", typeLine: "Instant", faces: [768]);
        var far = Seeded("Giant Growth", typeLine: "Instant", faces: [256]);
        var unembedded = Seeded("Unembedded", typeLine: "Instant", faces: []);
        await SeedAsync(connectionString, twoFaced, single, far, unembedded);

        var response = await PostAsync(client, "/cards/semantic-search", new { query = "deal damage", k = 3 });

        Assert.True(response.GetProperty("semantic").GetProperty("enabled").GetBoolean());
        var hits = response.GetProperty("candidates").EnumerateArray().ToArray();
        Assert.Equal([twoFaced.OracleId, single.OracleId, far.OracleId], Ids(response));
        Assert.Equal(0, hits[0].GetProperty("face_index").GetInt32());
        Assert.Equal(0, hits[0].GetProperty("distance").GetDouble(), 3);
        Assert.True(hits[1].GetProperty("distance").GetDouble() > 0);
        Assert.Equal(Version, response.GetProperty("versions").GetProperty("banlist_version").GetString());

        var (path, request) = Assert.Single(fake.Requests);
        Assert.Equal("/api/embed", path);
        Assert.Equal(
            $"Instruct: {EmbeddingModel.CardSearchInstruction}\nQuery: deal damage",
            request.GetProperty("input")[0].GetString());
    }

    [Fact]
    public async Task Semantic_search_orders_cards_at_equal_distance_by_name()
    {
        var connectionString = await NewDatabaseAsync();
        using var app = NewApp(connectionString, new FakeOllama());
        using var client = app.CreateClient();
        var zeta = Seeded("Zeta Twin", faces: [512]);
        var alpha = Seeded("Alpha Twin", faces: [512]);
        var mid = Seeded("Mid Twin", faces: [512]);
        await SeedAsync(connectionString, zeta, alpha, mid);

        var response = await PostAsync(client, "/cards/semantic-search", new { query = "twin", k = 2 });

        Assert.Equal([alpha.OracleId, mid.OracleId], Ids(response));
    }

    [Fact]
    public async Task K_counts_cards_not_faces()
    {
        var connectionString = await NewDatabaseAsync();
        using var app = NewApp(connectionString, new FakeOllama());
        using var client = app.CreateClient();
        await SeedAsync(connectionString, [.. Enumerable.Range(0, 5).Select(i => Seeded($"Split {i}", faces: [1024 - (2 * i), 1023 - (2 * i)]))]);

        var response = await PostAsync(client, "/cards/semantic-search", new { query = "split", k = 3 });

        Assert.Equal(3, Ids(response).Distinct().Count());
        Assert.Equal(3, Ids(response).Length);
    }

    /// <summary>A hundred padding cards are nearer the query than the listed
    /// cards. An index scan that stops at its search width and then filters
    /// returns none of the listed cards. With sequential scans and sorts
    /// turned off, the planner prefers such a scan.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oracle_ids_restrict_semantic_search_to_exactly_the_listed_cards(bool forceIndexScan)
    {
        var connectionString = await NewDatabaseAsync();
        using var app = NewApp(
            forceIndexScan ? connectionString + ";Options=-c enable_seqscan=off -c enable_sort=off" : connectionString,
            new FakeOllama());
        using var client = app.CreateClient();
        var fireIce = Seeded("Fire // Ice", typeLine: "Instant // Instant", faces: [16, 24]);
        var shock = Seeded("Shock", typeLine: "Instant", faces: [8]);
        var growth = Seeded("Giant Growth", typeLine: "Instant", faces: [4]);
        await SeedAsync(connectionString, [.. Enumerable.Range(0, 100).Select(i => Seeded($"Padding {i}", faces: [1024 - i])), fireIce, shock, growth]);

        var response = await PostAsync(client, "/cards/semantic-search", new
        {
            query = "burn",
            oracle_ids = new[] { growth.OracleId, fireIce.OracleId, shock.OracleId },
            k = 3,
        });

        Assert.Equal([fireIce.OracleId, shock.OracleId, growth.OracleId], Ids(response));
        Assert.Equal(1, response.GetProperty("candidates")[0].GetProperty("face_index").GetInt32());
    }

    // Semantic search filters to the format's legal candidates,
    // then ranks exactly, so a format filter only removes cards. A card in
    // two pools keeps its relative order in both. This is why the Commander
    // retrieval eval gates every format (eval/retrieval/README.md); if
    // search moves to approximate ranking, this test fails and that
    // decision reopens.
    [Fact]
    public async Task Semantic_search_keeps_the_relative_order_of_cards_in_two_format_pools()
    {
        var connectionString = await NewDatabaseAsync();
        using var app = NewApp(connectionString, new FakeOllama());
        using var client = app.CreateClient();
        // Nearest first by leading ones; every third card is outside Brawl.
        var seeds = Enumerable.Range(0, 9)
            .Select(i => Seeded($"Ranked {i}", faces: [1000 - (100 * i)], inBrawl: i % 3 != 1))
            .ToArray();
        await SeedAsync(connectionString, seeds);

        var commander = Ids(await PostAsync(client, "/cards/semantic-search", new { query = "rank", k = 50 }));
        var brawl = Ids(await PostAsync(client, "/cards/semantic-search", new { query = "rank", k = 50, format = "brawl" }));

        Assert.Equal(seeds.Select(s => s.OracleId), commander);
        Assert.Equal(seeds.Where(s => s.InBrawl).Select(s => s.OracleId), brawl);
        Assert.Equal(commander.Where(brawl.Contains), brawl);
    }

    [Fact]
    public async Task Oracle_ids_filter_both_endpoints_and_unknown_ids_match_nothing()
    {
        var connectionString = await NewDatabaseAsync();
        using var app = NewApp(connectionString, new FakeOllama());
        using var client = app.CreateClient();
        var ring = Seeded("Sol Ring", faces: [1024]);
        var crypt = Seeded("Mana Crypt", faces: [1024]);
        await SeedAsync(connectionString, ring, crypt);

        foreach (var route in new[] { "/cards/search", "/cards/semantic-search" })
        {
            Assert.Equal(2, Ids(await PostAsync(client, route, new { query = "mana" })).Length);
            Assert.Equal(2, Ids(await PostAsync(client, route, new { query = "mana", oracle_ids = (Guid[]?)null })).Length);
            Assert.Equal([ring.OracleId], Ids(await PostAsync(client, route, new { query = "mana", oracle_ids = new[] { ring.OracleId, Guid.NewGuid() } })));
            Assert.Empty(Ids(await PostAsync(client, route, new { query = "mana", oracle_ids = Array.Empty<Guid>() })));
            Assert.Empty(Ids(await PostAsync(client, route, new { query = "mana", oracle_ids = new[] { Guid.NewGuid() } })));
        }
    }

    [Theory]
    [InlineData("/cards/search")]
    [InlineData("/cards/semantic-search")]
    public async Task More_than_200_oracle_ids_is_a_400(string route)
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(route, new
        {
            query = "mana",
            oracle_ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("oracle_ids", body.RootElement.GetProperty("parameter").GetString());
        Assert.Equal(201, body.RootElement.GetProperty("value").GetInt32());
        Assert.Equal(200, body.RootElement.GetProperty("maximum").GetInt32());
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData(3, 3)]
    [InlineData(500, 200)]
    public async Task K_defaults_to_50_and_caps_at_200(int? k, int expected)
    {
        var connectionString = await NewDatabaseAsync();
        using var app = NewApp(connectionString, new FakeOllama());
        using var client = app.CreateClient();
        await SeedAsync(connectionString, [.. Enumerable.Range(0, 210).Select(i => Seeded($"Padding {i}", faces: [1024]))]);

        var response = await PostAsync(client, "/cards/semantic-search", new { query = "padding", k });

        Assert.Equal(expected, response.GetProperty("candidates").GetArrayLength());
    }

    [Fact]
    public async Task The_semantic_path_is_one_filtered_knn_statement()
    {
        await using var db = postgres.CreateCorpusContext();
        var request = new CardSemanticSearchRequest { Query = "q", Types = ["Creature"], Tags = ["ramp"], K = 7 };

        var sql = CardSearchEndpoints.Knn(
            db.LegalCandidates(Format.Commander, new DateOnly(2025, 4, 21), Role.Main), db.SourceProfiles, db.TagAliases, db.PopularityRanks, request, new Vector(new float[1024]))
            .ToQueryString();

        // One SELECT over the boundary function, its predicates in the
        // WHERE, and the KNN ORDER BY / LIMIT outermost — no wrapper. The
        // order is each card's nearest-face distance, an aggregate that no
        // index can serve.
        Assert.Contains("FROM legal_candidates(", sql);
        Assert.Matches(@"(?s)\bWHERE\b.*\nORDER BY \(\s*SELECT min\([^\n]*<=>[^\n]*\n.*\nLIMIT [^\n]*\s*$", sql.Replace("\r\n", "\n"));
        Assert.Single(Regex.Matches(sql, "ORDER BY"));
        Assert.DoesNotContain("card_ruling", sql);
    }

    [Fact]
    public async Task An_unknown_pinned_banlist_version_is_a_400_on_both_endpoints()
    {
        var connectionString = await NewDatabaseAsync();
        using var app = NewApp(connectionString, new FakeOllama());
        using var client = app.CreateClient();
        await SeedAsync(connectionString, Seeded("Sol Ring", faces: [1024]));

        foreach (var route in new[] { "/cards/search", "/cards/semantic-search" })
        {
            var response = await client.PostAsJsonAsync(route, new { query = "mana", banlist_version = "1999-01-01" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("banlist_version", body.RootElement.GetProperty("parameter").GetString());
            Assert.Equal("1999-01-01", body.RootElement.GetProperty("value").GetString());
        }
    }

    [Theory]
    [InlineData("/cards/semantic-search")]
    [InlineData("/rules/search")]
    public async Task A_whitespace_only_query_is_a_400_on_both_semantic_routes(string route)
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(route, new { query = " \r\n " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("query", body.RootElement.GetProperty("parameter").GetString());
    }

    [Theory]
    [InlineData("/cards/search", "limit", 0)]
    [InlineData("/cards/search", "limit", -5)]
    [InlineData("/cards/semantic-search", "k", 0)]
    [InlineData("/cards/semantic-search", "k", -1)]
    public async Task A_limit_or_k_below_one_is_a_400(string route, string parameter, int value)
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(route, new Dictionary<string, object> { ["query"] = "mana", [parameter] = value });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(parameter, body.RootElement.GetProperty("parameter").GetString());
        Assert.Equal(value, body.RootElement.GetProperty("value").GetInt32());
    }

    [Fact]
    public void The_property_harness_discovers_both_endpoints()
    {
        using var app = new ArchiveApp();

        var discovered = IndexFuzzHarness.Discover(app);

        Assert.Contains(("/cards/search", typeof(CardSearchRequest)), discovered);
        Assert.Contains(("/cards/semantic-search", typeof(CardSemanticSearchRequest)), discovered);
    }

    [Fact]
    public async Task Enters_tapped_filters_lands_by_their_stored_facet_on_both_endpoints()
    {
        var connectionString = await NewDatabaseAsync();
        using var app = NewApp(connectionString, new FakeOllama());
        using var client = app.CreateClient();
        var tower = Seeded("Command Tower", typeLine: "Land", rank: 1, faces: [1024], entersTapped: EntersTappedState.Untapped);
        var brass = Seeded("City of Brass", typeLine: "Land", rank: 2, faces: [1024], entersTapped: EntersTappedState.Untapped);
        var bridge = Seeded("Command Bridge", typeLine: "Land", rank: 3, faces: [1024], entersTapped: EntersTappedState.Tapped);
        var glacier = Seeded("Glacial Fortress", typeLine: "Land", rank: 4, faces: [1024], entersTapped: EntersTappedState.Conditional);
        // An unmatched land has a row with no facet; a mana rock has a row too.
        var odd = Seeded("Odd Land", typeLine: "Land", rank: 5, faces: [1024], profile: true);
        var ring = Seeded("Sol Ring", rank: 6, faces: [1024], profile: true);
        await SeedAsync(connectionString, tower, brass, bridge, glacier, odd, ring);

        foreach (var route in new[] { "/cards/search", "/cards/semantic-search" })
        {
            var untapped = await PostAsync(client, route, new { query = "land", enters_tapped = "untapped", types = new[] { "Land" } });
            Assert.Equal(new[] { tower.OracleId, brass.OracleId }.Order(), Ids(untapped).Order());
            Assert.All(untapped.GetProperty("candidates").EnumerateArray(),
                hit => Assert.Equal("untapped", hit.GetProperty("enters_tapped").GetString()));

            var conditional = await PostAsync(client, route, new { query = "land", enters_tapped = "conditional" });
            Assert.Equal([glacier.OracleId], Ids(conditional));

            var all = await PostAsync(client, route, new { query = "land" });
            var states = all.GetProperty("candidates").EnumerateArray().ToDictionary(
                c => c.GetProperty("card").GetProperty("id").GetGuid(),
                c => c.TryGetProperty("enters_tapped", out var state) ? state.GetString() : null);
            Assert.Equal("tapped", states[bridge.OracleId]);
            Assert.Null(states[odd.OracleId]);
            Assert.Null(states[ring.OracleId]);
        }
    }

    [Fact]
    public async Task An_unknown_enters_tapped_value_is_a_400_on_both_endpoints()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        foreach (var route in new[] { "/cards/search", "/cards/semantic-search" })
        {
            var response = await client.PostAsJsonAsync(route, new { query = "land", enters_tapped = "sometimes" });

            // Outside the enum vocabulary the body never binds: a 400, never a silent no-filter fallback.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Collection_scope_keeps_owned_cards_and_reports_availability_on_both_endpoints()
    {
        var connectionString = await NewDatabaseAsync();
        using var app = NewApp(connectionString, new FakeOllama());
        using var client = app.CreateClient();
        var forest = Seeded("Forest", copyLimit: null, owned: 3, faces: [1024]);
        var dwarves = Seeded("Seven Dwarves", copyLimit: 7, owned: 9, faces: [1024]);
        var ring = Seeded("Sol Ring", owned: 4, faces: [1024]);
        var unowned = Seeded("Mana Crypt", faces: [1024]);
        await SeedAsync(connectionString, forest, dwarves, ring, unowned);

        foreach (var route in new[] { "/cards/search", "/cards/semantic-search" })
        {
            var scoped = await PostAsync(client, route, new { query = "mana", collection_scope = true });
            var available = scoped.GetProperty("candidates").EnumerateArray().ToDictionary(
                c => c.GetProperty("card").GetProperty("id").GetGuid(),
                c => c.GetProperty("available").GetInt32());
            Assert.Equal(3, available[forest.OracleId]);
            Assert.Equal(7, available[dwarves.OracleId]);
            Assert.Equal(1, available[ring.OracleId]);
            Assert.Equal(3, available.Count);

            var candidates = (await PostAsync(client, route, new { query = "mana" })).GetProperty("candidates").EnumerateArray().ToList();
            Assert.Equal(4, candidates.Count);
            Assert.All(candidates, c => Assert.False(c.TryGetProperty("available", out _)));
        }
    }

    [Fact]
    public async Task A_mismatched_embedding_state_disables_semantic_search_while_structured_search_keeps_working()
    {
        var connectionString = await NewDatabaseAsync();
        var fake = new FakeOllama();
        using var app = NewApp(connectionString, fake);
        using var client = app.CreateClient();
        var ring = Seeded("Sol Ring", faces: [1024]);
        await SeedAsync(connectionString, ring);
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            (await db.EmbeddingStates.SingleAsync()).PrefixSetVersion = "0000000000000000";
            await db.SaveChangesAsync();
        }

        var semantic = await PostAsync(client, "/cards/semantic-search", new { query = "mana" });
        Assert.False(semantic.GetProperty("semantic").GetProperty("enabled").GetBoolean());
        Assert.Contains("0000000000000000", semantic.GetProperty("semantic").GetProperty("disabled_reason").GetString());
        Assert.Equal(0, semantic.GetProperty("candidates").GetArrayLength());
        Assert.Equal(Version, semantic.GetProperty("versions").GetProperty("banlist_version").GetString());
        Assert.Empty(fake.Requests);

        Assert.Equal([ring.OracleId], Ids(await PostAsync(client, "/cards/search", new { })));
    }

    [Fact]
    public async Task Stale_and_illegal_cards_never_appear_on_either_endpoint()
    {
        var connectionString = await NewDatabaseAsync();
        using var app = NewApp(connectionString, new FakeOllama());
        using var client = app.CreateClient();
        var live = Seeded("Sol Ring", faces: [1024]);
        var stale = Seeded("Old Ring", stale: true, faces: [1024]);
        var banned = Seeded("Mana Crypt", legal: false, faces: [1024]);
        await SeedAsync(connectionString, live, stale, banned);

        foreach (var route in new[] { "/cards/search", "/cards/semantic-search" })
        {
            Assert.Equal([live.OracleId], Ids(await PostAsync(client, route, new { query = "mana" })));
            Assert.Equal([live.OracleId], Ids(await PostAsync(client, route, new
            {
                query = "mana",
                oracle_ids = new[] { live.OracleId, stale.OracleId, banned.OracleId },
            })));
        }
    }

    private Task<string> NewDatabaseAsync() => postgres.CreateDatabaseAsync($"card_search_{Guid.NewGuid():N}");

    private static ArchiveApp NewApp(string connectionString, FakeOllama fake)
        => new(autoMigrate: true, connectionString, services =>
            services.AddHttpClient<OllamaEmbedder>().ConfigurePrimaryHttpMessageHandler(() => fake));

    private static Guid[] Ids(JsonElement response)
        => [.. response.GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("card").GetProperty("id").GetGuid())];

    private static async Task<JsonElement> PostAsync(HttpClient client, string route, object body)
    {
        var response = await client.PostAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    /// <summary>One card to seed: <paramref name="faces"/> gives each face
    /// document's embedding as its count of leading ones (cosine distance
    /// from <see cref="FakeOllama"/>'s all-ones query grows as fewer are set;
    /// empty seeds no document).</summary>
    private static Seed Seeded(
        string name,
        string typeLine = "Artifact",
        decimal manaValue = 1,
        int? rank = null,
        string[]? tags = null,
        string[]? keywords = null,
        int? copyLimit = 1,
        int owned = 0,
        bool stale = false,
        bool legal = true,
        int[]? faces = null,
        EntersTappedState? entersTapped = null,
        bool profile = false,
        bool inBrawl = true)
    {
        var card = TestCards.Minimal(Guid.NewGuid());
        card.Name = name;
        card.TypeLine = typeLine;
        card.CardTypes = [.. typeLine.Split(" — ")[0].Split(" // ")[0].Split(' ').Where(t => t != "Legendary")];
        card.Supertypes = typeLine.StartsWith("Legendary") ? ["Legendary"] : [];
        card.Subtypes = typeLine.Contains(" — ") ? typeLine.Split(" — ")[1].Split(" // ")[0].Split(' ') : [];
        card.ManaValue = manaValue;
        card.EdhrecRank = rank;
        card.Keywords = keywords ?? [];
        card.CopyLimit = copyLimit;
        card.Stale = stale;
        card.OracleTags = [.. (tags ?? []).Select(t => new CardOracleTag { Tag = t })];
        card.Documents = [.. (faces ?? []).Select((ones, i) => new CardFaceDocument
        {
            FaceIndex = i,
            Text = name,
            Embedding = new Vector(Enumerable.Range(0, 1024).Select(j => j < ones ? 1f : 0f).ToArray()),
        })];
        return new Seed(card, owned, legal, profile || entersTapped is not null, entersTapped, inBrawl);
    }

    /// <summary><paramref name="Profile"/> seeds a source-profile row
    /// carrying <paramref name="EntersTapped"/> (null = an unmatched land or
    /// a nonland producer). <paramref name="InBrawl"/> puts the card in the
    /// Brawl pool, legal in every role.</summary>
    private sealed record Seed(
        Card Card, int Owned, bool Legal, bool Profile = false, EntersTappedState? EntersTapped = null, bool InBrawl = true)
    {
        public Guid OracleId => Card.OracleId;
    }

    /// <summary>One banlist version, every card legal in every role unless
    /// banned, owned rows, and the embedding_state record matching
    /// configuration.</summary>
    private static async Task SeedAsync(string connectionString, params Seed[] seeds)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        var version = new BanlistVersion
        {
            EffectiveDate = DateOnly.Parse(Version),
            DatePrecision = DatePrecision.Day,
            AnnouncementUrl = "https://example.org/2025",
        };
        db.BanlistVersions.Add(version);
        db.Cards.AddRange(seeds.Select(s => s.Card));
        db.EmbeddingStates.Add(EmbeddingModel.Qwen3.State.ToStored());
        await db.SaveChangesAsync();

        foreach (var seed in seeds)
        {
            db.Legalities.AddRange(Enum.GetValues<Role>().Select(role => new Legality
            {
                CardId = seed.Card.Id,
                BanlistVersionId = version.Id,
                Role = role,
                Status = seed.Legal ? LegalityStatus.Legal : LegalityStatus.NotLegal,
            }));
            db.Legalities.AddRange(Enum.GetValues<Role>().Select(role => new Legality
            {
                CardId = seed.Card.Id,
                Format = Format.Brawl,
                BanlistVersionId = null,
                Role = role,
                Status = seed.InBrawl ? LegalityStatus.Legal : LegalityStatus.NotLegal,
                LegalityAsOf = BrawlAsOf,
            }));
            if (seed.Owned > 0)
            {
                db.CollectionCards.Add(new CollectionCard { OracleId = seed.OracleId, Quantity = seed.Owned });
            }

            if (seed.Profile)
            {
                db.SourceProfiles.Add(new SourceProfile { CardId = seed.Card.Id, EntersTapped = seed.EntersTapped });
            }
        }

        await db.SaveChangesAsync();
    }
}
