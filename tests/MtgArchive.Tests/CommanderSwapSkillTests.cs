using System.Text.RegularExpressions;
using MtgArchive.Cards;

namespace MtgArchive.Tests;

/// <summary>
/// The commander-swap skill is prose an agent follows. The
/// test pins what the issue locks: the depth constants, the endpoints,
/// the explicit scope, the agent-side re-rank, and the cited losses.
/// </summary>
public sealed class CommanderSwapSkillTests
{
    private static readonly string Skill = File.ReadAllText(
        Path.Combine(RepoRoot.Find(), "plugin", "mtg-archive", "skills", "commander-swap", "SKILL.md"));

    [Fact]
    public void The_skill_states_the_pull_and_presentation_depths_as_constants()
    {
        Assert.Contains("| Presentation depth | 5 |", Skill);
        Assert.Contains("| Reading depth | 20 |", Skill);
        Assert.Contains($"\"limit\": {CommanderAlternativesEndpoints.MaxLimit}", Skill);
    }

    [Fact]
    public void The_depths_match_the_service_limits()
    {
        // The semantic call scores every pulled candidate: the pull fits in
        // one oracle_ids list and one k.
        Assert.Contains($"| Pull depth | {CommanderAlternativesEndpoints.MaxLimit} |", Skill);
        Assert.Contains($"| Semantic depth | {CardSearchEndpoints.MaxLimit} |", Skill);
        Assert.Equal(CommanderAlternativesEndpoints.MaxLimit, CardSearchEndpoints.MaxLimit);
        Assert.Contains($"k: {CardSearchEndpoints.MaxLimit}, oracle_ids: [.candidates[].card.id]", Skill);
    }

    [Fact]
    public void The_skill_parses_the_pull_with_jq_and_never_extracts_retained()
    {
        Assert.Contains("-o /tmp/commander-swap/alternatives.json", Skill);
        Assert.Contains("Never extract `retained[]`.", Skill);
        Assert.Contains("Extract `lost[]` only for the 5 candidates you present", Skill);
    }

    [Fact]
    public void The_skill_composes_the_three_endpoints_and_the_ban_event_fetch()
    {
        Assert.Contains("POST /commander-alternatives", Skill);
        Assert.Contains("POST /cards/semantic-search", Skill);
        Assert.Contains("GET /cards/{oracle_id}", Skill);
        Assert.Contains("GET /ban-events/{id}", Skill);
    }

    [Fact]
    public void The_skill_sets_collection_scope_explicitly_and_defaults_it_off()
    {
        Assert.Contains("\"collection_scope\": false", Skill);
        Assert.Contains("the service never infers it", Skill);
    }

    [Fact]
    public void The_re_rank_is_agent_side_and_never_pre_filters_color_identity()
    {
        Assert.Contains("agent-side, never service-side", Skill);
        Assert.DoesNotContain("\"color_identity\":", Skill);
        Assert.Contains("Two distances within 0.003 are a tie", Skill);
    }

    [Fact]
    public void The_re_rank_never_crosses_a_retention_tier()
    {
        Assert.Contains("Never re-rank across a retention tier", Skill);
        Assert.Contains("1. `retained_count`, larger first.", Skill);
        Assert.Contains("2. `color_identity_distance`, smaller first.", Skill);
        Assert.Contains("3. The semantic `distance`, smaller first.", Skill);
        Assert.Contains("4. The service order from step 2.", Skill);
        Assert.Contains("ranks below the candidates in its tier that have one", Skill);
    }

    [Fact]
    public void The_skill_states_the_full_top_tier_and_the_disabled_fallback()
    {
        Assert.Contains("the service returns 200 candidates and the last one is in the first one's tier", Skill);
        Assert.Contains("take the first 20 candidates in service order", Skill);
        Assert.Contains("Keep the tiers.", Skill);
    }

    [Fact]
    public void Every_candidate_names_the_measure_that_ranked_it()
    {
        Assert.Contains("Name the measure for every candidate.", Skill);
    }

    [Fact]
    public void Every_loss_cites_its_reason_by_citation_type()
    {
        Assert.Contains("The reason is `banned`, `not_legal`, or `color_identity`.", Skill);
        Assert.Contains("the citation is the card itself", Skill);
        Assert.Contains("by the citation's `type`", Skill);
        Assert.Contains("No uncited losses", Skill);
    }

    [Fact]
    public void The_skill_cites_the_old_commanders_ban_event()
    {
        Assert.Contains("the legality row with `format` equal to the request format and `role` equal to `commander`", Skill);
        Assert.Contains("its `ban_event`", Skill);
        Assert.Contains("ban footnote next to its name", Skill);
    }

    [Fact]
    public void The_semantic_field_is_candidates()
    {
        Assert.DoesNotContain("`hits", Skill);
        Assert.Contains("`candidates`", Skill);
    }

    [Fact]
    public void The_skill_states_the_no_commander_marker_rule()
    {
        Assert.Contains("exactly one card in the list has scope `all`", Skill);
        Assert.Contains("In Commander, when the list marks no commander", Skill);
        Assert.Contains("State the assumption with its ban footnote.", Skill);
    }

    [Fact]
    public void The_skill_reads_the_semantic_status_and_the_unresolved_names()
    {
        Assert.Contains("`semantic.enabled`", Skill);
        Assert.Contains("`semantic.disabled_reason`", Skill);
        Assert.Contains("`unresolved_names`", Skill);
    }

    [Fact]
    public void The_skill_keeps_the_answer_contract_and_defers_the_99_to_build_deck()
    {
        Assert.Contains("references/answer-contract.md", Skill);
        Assert.Contains("versions block", Skill);
        Assert.Contains("docker compose up", Skill);
        Assert.Contains("That is `build-deck`.", Skill);
    }

    [Fact]
    public void Popularity_rank_is_the_service_tie_break_and_the_field_the_skill_reads()
    {
        Assert.DoesNotContain("edhrec_rank", Skill);
        Assert.Contains("then by `popularity_rank`", Skill);
        Assert.Contains("{card, color_identity, popularity_rank, available,", Skill);
        Assert.Contains("Popularity order", PluginFiles.Reference("answer-contract.md"));
    }

    // One pointer to the shared table and one Format section
    // that states each Brawl rule; no Brawl branch outside it.
    [Fact]
    public void Formats_live_in_one_format_section_with_one_pointer_to_the_shared_table()
    {
        Assert.Single(Regex.Matches(Skill, Regex.Escape("../../references/phrase-to-format.md")));
        Assert.Single(Regex.Matches(Skill, @"(?m)^## Format\s*$"));

        var section = Section("Format");
        Assert.Contains("Send `format`", section);
        Assert.Contains("names the destination as the request format", section);
        Assert.Contains("ask the user which card is the commander", section);
        Assert.Contains("The ban-based inference is Commander-only.", section);
        Assert.Contains("`legality`", section);
        Assert.Contains("`not_legal`", section);
        Assert.Contains("`popularity_rank`", section);
        Assert.Contains("`deck_size`", section);
        Assert.Contains("state the gap and point to `deck-check`", section);
        Assert.Contains("Give no cut list.", section);
        Assert.Contains("`GET /cards/{oracle_id}`", section);
        Assert.Contains("its `citation`", section);

        var outside = Skill.Replace(section, "");
        Assert.DoesNotMatch(@"(?i)\bif\b[^.\n]*\bbrawl\b", outside);
        Assert.DoesNotMatch(@"(?i)\bin a brawl format\b", outside);
    }

    [Fact]
    public void The_companion_is_sent_apart_and_judged_but_not_counted()
    {
        Assert.Contains("\"companion\":", Skill);
        var section = Section("Format");
        Assert.Contains("`companion`", section);
        Assert.Contains("not in `retained_count` or `lost_count`", section);
    }

    private static string Section(string heading)
    {
        var match = Regex.Match(Skill, $@"(?ms)^## {Regex.Escape(heading)}\s*$(.*?)(?=^## |\z)");
        Assert.True(match.Success, $"the skill has a '## {heading}' section");
        return match.Value;
    }
}
