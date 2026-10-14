using System.Text.Json;
using MtgArchive.Api;
using MtgArchive.Embedding;

namespace MtgArchive.Tests;

/// <summary>
/// The archetype glossary. The plugin file parses, covers the
/// fixture's archetype-vocab queries, and expands a query by its longest
/// whole-word term. The eval's glossary pass merges the pulls ahead of the
/// semantic hits by the file's rule and reports beside the bare scores.
/// </summary>
public sealed class ArchetypeGlossaryTests
{
    private static readonly IReadOnlyList<ArchetypeGlossary.Entry> Glossary = ArchetypeGlossary.Load();

    [Fact]
    public void The_plugin_file_has_an_entry_for_every_archetype_vocab_fixture_query()
    {
        var fixture = JsonSerializer.Deserialize<RetrievalEval.Fixture>(
            File.ReadAllText(Path.Combine(RepoRoot.Find(), "eval", "retrieval", "queries.json")), WireJson.Options)!;
        var queries = fixture.CardSearch.Where(q => q.ArchetypeVocab).ToList();

        Assert.Equal(15, queries.Count);
        Assert.All(queries, query =>
        {
            var expansion = ArchetypeGlossary.Expand(query.Query, Glossary);
            Assert.NotNull(expansion);
            Assert.Equal(query.Query, expansion.Entry.Term);
        });
        Assert.All(Glossary, entry =>
        {
            Assert.NotEmpty(entry.Definition);
            Assert.NotEmpty(entry.Phrasing);
            Assert.All(entry.Tags, tag => Assert.NotEmpty(tag));
            Assert.All(entry.Types, type => Assert.NotEmpty(type));
        });
        Assert.Equal(Glossary.Count, Glossary.Select(e => e.Term).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void The_pull_and_merge_steps_read_the_format_popularity_order()
    {
        var text = PluginFiles.Reference("archetype-glossary.md");

        Assert.DoesNotContain("edhrec_rank", text);
        Assert.Contains("The page comes back in the format's popularity order.", text);
        Assert.Contains("Pulled cards come first, in the format's popularity order across the pulls", text);
        Assert.EndsWith(
            "win with combat damage from that creature (in Commander, commander damage).",
            Glossary.Single(e => e.Term == "voltron").Definition);
    }

    [Fact]
    public void The_table_parses_by_column_and_blank_cells_are_no_pull()
    {
        var entries = ArchetypeGlossary.Parse(
            """
            # Glossary

            | not | the | terms | table | here |
            |---|---|---|---|---|
            | x | y | z | w | v |

            | term | definition | tags | types | phrasing |
            |---|---|---|---|---|
            | stax | Tax and lock pieces. | stasis, cost increaser |  | Spells cost more. |
            | voltron | One big creature. |  | Equipment, Aura | Equipped creature gets +3/+3. |
            """);

        Assert.Equal(
            [
                new("stax", "Tax and lock pieces.", ["stasis", "cost increaser"], [], "Spells cost more."),
                new("voltron", "One big creature.", [], ["Equipment", "Aura"], "Equipped creature gets +3/+3."),
            ],
            entries.Select(e => (e.Term, e.Definition, e.Tags.ToArray(), e.Types.ToArray(), e.Phrasing)));
    }

    [Theory]
    [InlineData("cheap green ramp", "ramp", "cheap green <ramp>")]
    [InlineData("a Card Draw Engine for Simic", "card draw engine", "a <card draw engine> for Simic")]
    [InlineData("board wipes that hit indestructible", "board wipe", "<board wipe> that hit indestructible")]
    [InlineData("the best wheels", "wheel", "the best <wheel>")]
    public void A_query_expands_by_its_longest_whole_word_term_and_keeps_the_rest(string query, string term, string shape)
    {
        var entries = Glossary.Select(e => e with { Phrasing = $"<{e.Term}>" }).ToList();

        var expansion = ArchetypeGlossary.Expand(query, entries);

        Assert.NotNull(expansion);
        Assert.Equal(term, expansion.Entry.Term);
        Assert.Equal(shape, expansion.Query);
    }

    [Theory]
    [InlineData("Rampant Growth")]
    [InlineData("ramping")]
    [InlineData("destroy all creatures")]
    public void A_query_without_a_whole_word_term_has_no_expansion(string query)
    {
        Assert.Null(ArchetypeGlossary.Expand(query, Glossary));
    }

    [Fact]
    public void The_glossary_pass_merges_pulls_by_rank_ahead_of_semantic_hits_and_reports_beside_the_bare_scores()
    {
        var (boots, greaves, rancor, junk) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var query = new RetrievalEval.CardSearchQuery(
            "cs-061", "voltron", true, [new(boots, "Swiftfoot Boots", 3), new(greaves, "Lightning Greaves", 3), new(rancor, "Rancor", 2)]);
        var expansion = new ArchetypeGlossary.Expansion(
            new("voltron", "One big creature.", [], ["Equipment", "Aura"], "Equipped creature gets +3/+3."),
            "Equipped creature gets +3/+3.");
        // Greaves is on both pull pages; its better popularity rank wins and it
        // appears once.
        var equipment = Pull((greaves, "Lightning Greaves", 13), (junk, "Utility Knife", 500));
        var aura = Pull((rancor, "Rancor", 40), (greaves, "Lightning Greaves", 999));
        var semantic = JsonSerializer.SerializeToElement(new
        {
            semantic = new { enabled = true },
            candidates = new[]
            {
                new { card = new { type = "card", id = boots.ToString(), label = "Swiftfoot Boots" } },
                new { card = new { type = "card", id = rancor.ToString(), label = "Rancor" } },
            },
        });

        var score = RetrievalEval.ScoreExpanded(query, expansion, [equipment, aura], semantic);

        Assert.Equal(["Lightning Greaves", "Rancor", "Utility Knife", "Swiftfoot Boots"], score.TopHits.Select(c => c.Label));
        Assert.Equal("voltron", score.Term);
        Assert.Equal(["Equipment", "Aura"], score.Types);
        Assert.Equal("Equipped creature gets +3/+3.", score.Query);
        Assert.Equal(1, score.RecallAt30);
        // Ideal: 3, 3, 2 at ranks 1..3. Got: 3, 2, 0, 3.
        var ideal = 3 + 3 / Math.Log2(3) + 2 / Math.Log2(4);
        Assert.Equal((3 + 2 / Math.Log2(3) + 3 / Math.Log2(5)) / ideal, score.NdcgAt10, 6);

        var bare = new RetrievalEval.QueryScore("cs-061", SearchSurface.CardSearch, true, 0, 0, []);
        var summary = RetrievalEval.Summaries([bare], [score]);
        Assert.Equal(0, summary["card_search_archetype_vocab"].NdcgAt10);
        Assert.Equal(score.NdcgAt10, summary["card_search_archetype_vocab_expanded"].NdcgAt10);
        Assert.DoesNotContain("card_search_archetype_vocab_expanded", RetrievalEval.Summaries([bare]).Keys);
    }

    private static JsonElement Pull(params (Guid Id, string Label, int Rank)[] hits)
        => JsonSerializer.SerializeToElement(new
        {
            candidates = hits.Select(h => new Dictionary<string, object?>
            {
                ["card"] = new { type = "card", id = h.Id.ToString(), label = h.Label },
                ["popularity_rank"] = h.Rank,
            }),
        });
}
