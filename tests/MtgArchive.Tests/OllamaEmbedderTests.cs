using Microsoft.Extensions.DependencyInjection;
using MtgArchive.Embedding;

namespace MtgArchive.Tests;

/// <summary>
/// The embedding client against <see cref="FakeOllama"/>: the
/// request Ollama sees (prefixed input, <c>truncate: false</c>), truncation
/// surfaced as a typed refusal, width validation, and startup provisioning
/// of the configured tag — idempotent, and retried until Ollama answers.
/// </summary>
public class OllamaEmbedderTests
{
    [Fact]
    public async Task Queries_and_documents_reach_ollama_prefixed_with_truncation_refused()
    {
        var fake = new FakeOllama { Dimensions = 768 };
        var embedder = new OllamaEmbedder(FakeOllama.Client(fake), EmbeddingModel.Nomic);

        var query = await embedder.EmbedQueryAsync(SearchSurface.CardSearch, "cards that ramp", CancellationToken.None);
        var documents = await embedder.EmbedDocumentsAsync(["Sol Ring", "Arcane Signet"], CancellationToken.None);

        // Nomic's 768-dim output is zero-padded to the 1024-wide columns.
        Assert.Equal(1024, query.ToArray().Length);
        Assert.All(query.ToArray()[768..], v => Assert.Equal(0f, v));
        Assert.Equal(2, documents.Count);
        Assert.Equal(2f, documents[1].ToArray()[0]);
        Assert.Equal(1024, documents[1].ToArray().Length);
        var (path, body) = Assert.Single(fake.Requests, r => r.Body.TryGetProperty("input", out var i) && i.GetArrayLength() == 1);
        Assert.Equal("/api/embed", path);
        Assert.Equal("nomic-embed-text", body.GetProperty("model").GetString());
        Assert.False(body.GetProperty("truncate").GetBoolean());
        Assert.Equal("search_query: cards that ramp", body.GetProperty("input")[0].GetString());
        var batch = fake.Requests.Single(r => r.Body.TryGetProperty("input", out var i) && i.GetArrayLength() == 2).Body;
        Assert.False(batch.GetProperty("truncate").GetBoolean());
        Assert.Equal(
            ["search_document: Sol Ring", "search_document: Arcane Signet"],
            batch.GetProperty("input").EnumerateArray().Select(i => i.GetString()!).ToArray());
    }

    [Theory]
    [InlineData("text", "text")]
    [InlineData("text\n", "text")]
    [InlineData("text\r\n", "text")]
    [InlineData(" text ", "text")]
    [InlineData("line1\r\nline2", "line1\nline2")]
    public async Task A_query_is_trimmed_and_its_line_endings_unified_before_it_reaches_ollama(string query, string expected)
    {
        // A trailing newline moves a distance by 0.015, CRLF by
        // 0.008, a trailing space by 0.003. Interior whitespace stays.
        var fake = new FakeOllama();
        var embedder = new OllamaEmbedder(FakeOllama.Client(fake), EmbeddingModel.Qwen3);

        await embedder.EmbedQueryAsync(SearchSurface.CardSearch, query, CancellationToken.None);

        Assert.Equal(
            EmbeddingModel.Qwen3.QueryText(SearchSurface.CardSearch, expected),
            Assert.Single(fake.Requests).Body.GetProperty("input")[0].GetString());
    }

    [Fact]
    public async Task An_over_long_input_is_surfaced_never_silently_truncated()
    {
        // Ollama's answer to truncate: false on an input past the context
        // window — the alternative, its default, is a vector of the prefix.
        var fake = new FakeOllama { EmbedError = "input length exceeds maximum context length" };
        var embedder = new OllamaEmbedder(FakeOllama.Client(fake), EmbeddingModel.Qwen3);

        var ex = await Assert.ThrowsAsync<EmbeddingTruncatedException>(
            () => embedder.EmbedDocumentsAsync(["short", new string('x', 200_000)], CancellationToken.None));

        Assert.Contains("2 input(s)", ex.Message);
        Assert.Contains("qwen3-embedding:0.6b", ex.Message);
        Assert.Contains("context length", ex.Message);
    }

    [Fact]
    public async Task A_vector_of_the_wrong_width_is_refused()
    {
        var fake = new FakeOllama { Dimensions = 768 };
        var embedder = new OllamaEmbedder(FakeOllama.Client(fake), EmbeddingModel.Qwen3);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => embedder.EmbedQueryAsync(SearchSurface.RulesSearch, "state-based actions", CancellationToken.None));

        Assert.Contains("768-dim", ex.Message);
        Assert.Contains("1024", ex.Message);
    }

    [Fact]
    public void A_model_wider_than_the_columns_is_refused_at_construction()
    {
        // Resolve refuses wide models first; a directly constructed
        // embedder fails the same way, not with a negative array size.
        var wide = new EmbeddingModel("wide-model", 2048, "{query}", "{document}");

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new OllamaEmbedder(FakeOllama.Client(new FakeOllama()), wide));

        Assert.Contains("vector(1024)", ex.Message);
    }

    [Fact]
    public async Task Provisioning_pulls_a_missing_tag_and_leaves_a_present_one_alone()
    {
        var fake = new FakeOllama();
        var embedder = new OllamaEmbedder(FakeOllama.Client(fake), EmbeddingModel.Qwen3);

        Assert.True(await embedder.EnsureModelAsync(CancellationToken.None));
        Assert.Equal("qwen3-embedding:0.6b", (await fake.Pulled.Task).GetProperty("model").GetString());

        // Present: no pull, so a registry republish under the same tag can
        // never swap the weights under the stored vectors.
        fake.LocalModels = ["qwen3-embedding:0.6b"];
        fake.Requests.Clear();
        Assert.False(await embedder.EnsureModelAsync(CancellationToken.None));
        Assert.Equal(["/api/tags"], fake.Requests.Select(r => r.Path).ToArray());

        // Ollama lists an untagged name as name:latest.
        var nomic = new FakeOllama { LocalModels = ["nomic-embed-text:latest"] };
        Assert.False(await new OllamaEmbedder(FakeOllama.Client(nomic), EmbeddingModel.Nomic)
            .EnsureModelAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Startup_provisions_the_configured_tag_retrying_until_ollama_answers()
    {
        // Ollama is still starting when the service comes up: the first
        // call fails, and the provisioner backs off and tries again.
        var fake = new FakeOllama { FailNext = 1 };
        using var app = new ArchiveApp(
            configureServices: services => services.AddHttpClient<OllamaEmbedder>()
                .ConfigurePrimaryHttpMessageHandler(() => fake),
            settings: new Dictionary<string, string?> { ["Embedding:PullOnStartup"] = "true" });
        using var client = app.CreateClient();

        var pull = await fake.Pulled.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("qwen3-embedding:0.6b", pull.GetProperty("model").GetString());
        Assert.Equal(["/api/tags", "/api/tags", "/api/pull"], fake.Requests.Select(r => r.Path).ToArray());
    }

    [Fact]
    public void An_unknown_tag_fails_startup()
    {
        using var app = new ArchiveApp(settings: new Dictionary<string, string?>
        {
            ["Embedding:ModelTag"] = "qwen3-embedding:8b",
        });

        var ex = Assert.Throws<InvalidOperationException>(() => app.CreateClient());

        Assert.Contains("qwen3-embedding:8b", ex.Message);
    }
}
