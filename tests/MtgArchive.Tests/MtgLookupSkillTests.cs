using System.Text.RegularExpressions;

namespace MtgArchive.Tests;

/// <summary>
/// The <c>mtg-lookup</c> skill. The skill body composes seven
/// routes, re-ranks hits itself, keeps exact-name lookup exact, and reads
/// rulings from card detail only.
/// </summary>
public sealed class MtgLookupSkillTests
{
    private static readonly string SkillText = File.ReadAllText(Path.Combine(
        RepoRoot.Find(), "plugin", "mtg-archive", "skills", "mtg-lookup", "SKILL.md"));

    [Theory]
    [InlineData("POST /cards/search")]
    [InlineData("POST /cards/semantic-search")]
    [InlineData("POST /rules/search")]
    [InlineData("GET /cards/{oracle_id}")]
    [InlineData("GET /cards?name=")]
    [InlineData("GET /rules/{number}")]
    [InlineData("GET /ban-events/{id}")]
    public void The_skill_names_every_route_it_composes(string route)
    {
        Assert.Contains(route, SkillText);
    }

    [Fact]
    public void The_skill_re_ranks_search_hits_itself()
    {
        Assert.Contains("You re-rank", SkillText);
        Assert.Contains("no re-ranker and no fusion", SkillText);
        Assert.Contains("within 0.003 are a tie", SkillText);
    }

    [Fact]
    public void The_exact_name_lookup_is_never_fuzzy_search()
    {
        Assert.Contains("`GET /cards?name=` is exact-name lookup only", SkillText);
        Assert.Contains("Never use it as fuzzy search", SkillText);
    }

    [Fact]
    public void Rulings_come_from_card_detail_only()
    {
        Assert.Contains("Rulings live on card detail and nowhere else. `GET /cards/{oracle_id}` serves them", SkillText);
        Assert.Contains("Search hits carry no rulings", SkillText);
    }

    [Fact]
    public void The_skill_answers_from_the_service_only_and_never_from_recall()
    {
        Assert.Contains("Never answer from model recall", SkillText);
        Assert.Contains("MTG_ARCHIVE_URL", SkillText);
        Assert.Contains("references/answer-contract.md", SkillText);
        Assert.DoesNotContain("docker compose up", SkillText);
    }

    [Fact]
    public void Archetype_slang_expands_through_the_shared_glossary_and_is_echoed_as_an_assumption()
    {
        Assert.Contains("references/archetype-glossary.md", SkillText);
        Assert.Contains("State the expansion as an assumption", SkillText);
        Assert.Contains("The glossary expansion, when the user's wording contained an archetype term", SkillText);
    }

    // One pointer to the shared table and one Format section; no
    // "if Brawl" checks scattered through the steps.
    [Fact]
    public void Brawl_lives_in_one_format_section_with_one_pointer_to_the_shared_table()
    {
        Assert.Single(Regex.Matches(SkillText, Regex.Escape("../../references/phrase-to-format.md")));
        Assert.Single(Regex.Matches(SkillText, @"(?m)^## Format\s*$"));

        var section = Section("Format");
        Assert.Contains("all five formats", section);
        Assert.Contains("`format`", section);
        Assert.Contains("cites `legality`, not a ban event", section);

        var outside = SkillText.Replace(section, "");
        Assert.DoesNotMatch(@"(?i)\bif\b[^.\n]*\bbrawl\b", outside);
        Assert.DoesNotMatch(@"(?i)\bin a brawl format\b", outside);
    }

    // Card detail returns five formats; a Standard row cites
    // legality like a Brawl row, with no ban event or version pin.
    [Fact]
    public void A_standard_row_lives_in_the_format_section_and_cites_legality()
    {
        var section = Section("Format");
        Assert.Contains("`standard`", section);
        Assert.Contains("A `standard` row cites `legality`", section);
        Assert.Contains("`standard_brawl`, and `standard`", section);
        Assert.DoesNotContain("all four formats", SkillText);
    }

    [Fact]
    public void A_legality_footnote_comes_from_a_brawl_legality_row()
    {
        Assert.Contains("`legality` comes from a Brawl legality row", SkillText);
    }

    private static string Section(string heading)
    {
        var match = Regex.Match(SkillText, $@"(?ms)^## {Regex.Escape(heading)}\s*$(.*?)(?=^## |\z)");
        Assert.True(match.Success, $"the skill has a '## {heading}' section");
        return match.Groups[1].Value;
    }

    [Fact]
    public void Deck_analysis_is_handed_to_deck_check()
    {
        Assert.Contains("belong to `deck-check`", SkillText);
    }

    // The handoff names tier 4, so a question about a deck's
    // own counterspells or protection goes to deck-check too.
    [Fact]
    public void The_deck_check_handoff_names_tier_4()
    {
        var line = SkillText.Split('\n').Single(l => l.Contains("belong to `deck-check`"));

        Assert.Contains("tier 4", line);
        Assert.Contains("counterspells or protection", line);
    }

    [Fact]
    public void The_popularity_order_breaks_ties_and_a_brawl_proxy_is_disclosed()
    {
        Assert.DoesNotContain("edhrec_rank", SkillText);
        Assert.Contains("Break ties by `popularity_rank` for cards", SkillText);
        Assert.Contains("pages in the format's popularity order", SkillText);
        Assert.Contains(PopularityContract.ProxyDisclosure, PluginFiles.Reference("answer-contract.md"));
    }
}
