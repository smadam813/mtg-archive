using System.Net;
using System.Text.Json;

namespace MtgArchive.Tests;

public class OpenApiTests
{
    [Fact]
    public async Task OpenapiJson_serves_the_machine_readable_contract()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("openapi", out _));
        Assert.True(doc.RootElement.TryGetProperty("paths", out var paths));
        Assert.True(paths.TryGetProperty("/healthz", out _));
    }

    [Fact]
    public async Task Ingest_routes_are_in_the_contract()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/ingest").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/ingest/runs/{id}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/ingest/runs/latest").TryGetProperty("get", out _));
    }

    [Fact]
    public async Task Card_reference_routes_are_in_the_contract()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");
        var byId = paths.GetProperty("/cards/{oracle_id}").GetProperty("get");
        var byName = paths.GetProperty("/cards").GetProperty("get");
        Assert.Contains("banlist_version", QueryParameters(byId));
        Assert.Contains("name", QueryParameters(byName));
        Assert.Contains("banlist_version", QueryParameters(byName));
    }

    [Fact]
    public async Task Collection_routes_are_in_the_contract()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var collection = doc.RootElement.GetProperty("paths").GetProperty("/collection");
        Assert.True(collection.TryGetProperty("put", out _));
        Assert.True(collection.TryGetProperty("get", out _));

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal(["manabox_csv", "list"], EnumLiterals(schemas, "CollectionImportFormat"));
        Assert.Equal(["no_match", "ambiguous"], EnumLiterals(schemas, "QuarantineReason"));
    }

    [Fact]
    public async Task Rules_reference_route_is_in_the_contract()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var get = doc.RootElement.GetProperty("paths").GetProperty("/rules/{number}").GetProperty("get");
        Assert.Contains("number", get.GetProperty("parameters").EnumerateArray()
            .Where(p => p.GetProperty("in").GetString() == "path")
            .Select(p => p.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Rules_search_route_is_in_the_contract()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("paths").GetProperty("/rules/search").TryGetProperty("post", out _));
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("RulesSearchRequest").GetProperty("properties");
        Assert.Contains("trims it and unifies CRLF to LF before embedding", request.GetProperty("query").GetProperty("description").GetString());
        Assert.True(request.TryGetProperty("k", out _));
        var hit = schemas.GetProperty("RulesSearchHit").GetProperty("properties");
        Assert.Equal("#/components/schemas/Citation", hit.GetProperty("citation").GetProperty("$ref").GetString());
        Assert.True(schemas.GetProperty("RulesSearchResponse").GetProperty("properties").TryGetProperty("semantic", out _));
    }

    [Fact]
    public async Task Card_search_routes_are_in_the_contract()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/cards/search").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/cards/semantic-search").TryGetProperty("post", out _));
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var structured = schemas.GetProperty("CardSearchRequest").GetProperty("properties");
        foreach (var member in new[]
        {
            "format", "banlist_version", "role", "color_identity", "collection_scope",
            "oracle_ids", "name", "types", "keywords", "tags", "min_mana_value", "max_mana_value", "enters_tapped", "limit", "offset",
        })
        {
            Assert.True(structured.TryGetProperty(member, out _), $"card search request lacks '{member}'");
        }

        var semantic = schemas.GetProperty("CardSemanticSearchRequest").GetProperty("properties");
        foreach (var member in new[] { "query", "k", "oracle_ids", "tags", "enters_tapped", "collection_scope", "format" })
        {
            Assert.True(semantic.TryGetProperty(member, out _), $"semantic search request lacks '{member}'");
        }

        Assert.Contains("trims it and unifies CRLF to LF before embedding", semantic.GetProperty("query").GetProperty("description").GetString());

        Assert.True(schemas.GetProperty("CardSearchHit").GetProperty("properties").TryGetProperty("enters_tapped", out _));
        // The hit's facet is nullable, so the schema lists null beside the states.
        Assert.Equal(["untapped", "tapped", "conditional"], EnumLiterals(schemas, "EntersTappedState").Where(v => v is not null));

        var hit = schemas.GetProperty("CardSearchHit").GetProperty("properties");
        Assert.Equal("#/components/schemas/Citation", hit.GetProperty("card").GetProperty("$ref").GetString());
        Assert.True(hit.TryGetProperty("legality", out _));
        Assert.True(hit.TryGetProperty("popularity_rank", out _));
        Assert.False(hit.TryGetProperty("rulings", out _));
        Assert.True(schemas.GetProperty("CardSemanticSearchResponse").GetProperty("properties")
            .TryGetProperty("semantic", out _));
    }

    // The frozen card-detail shape: per-role legality is fully
    // specified — nullable until its producing issue populates it.
    [Fact]
    public async Task Card_detail_schema_freezes_the_legality_shape()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        var detail = schemas.GetProperty("CardDetailResponse").GetProperty("properties");
        foreach (var member in new[]
        {
            "oracle_id", "name", "faces", "legality", "rulings", "tags", "taggings",
            "copy_limit", "stale", "versions", "digital_only",
        })
        {
            Assert.True(detail.TryGetProperty(member, out _), $"card detail lacks '{member}'");
        }

        // digital_only is a required boolean that states the derived rule.
        Assert.Equal("boolean", detail.GetProperty("digital_only").GetProperty("type").GetString());
        Assert.Contains("not_legal", detail.GetProperty("digital_only").GetProperty("description").GetString());
        Assert.Contains("digital_only",
            schemas.GetProperty("CardDetailResponse").GetProperty("required").EnumerateArray().Select(e => e.GetString()));

        var tagging = schemas.GetProperty("CardTagging").GetProperty("properties");
        Assert.True(tagging.TryGetProperty("inherited", out _));
        Assert.True(tagging.TryGetProperty("weight", out _));

        var legality = schemas.GetProperty("RoleLegality").GetProperty("properties");
        Assert.Equal("#/components/schemas/Role", legality.GetProperty("role").GetProperty("$ref").GetString());
        Assert.Equal("#/components/schemas/LegalityStatus",
            legality.GetProperty("status").GetProperty("$ref").GetString());
        Assert.Contains("#/components/schemas/Citation",
            legality.GetProperty("ban_event").GetRawText());
        // Each row names its format; a Brawl row holds its legality citation
        // and the citation target fields.
        Assert.Contains("#/components/schemas/Format", legality.GetProperty("format").GetRawText());
        foreach (var member in new[] { "scryfall_key", "scryfall_uri", "legality_as_of" })
        {
            Assert.True(legality.TryGetProperty(member, out _), $"legality row lacks '{member}'");
        }

        Assert.Contains("#/components/schemas/Citation", legality.GetProperty("citation").GetRawText());
        Assert.Equal(["card", "rule", "ban_event", "legality", "snapshot"], EnumLiterals(schemas, "CitationType"));

        Assert.Equal(["commander", "companion", "main"], EnumLiterals(schemas, "Role"));
        Assert.Equal(["legal", "banned", "not_legal", "not_eligible"], EnumLiterals(schemas, "LegalityStatus"));
        Assert.False(detail.TryGetProperty("staple", out _));
        Assert.False(schemas.TryGetProperty("StapleStatus", out _));
    }

    [Fact]
    public async Task The_authoring_queue_route_is_gone()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("paths").TryGetProperty("/authoring-queue", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/authoring-queue")).StatusCode);
    }

    [Fact]
    public async Task Banlist_routes_are_in_the_contract_with_verbatim_enum_literals()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/banlist/versions").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/banlist/{version}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/ban-events/{id}").TryGetProperty("get", out _));

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal(["ban", "unban"], EnumLiterals(schemas, "BanAction"));
        Assert.Equal(["all", "commander_only", "companion"], EnumLiterals(schemas, "BanScope"));
        Assert.Equal(["day", "month"], EnumLiterals(schemas, "DatePrecision"));
    }

    // The corpus-state shape: the GC snapshot axis and
    // embedding_state are fully specified now — null/absent until their
    // producing issues populate them — so no shape change is needed later.
    [Fact]
    public async Task Corpus_state_route_is_in_the_contract_with_the_frozen_shape()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("paths").GetProperty("/corpus-state")
            .TryGetProperty("get", out _));

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var state = schemas.GetProperty("CorpusStateResponse").GetProperty("properties");
        foreach (var member in new[] { "versions", "embedding_state", "last_phase_runs", "freshness" })
        {
            Assert.True(state.TryGetProperty(member, out _), $"corpus state lacks '{member}'");
        }

        Assert.True(schemas.GetProperty("VersionsBlock").GetProperty("properties")
            .TryGetProperty("gc_snapshot", out _));
        Assert.True(schemas.GetProperty("VersionsBlock").GetProperty("properties")
            .TryGetProperty("format", out _));
        Assert.True(schemas.GetProperty("VersionsBlock").GetProperty("properties")
            .TryGetProperty("legality_as_of", out _));
        foreach (var member in new[] { "popularity_source", "popularity_as_of" })
        {
            Assert.True(schemas.GetProperty("VersionsBlock").GetProperty("properties").TryGetProperty(member, out _), $"versions lacks '{member}'");
        }

        var embedding = schemas.GetProperty("CorpusEmbeddingState").GetProperty("properties");
        foreach (var member in new[] { "model_tag", "dimensions", "prefix_set_version" })
        {
            Assert.True(embedding.TryGetProperty(member, out _), $"embedding state lacks '{member}'");
        }

        var freshness = schemas.GetProperty("SourceStamps").GetProperty("properties");
        foreach (var member in new[] { "scryfall_updated_at", "cr_effective_date", "cr_text_sha256", "curated_file_hashes" })
        {
            Assert.True(freshness.TryGetProperty(member, out _), $"freshness lacks '{member}'");
        }
    }

    [Fact]
    public async Task Tier1_report_route_is_in_the_contract()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("paths").GetProperty("/tier1-report").TryGetProperty("post", out _));
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("Tier1ReportRequest").GetProperty("properties");
        foreach (var member in new[] { "decklist", "categories", "max_turn", "format" })
        {
            Assert.True(request.TryGetProperty(member, out _), $"tier-1 request lacks '{member}'");
        }

        var category = schemas.GetProperty("CategoryRequest").GetProperty("properties");
        Assert.True(category.TryGetProperty("name", out _));
        Assert.True(category.TryGetProperty("cards", out _));
        Assert.True(category.TryGetProperty("k", out _));
        var report = schemas.GetProperty("Tier1Report").GetProperty("properties");
        foreach (var member in new[]
        {
            "input", "assumptions", "category_draws", "mana_sources", "demand_vs_supply", "land_drops", "mulligan_looks", "versions",
        })
        {
            Assert.True(report.TryGetProperty(member, out _), $"tier-1 report lacks '{member}'");
        }

        Assert.Equal(["mono", "dual", "three_plus"], EnumLiterals(schemas, "ColorSpread"));
        var manaSources = schemas.GetProperty("ManaSourceReport").GetProperty("properties");
        foreach (var member in new[] { "sources", "by_color", "excluded" })
        {
            Assert.True(manaSources.TryGetProperty(member, out _), $"mana_sources lacks '{member}'");
        }

        var excluded = schemas.GetProperty("ExcludedSource").GetProperty("properties");
        foreach (var member in new[] { "oracle_id", "name", "quantity", "reason" })
        {
            Assert.True(excluded.TryGetProperty(member, out _), $"excluded source lacks '{member}'");
        }

        Assert.Equal(["opponent_dependent", "token_to_another_player"], EnumLiterals(schemas, "ExclusionReason"));
    }

    [Fact]
    public async Task Deck_verdict_route_is_in_the_contract_and_the_decklist_has_a_companion()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("paths").GetProperty("/deck-verdict").TryGetProperty("post", out _));
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("DeckVerdictRequest").GetProperty("properties");
        foreach (var member in new[] { "decklist", "format", "banlist_version" })
        {
            Assert.True(request.TryGetProperty(member, out _), $"deck verdict request lacks '{member}'");
        }

        var decklist = schemas.GetProperty("Decklist").GetProperty("properties");
        Assert.Equal(["commanders", "companion", "entries"], decklist.EnumerateObject().Select(p => p.Name).ToArray());
        var verdict = schemas.GetProperty("DeckVerdict").GetProperty("properties");
        foreach (var member in new[] { "status", "card_findings", "shape_findings", "deck_size", "required_deck_size", "versions" })
        {
            Assert.True(verdict.TryGetProperty(member, out _), $"deck verdict lacks '{member}'");
        }

        Assert.Equal(["legal", "incomplete", "illegal", "unknown"], EnumLiterals(schemas, "DeckVerdictStatus"));
        Assert.Equal(
            ["too_few_cards", "no_commander", "too_many_cards", "pairing", "copy_limit", "color_identity", "basic_land_types"],
            EnumLiterals(schemas, "ShapeFindingKind"));
        var finding = schemas.GetProperty("CardFinding").GetProperty("properties");
        Assert.Equal(["card", "role", "status", "ban_event", "citation"], finding.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task Decklist_parse_route_is_in_the_contract_and_returns_the_decklist_body()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("paths").GetProperty("/decklist-parse").TryGetProperty("post", out _));
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Equal(["text"], schemas.GetProperty("DecklistParseRequest").GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(["decklist", "notes"], schemas.GetProperty("DecklistParseResponse").GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).ToArray());
    }

    // Every route that resolves a decklist reports its A- rewrites.
    [Fact]
    public async Task Every_deck_route_response_carries_the_name_rewrites()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var schema in new[]
            { "DeckVerdict", "Tier1Report", "SimulationEnvelope", "BracketClassificationResponse", "CommanderAlternativesResponse" })
        {
            Assert.True(schemas.GetProperty(schema).GetProperty("properties").TryGetProperty("name_rewrites", out _),
                $"{schema} lacks 'name_rewrites'");
        }

        Assert.Equal(["name", "card"], schemas.GetProperty("NameRewrite").GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task Simulate_route_is_in_the_contract_with_the_envelope_shape()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("paths").GetProperty("/simulate").TryGetProperty("post", out _));
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("SimulateRequest").GetProperty("properties");
        foreach (var member in new[] { "decklist", "goal", "runs", "seed", "turn_horizon", "disruption_profile", "disruption_mode", "play_policy", "banlist_version", "format" })
        {
            Assert.True(request.TryGetProperty(member, out _), $"simulate request lacks '{member}'");
        }

        var envelope = schemas.GetProperty("SimulationEnvelope").GetProperty("properties");
        foreach (var member in new[]
        {
            "goal", "probability", "runs", "successes", "base_seed", "turn_horizon", "policies", "engine", "curve",
            "exemplars", "disruption_profile", "disruption_mode", "disruption", "engine_coverage", "engine_errors", "deck_verdict",
            "input", "simplifications", "versions",
        })
        {
            Assert.True(envelope.TryGetProperty(member, out _), $"simulation envelope lacks '{member}'");
        }

        Assert.Contains("pairs", request.GetProperty("runs").GetProperty("description").GetString());
        var disruption = schemas.GetProperty("DisruptionReport").GetProperty("properties");
        foreach (var member in new[] { "baseline", "disrupted", "resilience_delta", "events", "assumptions", "response_cards" })
        {
            Assert.True(disruption.TryGetProperty(member, out _), $"disruption report lacks '{member}'");
        }

        var arm = schemas.GetProperty("ArmReport").GetProperty("properties");
        foreach (var member in new[] { "probability", "successes", "curve", "exemplars" })
        {
            Assert.True(arm.TryGetProperty(member, out _), $"arm report lacks '{member}'");
        }

        var eventReport = schemas.GetProperty("EventReport").GetProperty("properties");
        foreach (var member in new[] { "kind", "turn", "fired_rate", "mean_fired_turn", "unused", "answered_rate", "not_cast_rate" })
        {
            Assert.True(eventReport.TryGetProperty(member, out _), $"event report lacks '{member}'");
        }

        // Under interactive disruption "fired" means the opponent cast the card.
        Assert.Contains("opponent cast", eventReport.GetProperty("fired_rate").GetProperty("description").GetString());

        var exemplarEvent = schemas.GetProperty("ExemplarEventLine").GetProperty("properties");
        foreach (var member in new[] { "turn", "kind", "victim", "hit", "answered_by" })
        {
            Assert.True(exemplarEvent.TryGetProperty(member, out _), $"exemplar event lacks '{member}'");
        }

        Assert.True(schemas.GetProperty("ExemplarLine").GetProperty("properties").TryGetProperty("events", out _));

        // The interactive envelope names the deck's responses it could not profile.
        Assert.True(schemas.GetProperty("EngineCoverage").GetProperty("properties").TryGetProperty("unprofiled_responses", out _));
        Assert.Contains("lower bound", eventReport.GetProperty("answered_rate").GetProperty("description").GetString());

        var probability = schemas.GetProperty("FloorProbability").GetProperty("properties");
        Assert.True(probability.TryGetProperty("floor", out _));
        Assert.True(probability.TryGetProperty("label", out _));
        Assert.True(probability.TryGetProperty("confidence_interval", out _));
        var goal = schemas.GetProperty("GoalWire").GetProperty("properties");
        foreach (var member in new[] { "kind", "card", "by_turn", "goals" })
        {
            Assert.True(goal.TryGetProperty(member, out _), $"goal lacks '{member}'");
        }

        Assert.False(envelope.TryGetProperty("legality", out _));
        Assert.Equal("#/components/schemas/DeckVerdict", envelope.GetProperty("deck_verdict").GetProperty("$ref").GetString());
        Assert.False(schemas.TryGetProperty("DecklistLegality", out _));
        Assert.Equal(["fastest", "typical", "failure"], EnumLiterals(schemas, "ExemplarKind"));
        Assert.False(schemas.GetProperty("VersionsBlock").GetProperty("properties").TryGetProperty("staple_cut_version", out _));
        Assert.False(request.TryGetProperty("staple_cut_version", out _));
        Assert.False(envelope.TryGetProperty("dsl_coverage", out _));
        Assert.False(schemas.TryGetProperty("DslCoverage", out _));
    }

    [Fact]
    public async Task Commander_alternatives_route_is_in_the_contract()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("paths").GetProperty("/commander-alternatives").TryGetProperty("post", out _));
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("CommanderAlternativesRequest").GetProperty("properties");
        foreach (var member in new[] { "decklist", "collection_scope", "format", "banlist_version", "limit" })
        {
            Assert.True(request.TryGetProperty(member, out _), $"commander-alternatives request lacks '{member}'");
        }

        var alternativesResponse = schemas.GetProperty("CommanderAlternativesResponse").GetProperty("properties");
        foreach (var member in new[] { "original", "candidates", "versions" })
        {
            Assert.True(alternativesResponse.TryGetProperty(member, out _), $"commander-alternatives response lacks '{member}'");
        }

        var candidate = schemas.GetProperty("CommanderAlternative").GetProperty("properties");
        foreach (var member in new[]
        {
            "card", "color_identity", "popularity_rank", "available", "retained_count", "lost_count",
            "color_identity_distance", "retained", "lost", "companion",
        })
        {
            Assert.True(candidate.TryGetProperty(member, out _), $"commander alternative lacks '{member}'");
        }

        Assert.Equal("#/components/schemas/Citation", candidate.GetProperty("card").GetProperty("$ref").GetString());
        var lost = schemas.GetProperty("LostCard").GetProperty("properties");
        Assert.Equal("#/components/schemas/Citation", lost.GetProperty("citation").GetProperty("$ref").GetString());
        Assert.Equal(["color_identity", "banned", "not_legal"], EnumLiterals(schemas, "LossReason"));
        var companion = schemas.GetProperty("CompanionCard").GetProperty("properties");
        foreach (var member in new[] { "card", "retained", "reason", "citation" })
        {
            Assert.True(companion.TryGetProperty(member, out _), $"companion entry lacks '{member}'");
        }
    }

    [Fact]
    public async Task Responses_carry_a_schema_in_the_contract()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ok = doc.RootElement.GetProperty("paths").GetProperty("/healthz")
            .GetProperty("get").GetProperty("responses").GetProperty("200");
        Assert.True(ok.TryGetProperty("content", out var content));
        Assert.True(content.TryGetProperty("application/json", out var json));
        Assert.True(json.TryGetProperty("schema", out _));
    }

    private static string[] QueryParameters(JsonElement operation)
        => [.. operation.GetProperty("parameters").EnumerateArray()
            .Where(p => p.GetProperty("in").GetString() == "query")
            .Select(p => p.GetProperty("name").GetString()!)];

    [Fact]
    public async Task Bracket_classification_and_its_reference_endpoints_are_documented()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/openapi.json");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/bracket-classification").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/bracket-spec/versions").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/bracket-spec/{date}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/game-changers/versions").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/game-changers/{date}").TryGetProperty("get", out _));
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("BracketClassificationRequest").GetProperty("properties");
        foreach (var member in new[] { "decklist", "target_bracket", "bracket_spec_version", "gc_snapshot", "assumptions", "format" })
        {
            Assert.True(request.TryGetProperty(member, out _), $"bracket classification request lacks '{member}'");
        }

        var classification = schemas.GetProperty("BracketClassificationResponse").GetProperty("properties");
        Assert.Equal(["bracket_floor", "assumed_bracket", "reasons", "assumptions", "citations", "versions", "name_rewrites"],
            classification.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(["declared", "unknown"], EnumLiterals(schemas, "AssumedBracketStatus"));
        var reason = schemas.GetProperty("BarometerReason").GetProperty("properties");
        Assert.Equal("#/components/schemas/Citation",
            reason.GetProperty("cards").GetProperty("items").GetProperty("$ref").GetString());
    }

    private static string[] EnumLiterals(JsonElement schemas, string name)
        => [.. schemas.GetProperty(name).GetProperty("enum").EnumerateArray().Select(v => v.GetString()!)];

}
