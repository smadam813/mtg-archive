using MtgArchive.Api;
using MtgArchive.Embedding;

namespace MtgArchive.Tests;

/// <summary>
/// The prefix map: what each model prepends per Search surface
/// and side, the prefix-set version that bumps with any prefix change, and
/// which tags configuration may name.
/// </summary>
public class EmbeddingModelTests
{
    [Theory]
    [InlineData("qwen3-embedding:0.6b", SearchSurface.CardSearch,
        "Instruct: " + EmbeddingModel.CardSearchInstruction + "\nQuery: cards that ramp")]
    [InlineData("qwen3-embedding:0.6b", SearchSurface.RulesSearch,
        "Instruct: " + EmbeddingModel.RulesSearchInstruction + "\nQuery: cards that ramp")]
    [InlineData("nomic-embed-text", SearchSurface.CardSearch, "search_query: cards that ramp")]
    [InlineData("nomic-embed-text", SearchSurface.RulesSearch, "search_query: cards that ramp")]
    public void Queries_carry_the_surface_instruction_or_the_task_prefix(string tag, SearchSurface surface, string expected)
        => Assert.Equal(expected, Model(tag).QueryText(surface, "cards that ramp"));

    [Theory]
    [InlineData("qwen3-embedding:0.6b", "Sol Ring {1}\nArtifact\n{T}: Add {C}{C}.")]
    [InlineData("nomic-embed-text", "search_document: Sol Ring {1}\nArtifact\n{T}: Add {C}{C}.")]
    public void Documents_are_unprefixed_on_qwen3_and_task_prefixed_on_nomic(string tag, string expected)
        => Assert.Equal(expected, Model(tag).DocumentText("Sol Ring {1}\nArtifact\n{T}: Add {C}{C}."));

    [Fact]
    public void The_prefix_set_version_bumps_with_any_prefix_change_and_rides_with_the_model()
    {
        var qwen3 = EmbeddingModel.Qwen3;

        // Stable across instances, and different per model.
        Assert.Equal(qwen3.PrefixSetVersion, (qwen3 with { }).PrefixSetVersion);
        Assert.Matches("^[0-9A-F]{16}$", qwen3.PrefixSetVersion);
        Assert.NotEqual(qwen3.PrefixSetVersion, EmbeddingModel.Nomic.PrefixSetVersion);

        // Any change to a prefix template is a different version; the
        // instruction constants are hashed in the same way.
        Assert.NotEqual(qwen3.PrefixSetVersion, (qwen3 with { QueryTemplate = "Query: {query}" }).PrefixSetVersion);
        Assert.NotEqual(qwen3.PrefixSetVersion, (qwen3 with { DocumentTemplate = "Document: {document}" }).PrefixSetVersion);

        // The tag and dims alone do not: the version is the prefix set's.
        Assert.Equal(qwen3.PrefixSetVersion, (qwen3 with { Tag = "other", Dimensions = 1 }).PrefixSetVersion);

        Assert.Equal(new CorpusEmbeddingState("qwen3-embedding:0.6b", 1024, qwen3.PrefixSetVersion), qwen3.State);
    }

    [Fact]
    public void The_card_document_composition_is_part_of_the_prefix_set_and_verbatim_keeps_the_frozen_hash()
    {
        var qwen3 = EmbeddingModel.Qwen3;

        // ADR 0007's frozen tuple: the default composition must not move it.
        Assert.Equal(CardDocumentComposition.Verbatim, qwen3.Composition);
        Assert.Equal("9D84B9513202418B", qwen3.PrefixSetVersion);

        var stripped = qwen3 with { Composition = CardDocumentComposition.StripReminderText };
        var verbalized = qwen3 with { Composition = CardDocumentComposition.VerbalizeSymbols };
        Assert.NotEqual(qwen3.PrefixSetVersion, stripped.PrefixSetVersion);
        Assert.NotEqual(qwen3.PrefixSetVersion, verbalized.PrefixSetVersion);
        Assert.NotEqual(stripped.PrefixSetVersion, verbalized.PrefixSetVersion);
    }

    [Theory]
    [InlineData(null, CardDocumentComposition.Verbatim)]
    [InlineData("verbatim", CardDocumentComposition.Verbatim)]
    [InlineData("strip-reminder-text", CardDocumentComposition.StripReminderText)]
    [InlineData("verbalize-symbols", CardDocumentComposition.VerbalizeSymbols)]
    public void The_composition_resolves_from_configuration_with_verbatim_as_the_default(string? name, CardDocumentComposition expected)
        => Assert.Equal(expected, EmbeddingModel.Resolve("qwen3-embedding:0.6b", name).Composition);

    [Fact]
    public void An_unknown_composition_fails_startup_and_names_the_choices()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => EmbeddingModel.Resolve("qwen3-embedding:0.6b", "drop-tags"));

        Assert.Contains("Embedding:Composition", ex.Message);
        Assert.Contains("verbatim, strip-reminder-text, verbalize-symbols", ex.Message);
    }

    [Fact]
    public void The_primary_tag_resolves_from_configuration()
    {
        var model = EmbeddingModel.Resolve("qwen3-embedding:0.6b");

        Assert.Equal(EmbeddingModel.Qwen3, model);
        Assert.Equal(1024, model.Dimensions);
    }

    [Fact]
    public void The_nomic_fallback_resolves_at_its_native_width()
    {
        // Storable in the 1024-wide columns through the embedder's
        // zero-padding; the state record keeps the native width.
        var model = EmbeddingModel.Resolve("nomic-embed-text");

        Assert.Equal(EmbeddingModel.Nomic, model);
        Assert.Equal(768, model.Dimensions);
    }

    [Theory]
    [InlineData("qwen3-embedding:4b")]
    [InlineData("qwen3-embedding:8b")]
    [InlineData(null)]
    public void Larger_qwen3_variants_are_not_configuration_options(string? tag)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EmbeddingModel.Resolve(tag));

        Assert.Contains("2000-dim", ex.Message);
        Assert.Contains("Embedding:ModelTag", ex.Message);
    }

    private static EmbeddingModel Model(string tag) => EmbeddingModel.Known.Single(m => m.Tag == tag);
}
