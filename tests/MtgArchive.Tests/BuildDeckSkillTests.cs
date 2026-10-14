using System.Globalization;
using System.Text.RegularExpressions;

namespace MtgArchive.Tests;

/// <summary>
/// The build-deck skill: the skill-side recommendation loop of
/// ADR 0006. The skill file states the locked loop parameters in one table,
/// composes only the existing endpoints, and never names a service-side
/// optimizer. Step 9 renders every part of the answer contract the
/// envelopes carry. An out-of-reach target rests on the
/// reach bound, and the loop runs on to plateau or budget.
/// </summary>
public sealed class BuildDeckSkillTests
{
    private static readonly string Skill = File.ReadAllText(
        Path.Combine(RepoRoot.Find(), "plugin", "mtg-archive", "skills", "build-deck", "SKILL.md"));

    private static readonly Dictionary<string, decimal> Parameters = Regex
        .Matches(Section("Loop parameters"), @"(?m)^\|\s*`([^`]+)`\s*\|\s*([0-9.]+)\s*\|")
        .ToDictionary(m => m.Groups[1].Value, m => decimal.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));

    private static readonly string Report = Section("Step 9: Report");

    private static string Section(string heading, int level = 2)
        => Regex.Match(Skill, $@"(?ms)^{new string('#', level)} {Regex.Escape(heading)}\s*$(.*?)(?=^#{{1,{level}}} )").Groups[1].Value;

    [Fact]
    public void The_skill_composes_only_the_index_and_simulation_endpoints()
    {
        foreach (var route in new[] { "/cards/search", "/cards/semantic-search", "/tier1-report", "/bracket-classification", "/simulate", "/commander-alternatives", "/deck-verdict", "/decklist-parse" })
        {
            Assert.Contains($"`POST {route}`", Skill);
        }

        Assert.DoesNotContain("/authoring-queue", Skill);
        Assert.DoesNotContain("/optimize", Skill);
        Assert.DoesNotContain("suggest-cards", Skill);
    }

    [Fact]
    public void Candidate_generation_runs_under_collection_scope()
    {
        Assert.Contains("\"collection_scope\": true", Skill);
        Assert.Contains("min(copy limit, owned quantity)", Skill);
        Assert.Contains("\"role\": \"commander\"", Skill);
    }

    [Fact]
    public void The_loop_parameters_are_the_locked_values()
    {
        Assert.Equal(15, Parameters["iteration_budget"]);
        Assert.Equal(3, Parameters["plateau_rounds"]);
        Assert.Equal(0.95m, Parameters["confidence_level"]);
        Assert.Matches(@"(?i)no mid-loop check-ins", Skill);
        Assert.Matches(@"(?i)service never enforces", Skill);
    }

    [Fact]
    public void The_three_stops_report_best_so_far()
    {
        Assert.Matches(@"(?i)target met", Skill);
        Assert.Matches(@"(?i)plateau", Skill);
        Assert.Matches(@"(?i)budget exhausted", Skill);
        Assert.Matches(@"(?i)best-so-far", Skill);
        Assert.Matches(@"(?i)lower bound", Skill);
    }

    /// <summary>L comes from the format's free mulligans, so the skills
    /// read it from <c>/tier1-report</c>: 3 in Competitive
    /// Brawl, 4 elsewhere. No skill states L as a fixed number.</summary>
    [Theory]
    [InlineData("archive-admin")]
    [InlineData("build-deck")]
    [InlineData("commander-swap")]
    [InlineData("deck-check")]
    [InlineData("mtg-lookup")]
    public void No_skill_hardcodes_the_mulligan_looks(string skill)
    {
        Assert.DoesNotMatch(@"(?i)`L`\s+(is|=)\s+\d", PluginFiles.Skill(skill));
    }

    /// <summary>The deck verdict replaced the envelope's <c>legality</c>
    /// field, so no skill reads that field or its
    /// <c>violations</c> list.</summary>
    [Theory]
    [InlineData("archive-admin")]
    [InlineData("build-deck")]
    [InlineData("commander-swap")]
    [InlineData("deck-check")]
    [InlineData("mtg-lookup")]
    public void No_skill_reads_the_old_simulate_legality_field(string skill)
    {
        var text = PluginFiles.Skill(skill);
        Assert.DoesNotContain("`violations`", text);
        Assert.DoesNotContain("`legality` verdict", text);
        Assert.DoesNotMatch(@"(?m)^- `legality`:", text);
    }

    [Fact]
    public void The_tier_2_loop_reads_the_deck_verdict_from_the_envelope()
    {
        var step6 = Section("Step 6: Evaluate with tier 2 (the inner loop)");

        Assert.Contains("- `deck_verdict`:", step6);
        Assert.Contains("`card_findings`", step6);
        Assert.Contains("`POST /deck-verdict`", step6);
    }

    [Fact]
    public void An_out_of_reach_target_rests_on_the_reach_bound_and_the_loop_keeps_climbing()
    {
        var step5 = Section("Step 5: Gate through the cost ladder");
        var step7 = Section("Step 7: Stop");

        Assert.Contains("**Reach bound.**", step5);
        Assert.Contains("route category", step5);
        Assert.Contains("bound decklist", step5);
        Assert.Contains("`1 - (1 - q)^L`", step5);
        Assert.Matches(@"`L` is `mulligan_looks` from the same tier-1 report", step5);
        Assert.Matches(@"(?i)command zone.*never.*out of reach", step5);
        Assert.Matches(@"(?i)fetch land", step5);
        Assert.Matches(@"(?i)`all_of`.*smallest", step5);
        Assert.Matches(@"`1 - 0\.65\^4`, about 0\.82", step5);
        Assert.Matches(@"(?i)\*\*Reach gate\.\*\*.*own reach bound", step5);

        Assert.Matches(@"(?i)bound decklist.*Game Changer limit", step5);
        Assert.Matches(@"(?i)bound decklist must pass bracket classification", step5);
        Assert.Matches(@"(?i)after a revisit, repeat the out-of-reach declaration", step7);

        Assert.Matches(@"(?i)\(d\) Target out of reach", step7);
        Assert.Matches(@"(?i)plateau or budget exhausted", step7);

        Assert.DoesNotContain("no simulate call can meet the target", Skill);
        Assert.DoesNotContain("tutor target has no such cap", Skill);
    }

    [Fact]
    public void The_mana_goal_answer_names_excluded_sources_and_their_reason()
    {
        var manaGoal = Section("Mana-goal answer", level: 3);

        Assert.Contains("`mana_sources.excluded`", manaGoal);
        Assert.Matches(@"(?i)name each excluded card and its `reason`", manaGoal);
        Assert.Matches(@"(?i)adds an excluded card.*does not count", manaGoal);
    }

    // The declared frame of the mana-goal template ends on its "Stopped on" line.
    [Fact]
    public void The_mana_goal_answer_keeps_its_stopped_on_line()
    {
        var manaGoal = Section("Mana-goal answer", level: 3);

        Assert.Matches(@"(?m)^Budget: \d+ rounds, tier 1 only; \d+ spent\.\r?\nStopped on: ", manaGoal);
        Assert.Matches(@"(?i)always print the ""Stopped on"" line and the list of excluded cards under the table, even when the loop makes no swap", manaGoal);
        Assert.Matches(@"(?i)budget line counts 0 rounds spent", manaGoal);
        Assert.Matches(@"(?i)goal rows widened to 2 or more pips", manaGoal);
        Assert.Matches(@"(?i)for the goal color, show its goal rows, widened or not", manaGoal);
        Assert.Contains("Stopped on: target met at baseline; no swap.", manaGoal);
    }

    // Baseline goal rows that meet the target widen to the rows with 2 or more pips.
    [Fact]
    public void A_mana_goal_widens_its_goal_rows_when_the_baseline_meets_the_target()
    {
        var step1 = Section("Step 1: Resolve the request to numbers");
        var step7 = Section("Step 7: Stop");

        Assert.Matches(@"(?i)check the goal rows on the baseline tier-1 report of step 5", step1);
        Assert.Matches(@"(?i)meet the target and the goal color has a row with 2 or more `pips` that is not a goal row, the goal rows become all the goal color's rows", step1);
        Assert.Matches(@"(?i)state the widening", step1);
        Assert.Matches(@"(?i)after any widening, meet the target at baseline, stop with no swap", step1);
        Assert.Matches(@"(?i)widened goal rows miss the target, run the loop as usual", step1);
        Assert.Contains("Stopped on: target met at baseline; no swap.", step1);
        Assert.Matches(@"(?i)mana goal that meets the target at baseline.*makes no swap", step7);
    }

    [Fact]
    public void The_answer_frames_four_stopping_criteria_and_states_the_out_of_reach_gap()
    {
        Assert.Matches(@"(?i)Stopping criteria: target met, plateau .*, budget exhausted, or target out of reach", Report);
        Assert.Matches(@"(?i)Stopped on: target out of reach .*; then (plateau|budget exhausted)", Report);
        Assert.Matches(@"(?i)gap.*reach bound.*best lower bound", Report);
    }

    [Fact]
    public void Every_candidate_shares_one_base_seed_and_the_confirmation_call_counts()
    {
        Assert.Matches(@"(?i)one base seed", Skill);
        Assert.Matches(@"(?i)fresh seed", Skill);
        Assert.Matches(@"(?i)confirmation call counts against the budget", Skill);
        Assert.Contains("\"seed\"", Skill);
    }

    [Fact]
    public void Goals_map_into_the_closed_predicate_vocabulary()
    {
        foreach (var kind in new[] { "on_battlefield", "cast", "all_of" })
        {
            Assert.Matches($@"`{kind}[(`]", Skill);
        }

        Assert.Matches(@"(?i)nearest expressible", Skill);
        Assert.DoesNotContain("dsl_coverage", Skill);
    }

    [Fact]
    public void The_skill_loads_the_bracket_and_disruption_references()
    {
        Assert.Contains("../../references/phrase-to-bracket.md", Skill);
        Assert.Contains("../../references/default-disruption-profile.md", Skill);
    }

    [Fact]
    public void The_report_names_both_policies_and_the_exemplars_from_the_envelope()
    {
        Assert.Contains("`policies.play`", Report);
        Assert.Contains("`policies.mulligan`", Report);
        Assert.Contains("`exemplars`", Report);
        Assert.Contains("`seed`", Report);
        Assert.Contains("`run_index`", Report);
        Assert.Matches(@"(?i)up to 3 exemplar", Report);
    }

    [Fact]
    public void The_report_forbids_a_point_estimate_and_keeps_the_full_label()
    {
        Assert.Matches(@"(?i)never .*point estimate.*lead", Report);
        Assert.Contains(">= X%, greedy policy", Report);
        Assert.Matches(@"(?i)never shorten", Report);
    }

    [Fact]
    public void Every_decklist_card_carries_a_fetchable_card_footnote()
    {
        Assert.Matches(@"(?i)every card in the decklist.*footnote", Report);
        Assert.Contains("`GET /cards/{id}`", Report);
    }

    // The answer prints each footnote, never a bare `[n]` and an offer to paste the ids.
    [Fact]
    public void Every_card_footnote_is_printed_and_never_offered_in_its_place()
    {
        Assert.Matches(@"(?i)print every card footnote, however long the decklist", Report);
        Assert.Matches(@"(?i)never offer the footnotes or the ids in place of printing them", Report);
        Assert.Matches(@"(?i)bare `\[n\]`.*is not a citation", Report);
    }

    [Fact]
    public void The_bracket_reads_as_a_floor_with_its_reasons()
    {
        Assert.Contains("`bracket_floor`", Report);
        Assert.Matches(@"(?i)never .*single bracket", Report);
        Assert.Contains("`reasons`", Report);
    }

    [Fact]
    public void An_unimplemented_or_unprofiled_card_is_reported_from_engine_coverage()
    {
        var step6 = Section("Step 6: Evaluate with tier 2 (the inner loop)");
        Assert.Contains("`engine_coverage.unimplemented`", step6);
        Assert.Contains("`engine_coverage.unprofiled`", step6);
        Assert.DoesNotContain("staple cut", Skill);
        Assert.DoesNotContain("inert", Skill);
    }

    [Fact]
    public void The_loop_reads_the_engine_and_its_coverage_from_the_envelope()
    {
        Assert.Contains("`engine_coverage`", Skill);
        Assert.Contains("`engine.version`", Skill);
        Assert.Contains("`engine_errors`", Skill);
        Assert.Contains("blank card", Skill);
        Assert.Contains("`engine.name`", Report);
        Assert.Contains("two games per run", Skill);
    }

    // One pointer to the shared table and one Format section
    // that holds every Brawl rule, so no step carries an "if Brawl" check.
    [Fact]
    public void Formats_live_in_one_format_section_with_one_pointer_to_the_shared_table()
    {
        Assert.Single(Regex.Matches(Skill, Regex.Escape("../../references/phrase-to-format.md")));
        Assert.Single(Regex.Matches(Skill, @"(?m)^## Format\s*$"));

        var body = Regex.Replace(Skill, @"(?s)\A---.*?\n---\s*\n", string.Empty);
        var outside = body.Replace(Section("Format"), string.Empty);
        Assert.DoesNotContain("Brawl", outside);
    }

    [Fact]
    public void The_format_section_states_each_brawl_rule()
    {
        var format = Section("Format");

        Assert.Matches(@"(?i)pick the format first", format);
        Assert.Matches(@"(?i)no bracket step", format);
        Assert.Contains("99", format);
        Assert.Contains("59", format);
        Assert.Matches(@"(?i)tier-1 screen, then the reach gate, then tier 2", format);
        Assert.Matches(@"(?i)every owned, legal, on-identity route card", format);
        Assert.Matches(@"(?i)no Game Changer cap", format);
        Assert.Contains("`mulligan_looks`", format);
        Assert.Contains("`POST /tier1-report`", format);
        Assert.Contains("`popularity_rank`", format);
        Assert.Contains("`POST /decklist-parse`", format);
        Assert.Contains("No power target: Arena publishes no bracket system for Brawl.", format);
        Assert.Matches(@"""Competitive Brawl"" or ""ranked Brawl"".*`competitive_brawl`", format);
        Assert.Matches(@"(?i)versions block leaves out the bracket spec and the Game Changers snapshot", format);
        Assert.Contains("`format`", format);
    }

    // The answer names the game, and both answer layouts point at the rule.
    [Fact]
    public void The_answer_names_the_game_from_one_rule_in_the_format_section()
    {
        var format = Section("Format");

        Assert.Matches(@"Name the game in the answer, next to the floor: 25 starting life", format);
        Assert.Matches(@"(?i)no draw on turn 1 on the play", format);
        Assert.Contains("`simplifications`", format);
        Assert.Matches(@"(?i)mana goal has no envelope", format);
        Assert.Equal(2, Regex.Matches(Report, Regex.Escape("Name the game")).Count);
    }

    [Fact]
    public void The_final_list_passes_the_deck_verdict_and_is_never_padded()
    {
        var gate = Section("Step 9: Report");

        Assert.Contains("`POST /deck-verdict`", gate);
        Assert.Matches(@"(?i)fix (each|every) (break|finding)", gate);
        Assert.Contains("`incomplete`", gate);
        Assert.Matches(@"`required_deck_size` minus `deck_size`", gate);
        Assert.Matches(@"(?i)never pad", gate);
        Assert.Matches(@"(?i)does not own", gate);
    }

    [Fact]
    public void The_commander_shortlist_breaks_ties_by_the_commander_popularity_order()
    {
        Assert.DoesNotContain("edhrec_rank", Skill);
        Assert.Contains("Rank the shortlist by fit to the goal, then by `popularity_rank`", Skill);
    }
}
