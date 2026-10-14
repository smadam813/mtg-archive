using Microsoft.EntityFrameworkCore;
using MtgArchive.Api;
using MtgArchive.Banlist;
using MtgArchive.Bracket;
using MtgArchive.Cards;
using MtgArchive.CollectionImport;
using MtgArchive.Data;
using MtgArchive.Embedding;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Xmage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options => WireJson.Apply(options.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<ArchiveDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Archive"),
    npgsql => npgsql.UseVector()));
builder.Services.AddSingleton<BanlistVersionAxis>();
builder.Services.AddSingleton<BracketVersionAxis>();

// The reference surface: card detail, CR rules, and banlist history — the
// resolution targets for card, rule, ban-event, and banlist-version
// citations.
builder.Services.AddSingleton<IEndpointMapper, CardEndpoints>();
builder.Services.AddSingleton<IEndpointMapper, RulesEndpoints>();
builder.Services.AddSingleton<IEndpointMapper, BanlistEndpoints>();

builder.Services.AddSingleton<IEndpointMapper, CollectionEndpoints>();

// Simulation tier 1: the exact closed-form report.
builder.Services.AddSingleton<IEndpointMapper, Tier1Endpoints>();
builder.Services.AddSingleton<IEndpointMapper, DeckVerdictEndpoints>();
builder.Services.AddSingleton<IEndpointMapper, DecklistParseEndpoints>();
builder.Services.AddSingleton<IEndpointMapper, BracketEndpoints>();

// Simulation tier 2 and 3: the Monte Carlo batch on the XMage sidecar and its result envelope.
// The XMage sidecar's connection pool (ADR 0011): one per service, shared
// by every batch. It connects on first use, so the service starts without
// the sidecar.
builder.Services.AddSingleton(sp => new XmageSidecar(XmageOptions.From(sp.GetRequiredService<IConfiguration>())));
builder.Services.AddSingleton<IEndpointMapper, SimulateEndpoints>();

// The Index: the two card query endpoints (structured, semantic).
builder.Services.AddSingleton<IEndpointMapper, CardSearchEndpoints>();

// The one deterministic recommendation query (ADR 0006).
builder.Services.AddSingleton<IEndpointMapper, CommanderAlternativesEndpoints>();

// The ingest run framework: the single-flight background job, its phases
// in canonical order, and the trigger/poll endpoints.
builder.Services.AddHttpClient<ScryfallBulkClient>(ScryfallBulkClient.Configure);
builder.Services.AddTransient<IScryfallBulkSource>(sp => sp.GetRequiredService<ScryfallBulkClient>());
builder.Services.AddScoped<IIngestPhase, FetchValidatePhase>();
builder.Services.AddScoped<IIngestPhase, CardUpsertPhase>();
builder.Services.AddScoped<IIngestPhase, SourceProfilePhase>();
builder.Services.AddScoped<IIngestPhase, CardFeaturePhase>();
builder.Services.AddHttpClient<WotcCrClient>(WotcCrClient.Configure);
builder.Services.AddTransient<ICrSource>(sp => sp.GetRequiredService<WotcCrClient>());
builder.Services.AddScoped<IIngestPhase, CrRefreshPhase>();
builder.Services.AddScoped<IIngestPhase, BanlistReplayPhase>();
builder.Services.AddScoped<IIngestPhase, LegalityRebuildPhase>();
builder.Services.AddScoped<IIngestPhase, PopularityLoadPhase>();
builder.Services.AddScoped<IIngestPhase, BracketLoadPhase>();
builder.Services.AddScoped<IIngestPhase, ReEmbedPhase>();
builder.Services.AddScoped<CorpusStateGate>();
builder.Services.AddSingleton<IngestRunner>();
builder.Services.AddSingleton<IEndpointMapper, IngestEndpoints>();
builder.Services.AddSingleton<IEndpointMapper, CorpusStateEndpoints>();

// Embedding: startup resolves the model and the card document composition
// from configuration once, and an unknown tag or composition fails startup.
// Every vector goes through the one client over the stock ollama container.
// A background task provisions the tag. Tests without Ollama opt out of
// provisioning via Embedding:PullOnStartup.
builder.Services.AddSingleton(EmbeddingModel.Resolve(
    builder.Configuration["Embedding:ModelTag"], builder.Configuration["Embedding:Composition"]));
builder.Services.AddHttpClient<OllamaEmbedder>(OllamaEmbedder.Configure);
builder.Services.AddScoped<EmbeddingStateGate>();
if (builder.Configuration.GetValue("Embedding:PullOnStartup", true))
{
    builder.Services.AddHostedService<EmbeddingModelProvisioner>();
}

var app = builder.Build();

app.UseExceptionHandler();

// Bodiless framework rejections (route-constraint misses like
// /cards/not-a-guid, binding failures) become problem+json through
// IProblemDetailsService, keeping every error application/problem+json.
app.UseStatusCodePages();

// Migrations own all DDL and apply on startup — single user, one instance,
// no rolling deploys. Tests without a database opt out via Database:AutoMigrate.
if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ArchiveDbContext>().Database.Migrate();
}

app.MapGet("/healthz", () => TypedResults.Ok(new Healthz("ok")));

// The canonical machine-readable contract. Every endpoint PR keeps it current
// (docs/api-conventions.md). No Swagger UI.
app.MapOpenApi("/openapi.json");

foreach (var mapper in app.Services.GetServices<IEndpointMapper>())
{
    mapper.Map(app);
}

app.Run();

/// <summary>Compose healthcheck payload.</summary>
public sealed record Healthz(string Status);
