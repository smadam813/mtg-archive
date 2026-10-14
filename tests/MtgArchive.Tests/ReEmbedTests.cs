using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Api;
using MtgArchive.Embedding;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The re-embed phase through the ingest surface against
/// <see cref="FakeOllama"/>: last in the canonical order, every face
/// document and CR chunk embedded through the client on the first run,
/// only changed documents afterwards, rules chunks only on a CR change,
/// stale cards without a document, the embedding state written on
/// completion (and everything re-embedded when it disagrees), a change a
/// failed run committed, and truncation reported rather than accepted.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReEmbedTests(PostgresFixture postgres)
{
    private const string ElvesDocument = "Llanowar Elves\nCreature — Elf Druid\n{T}: Add {G}.\n1/1\n{G}\nTags: mana-dork, ramp";

    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "fixtures", "embedding");

    [Fact]
    public async Task Ingest_embeds_last_then_only_what_changed()
    {
        var (connectionString, source, ollama, cr) = await SetUpAsync();
        using var app = NewApp(connectionString, source, ollama, cr);
        using var client = app.CreateClient();

        var run = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        var phases = run.GetProperty("phases").EnumerateArray().ToList();
        Assert.Equal("re_embed", phases[^1].GetProperty("phase").GetString());
        Assert.All(phases, p => Assert.Equal("succeeded", p.GetProperty("status").GetString()));
        Assert.Empty(run.GetProperty("report").GetProperty("errors").EnumerateArray());

        // 8 face documents (Liliana and Fire // Ice have two each) plus the
        // CR excerpt's 6 rule and 3 glossary chunks, unprefixed under qwen3,
        // every request refusing truncation.
        var inputs = EmbedInputs(ollama);
        Assert.Equal(17, inputs.Count);
        Assert.Contains(ElvesDocument, inputs);
        Assert.Contains(inputs, i => i.StartsWith("100.1. A test game"));
        Assert.Contains(inputs, i => i.StartsWith("Abandon\n"));
        Assert.All(ollama.Requests.Where(r => r.Path == "/api/embed"), r =>
        {
            Assert.Equal("qwen3-embedding:0.6b", r.Body.GetProperty("model").GetString());
            Assert.False(r.Body.GetProperty("truncate").GetBoolean());
        });

        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            // Per-face storage: faces dedupe at query time, not here.
            var liliana = await db.Cards.SingleAsync(c => c.Name.StartsWith("Liliana"));
            var faces = await db.CardFaceDocuments.Where(d => d.CardId == liliana.Id).OrderBy(d => d.FaceIndex).ToListAsync();
            Assert.Equal([0, 1], faces.Select(f => f.FaceIndex));
            Assert.All(faces, f => Assert.NotNull(f.Embedding));
            Assert.Equal(8, await db.CardFaceDocuments.CountAsync(d => d.Embedding != null));
            Assert.Equal(0, await db.CrRuleChunks.CountAsync(c => c.Embedding == null));
            Assert.Equal(0, await db.CrGlossaryChunks.CountAsync(c => c.Embedding == null));
            Assert.Empty(await db.RulesReembedSignals.ToListAsync());
            Assert.Equal(EmbeddingModel.Qwen3.State, CorpusEmbeddingState.Of(await db.EmbeddingStates.SingleAsync()));
        }

        Assert.True((await GateAsync(app)).Enabled);

        // Forced past the no-op gate with nothing changed: nothing embeds.
        ollama.Requests.Clear();
        await WaitForCompletionAsync(client, await TriggerAsync(client, force: true), "succeeded");
        Assert.Empty(EmbedInputs(ollama));

        // Elves' text changes and Sol Ring goes stale: one document embeds,
        // and the stale card's document is gone.
        source.OracleCards = string.Join('\n', File.ReadAllLines(Path.Combine(FixtureDir, "oracle-cards.jsonl"))
            .Where(line => !line.Contains("\"Sol Ring\""))
            .Select(line => line.Replace("{T}: Add {G}.", "{T}: Add {G}{G}.")));
        var report = (await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded")).GetProperty("report");

        Assert.Equal(1, report.GetProperty("changed_cards").GetInt32());
        Assert.Equal([ElvesDocument.Replace("{T}: Add {G}.", "{T}: Add {G}{G}.")], EmbedInputs(ollama));
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(7, await db.CardFaceDocuments.CountAsync());
            Assert.Equal(0, await db.CardFaceDocuments.CountAsync(d => d.Embedding == null));
            Assert.Empty(await db.CardFaceDocuments.Where(d => d.Text.StartsWith("Sol Ring\n")).ToListAsync());
        }

        // A CR change: the replaced chunks re-embed in full, no card documents.
        ollama.Requests.Clear();
        cr.RawText = CrFixtures.RawExcerptNextVersion();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        inputs = EmbedInputs(ollama);
        Assert.Equal(9, inputs.Count);
        Assert.Contains(inputs, i => i.StartsWith("Abscond\n"));
        Assert.DoesNotContain(inputs, i => i.StartsWith("Llanowar"));
        await using (var reread = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(0, await reread.CrRuleChunks.CountAsync(c => c.Embedding == null));
            Assert.Equal(0, await reread.CrGlossaryChunks.CountAsync(c => c.Embedding == null));
            Assert.Empty(await reread.RulesReembedSignals.ToListAsync());
        }
    }

    [Fact]
    public async Task A_state_mismatch_re_embeds_everything_and_restores_the_state()
    {
        var (connectionString, source, ollama, cr) = await SetUpAsync();
        using var app = NewApp(connectionString, source, ollama, cr);
        using var client = app.CreateClient();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        // A prefix change under stored vectors (the model-switch case reads
        // the same): the gate closes until an ingest run re-embeds — and a
        // plain trigger over unchanged sources is that run, not a no-op.
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            (await db.EmbeddingStates.SingleAsync()).PrefixSetVersion = "0000000000000000";
            await db.SaveChangesAsync();
        }

        Assert.False((await GateAsync(app)).Enabled);
        ollama.Requests.Clear();
        var run = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        Assert.False(run.GetProperty("no_op").GetBoolean());
        Assert.Equal(17, EmbedInputs(ollama).Count);
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(EmbeddingModel.Qwen3.State, CorpusEmbeddingState.Of(await db.EmbeddingStates.SingleAsync()));
        }

        Assert.True((await GateAsync(app)).Enabled);
    }

    [Fact]
    public async Task A_composition_switch_closes_the_gate_re_embeds_the_rewritten_documents_and_reports_unknown_symbols()
    {
        var (connectionString, source, ollama, cr) = await SetUpAsync();
        source.OracleCards += "\n" + CardLine(Guid.NewGuid(), "Odd Rock", oracleText: "{T}: Add {FOO}. Pay {FOO}{BAR}: Draw a card.");
        using (var app = NewApp(connectionString, source, ollama, cr))
        {
            using var client = app.CreateClient();
            await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        }

        // Cell B by configuration alone: the stored (verbatim) state
        // disagrees with the new prefix set, so the gate closes and the next
        // plain run re-embeds everything under the new composition.
        ollama.Requests.Clear();
        using var verbalizing = NewApp(connectionString, source, ollama, cr,
            settings: new Dictionary<string, string?> { ["Embedding:Composition"] = "verbalize-symbols" });
        using var verbalizingClient = verbalizing.CreateClient();
        Assert.False((await GateAsync(verbalizing)).Enabled);

        var run = await WaitForCompletionAsync(verbalizingClient, await TriggerAsync(verbalizingClient), "succeeded");

        var inputs = EmbedInputs(ollama);
        Assert.Equal(18, inputs.Count);
        Assert.Contains("Sol Ring\nArtifact\ntap: Add two colorless mana.\n{1}\nTags: mana-rock, ramp", inputs);
        Assert.Contains("Glistener Elf\nCreature — Elf Warrior\nInfect\n1/1\n{G}\nTags: infect", inputs);
        Assert.Contains("Odd Rock\nArtifact\ntap: Add {FOO}. Pay {FOO}{BAR}: Draw a card.\n{1}", inputs);
        Assert.Contains(inputs, i => i.StartsWith("100.1. A test game"));
        var error = Assert.Single(run.GetProperty("report").GetProperty("errors").EnumerateArray()).GetString()!;
        Assert.Equal("3 symbol(s) in oracle text unknown to the verbalizer, embedded verbatim: {FOO} x2, {BAR} x1", error);
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var state = CorpusEmbeddingState.Of(await db.EmbeddingStates.SingleAsync());
            Assert.Equal((EmbeddingModel.Qwen3 with { Composition = CardDocumentComposition.VerbalizeSymbols }).State, state);
            Assert.NotEqual(EmbeddingModel.Qwen3.State, state);
            // Served text is untouched: only the embedded document changed.
            Assert.Equal("{T}: Add {C}{C}.", (await db.Cards.SingleAsync(c => c.Name == "Sol Ring")).OracleText);
        }

        Assert.True((await GateAsync(verbalizing)).Enabled);
    }

    [Fact]
    public async Task A_change_committed_by_a_run_that_fails_later_embeds_on_the_rerun()
    {
        var (connectionString, source, ollama, cr) = await SetUpAsync();
        var bracketLoad = new SwitchablePhase(IngestPhases.BracketLoad);
        using var app = NewApp(connectionString, source, ollama, cr,
            services => services.AddSingleton<IIngestPhase>(bracketLoad));
        using var client = app.CreateClient();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        // Elves' text changes: the upsert commits it, then a later phase
        // fails before the re-embed slot.
        source.OracleCards = source.OracleCards.Replace("{T}: Add {G}.", "{T}: Add {G}{G}.");
        bracketLoad.Fail = true;
        ollama.Requests.Clear();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "failed");
        Assert.Empty(EmbedInputs(ollama));

        // The rerun's upsert sees no diff, yet the changed document embeds.
        bracketLoad.Fail = false;
        var report = (await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded")).GetProperty("report");

        Assert.Equal(0, report.GetProperty("changed_cards").GetInt32());
        Assert.Equal([ElvesDocument.Replace("{T}: Add {G}.", "{T}: Add {G}{G}.")], EmbedInputs(ollama));
    }

    [Fact]
    public async Task An_over_long_document_is_reported_and_the_rest_embed()
    {
        var (connectionString, source, ollama, cr) = await SetUpAsync();
        ollama.MaxInputLength = 2000;
        source.OracleCards += "\n" + CardLine(Guid.NewGuid(), "Wall of Text", oracleText: new string('x', 3000));
        using var app = NewApp(connectionString, source, ollama, cr);
        using var client = app.CreateClient();

        var report = (await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded")).GetProperty("report");

        var error = Assert.Single(report.GetProperty("errors").EnumerateArray()).GetString()!;
        Assert.Contains("card document 'Wall of Text'", error);
        Assert.Contains("context length", error);
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var wall = await db.CardFaceDocuments.SingleAsync(d => d.Embedding == null);
            Assert.StartsWith("Wall of Text\n", wall.Text);
            Assert.Equal(8, await db.CardFaceDocuments.CountAsync(d => d.Embedding != null));
            Assert.Single(await db.EmbeddingStates.ToListAsync());
        }

        // Still refused on the rerun, still reported: never silently accepted.
        report = (await WaitForCompletionAsync(client, await TriggerAsync(client, force: true), "succeeded")).GetProperty("report");
        Assert.Contains("card document 'Wall of Text'", Assert.Single(report.GetProperty("errors").EnumerateArray()).GetString());
    }

    private static List<string> EmbedInputs(FakeOllama ollama)
        => [.. ollama.Requests
            .Where(r => r.Path == "/api/embed")
            .SelectMany(r => r.Body.GetProperty("input").EnumerateArray().Select(i => i.GetString()!))];

    private static async Task<SemanticQueryStatus> GateAsync(ArchiveApp app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EmbeddingStateGate>().CheckAsync(CancellationToken.None);
    }

    private async Task<(string ConnectionString, FakeBulkSource Source, FakeOllama Ollama, FakeCrSource Cr)> SetUpAsync()
    {
        var source = new FakeBulkSource
        {
            OracleCards = File.ReadAllText(Path.Combine(FixtureDir, "oracle-cards.jsonl")),
            Rulings = File.ReadAllText(Path.Combine(FixtureDir, "rulings.jsonl")),
            OracleTags = File.ReadAllText(Path.Combine(FixtureDir, "oracle-tags.jsonl")),
        };
        return (await postgres.CreateDatabaseAsync($"reembed_{Guid.NewGuid():N}"), source, new FakeOllama(), new FakeCrSource());
    }

    private static ArchiveApp NewApp(
        string connectionString, FakeBulkSource source, FakeOllama ollama, FakeCrSource cr,
        Action<IServiceCollection>? configure = null, IReadOnlyDictionary<string, string?>? settings = null)
        => new(autoMigrate: true, connectionString, services =>
        {
            services.RemoveAll<IScryfallBulkSource>();
            services.AddSingleton<IScryfallBulkSource>(source);
            services.RemoveAll<ICrSource>();
            services.AddSingleton<ICrSource>(cr);
            services.AddHttpClient<OllamaEmbedder>().ConfigurePrimaryHttpMessageHandler(() => ollama);
            configure?.Invoke(services);
        }, settings);
}
