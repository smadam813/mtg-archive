using System.Text.RegularExpressions;

namespace MtgArchive.Tests;

/// <summary>
/// The deck-check skill: read-only analysis of an existing
/// deck. The skill composes five service routes and reads the cost ladder
/// as its tier-selection order. It never sends a power-level phrase or an
/// invented category to the service.
/// </summary>
public sealed class DeckCheckSkillTests
{
    private static readonly string SkillText = PluginFiles.Skill("deck-check");

    [Theory]
    [InlineData("POST /tier1-report")]
    [InlineData("POST /bracket-classification")]
    [InlineData("POST /simulate")]
    [InlineData("POST /cards/search")]
    [InlineData("POST /deck-verdict")]
    public void The_skill_composes_the_five_routes(string route)
    {
        Assert.Contains(route, SkillText);
    }

    [Fact]
    public void The_cost_ladder_is_the_reading_order_and_tier_1_comes_first()
    {
        Assert.True(SkillText.IndexOf("POST /tier1-report", StringComparison.Ordinal) < SkillText.IndexOf("POST /simulate", StringComparison.Ordinal));
        Assert.Contains("cost ladder", SkillText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("when the goal names card semantics or sequencing", SkillText);
    }

    [Fact]
    public void The_caller_supplies_categories_from_oracle_tags_and_echoes_them()
    {
        Assert.Contains("Oracle Tags", SkillText);
        Assert.Contains("\"categories\"", SkillText);
        Assert.Matches(@"""name"":\s*""[^""]+"",\s*""cards"":", SkillText);
        Assert.Contains("never invents", SkillText);
    }

    [Theory]
    [InlineData("on_battlefield", "by_turn")]
    [InlineData("cast", "by_turn")]
    [InlineData("all_of", "goals")]
    public void The_goal_table_maps_a_phrase_to_each_predicate(string kind, string field)
    {
        Assert.Matches($@"(?m)^\|[^|]+\|\s*`\{{ ""kind"": ""{kind}"",[^`]*""{field}""[^`]*\}}`\s*\|", SkillText);
    }

    [Fact]
    public void Bracket_questions_send_a_numeric_target_and_report_four_parts()
    {
        Assert.Contains("references/phrase-to-bracket.md", SkillText);
        Assert.Contains("`target_bracket`", SkillText);
        foreach (var part in new[] { "bracket_floor", "assumed_bracket", "reasons", "assumptions" })
        {
            Assert.Contains($"`{part}`", SkillText);
        }
    }

    [Fact]
    public void Resilience_uses_the_shared_default_profile()
    {
        Assert.Contains("references/default-disruption-profile.md", SkillText);
        Assert.Contains("`disruption_profile`", SkillText);
        Assert.Contains("`resilience_delta`", SkillText);
    }

    [Fact]
    public void Simulation_answers_echo_the_engine_simplifications()
    {
        Assert.Contains("`simplifications`", SkillText);
        Assert.Contains("`simplifications`", PluginFiles.Reference("answer-contract.md"));
    }

    [Fact]
    public void Reports_follow_the_answer_contract()
    {
        Assert.Contains("references/answer-contract.md", SkillText);
        Assert.Contains("`versions`", SkillText);
        Assert.Contains("95%", SkillText);
    }

    [Fact]
    public void Excluded_mana_sources_are_named_with_their_reason()
    {
        Assert.Contains("`mana_sources.excluded`", SkillText);
        Assert.Matches(@"(?i)name each excluded card and its `reason`", SkillText);
    }

    /// <summary>The tier-1 report carries the mulligan looks and echoes the
    /// format in its versions block.</summary>
    [Fact]
    public void Tier_1_reads_the_mulligan_looks_and_the_format_echo_from_the_report()
    {
        var tier1 = SkillText[SkillText.IndexOf("## Tier 1:", StringComparison.Ordinal)..SkillText.IndexOf("## Bracket:", StringComparison.Ordinal)];
        Assert.Contains("`mulligan_looks`", tier1);
        Assert.Contains("`format`", tier1);
        Assert.DoesNotContain("`versions` block is empty", tier1);
    }

    [Fact]
    public void Tier_1_answers_echo_the_whole_assumptions_block_without_a_hand_list()
    {
        const string Echo = "Echo every line of the `assumptions` block";
        var contract = PluginFiles.Reference("answer-contract.md");
        Assert.Contains(Echo, SkillText);
        Assert.Contains(Echo, contract);
        foreach (var text in new[] { SkillText, contract })
        {
            Assert.DoesNotContain("no mulligans", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("enters-tapped ignored", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Deck_changes_are_out_of_scope_and_the_threshold_is_the_skills_own()
    {
        Assert.Contains("build-deck", SkillText);
        Assert.Contains("consistency target", SkillText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Placeholder", SkillText);
    }

    // One pointer to the shared table and one Format section.
    // The section names the verdict route, one call per format, and the
    // no-power-target reply.
    [Fact]
    public void Formats_live_in_one_format_section_with_one_pointer_to_the_shared_table()
    {
        Assert.Single(Regex.Matches(SkillText, Regex.Escape("../../references/phrase-to-format.md")));
        Assert.Single(Regex.Matches(SkillText, @"(?m)^## Format\s*$"));

        var section = Section("Format");
        Assert.Contains("POST /deck-verdict", section);
        Assert.Contains("one call per format", section);
        Assert.Contains("one answer section per format", section);
        Assert.Contains("one merged versions block", section);
        Assert.Contains("\"No power target: Arena publishes no bracket system for Brawl.\"", section);
        Assert.Contains("make no call", section, StringComparison.OrdinalIgnoreCase);

        var outside = SkillText.Replace(section, "");
        Assert.DoesNotMatch(@"(?i)\bif\b[^.\n]*\bbrawl\b", outside);
        Assert.DoesNotMatch(@"(?i)\bin a brawl format\b", outside);
    }

    // A verdict lists blockers only, so the skill looks up each
    // banned card to report its status in the other Brawl formats.
    [Fact]
    public void A_banned_card_is_looked_up_in_the_other_brawl_formats()
    {
        var section = Section("Format");

        Assert.Contains("GET /cards?name=", section);
        Assert.Contains("status `banned`", section);
        Assert.Contains("other Brawl formats", section);
    }

    // A digital-only card shows as a not_legal Brawl finding.
    // The skill reads digital_only from each card detail it fetches. It
    // names a digital-only card and cites that card detail.
    [Fact]
    public void A_not_legal_card_is_looked_up_for_its_digital_only_flag()
    {
        var section = Section("Format");

        Assert.Contains("status `not_legal`, call `GET /cards?name=` to read `digital_only`", section);
        Assert.Contains("Read `digital_only` in each card detail you fetch for a `banned` or `not_legal` finding", section);
        Assert.Contains("name the card a digital-only card", section);
        Assert.Contains("Cite it as a `card` footnote with the card detail's `oracle_id`", section);
        Assert.Contains("Do not say Alchemy unless a cited source says so", section);
        Assert.DoesNotContain("Make no lookup for a `not_legal` finding", section);
    }

    [Fact]
    public void Verdict_findings_carry_their_citations()
    {
        var section = Section("Format");
        foreach (var member in new[] { "`status`", "`card_findings`", "`shape_findings`", "`companion`", "`legality`", "`ban_event`" })
        {
            Assert.Contains(member, section);
        }
    }

    // The envelope carries the deck verdict, so a tier-2 run
    // reports it from the envelope and spends no second call on it.
    [Fact]
    public void Tier_2_reports_the_deck_verdict_from_the_envelope()
    {
        var section = Section("Tier 2: `POST /simulate`");

        Assert.Contains("`deck_verdict`", section);
        Assert.Contains("same verdict as `POST /deck-verdict`", section);
    }

    // Interactive disruption is the top rung. A
    // question about the deck's own counterspells, protection or
    // interaction climbs to it; a tier-3 report offers it in one line.
    [Fact]
    public void Tier_4_is_the_top_rung_of_the_cost_ladder()
    {
        var ladder = Section("Step 2: pick the cheapest tier");

        var tier3 = ladder.IndexOf("**Tier 3**", StringComparison.Ordinal);
        var tier4 = ladder.IndexOf("**Tier 4**", StringComparison.Ordinal);
        Assert.True(tier3 >= 0 && tier4 > tier3, "tier 4 sits above tier 3 on the ladder");
        var rung = ladder[tier4..];
        Assert.Contains("interactive disruption", rung);
        Assert.Contains("counterspells, protection or interaction help", rung);
    }

    [Fact]
    public void A_tier_3_report_offers_tier_4_in_one_line()
    {
        var section = Section("Tier 3: resilience");

        Assert.Contains("offer tier 4 in one line", section);
    }

    [Fact]
    public void Tier_4_posts_the_same_request_and_seed_in_both_modes()
    {
        var section = Section("Tier 4: interactive disruption");

        Assert.Contains("\"disruption_mode\": \"interactive\"", section);
        Assert.Contains("`\"abstracted\"`", section);
        Assert.Contains("same request and the same `seed`", section);
        foreach (var member in new[]
        {
            "`disruption_mode`", "`probability.label`", "`resilience_delta`", "`fired_rate`", "`answered_rate`",
            "`not_cast_rate`", "`disruption.response_cards`", "`engine_coverage.unprofiled_responses`", "`answered_by`",
        })
        {
            Assert.Contains(member, section);
        }

        Assert.Contains("both of their disrupted floors", section);
        Assert.Contains("The `resilience_delta` of each envelope", section);
        Assert.Contains("a lower bound", section);
    }

    [Fact]
    public void Tier_4_says_the_floor_difference_mixes_the_rules_with_the_answers()
    {
        var section = Section("Tier 4: interactive disruption");

        Assert.Contains("The difference between the abstracted and interactive disrupted floors mixes the real-card rules with the deck's answers", section);
    }

    // Tier 4 adds a hold run when the interactive envelope lists response cards.
    [Fact]
    public void Tier_4_makes_a_hold_run_only_when_the_deck_has_response_cards()
    {
        var section = Section("Tier 4: interactive disruption");

        Assert.Contains("Read `disruption.response_cards` from the interactive envelope", section);
        Assert.Contains("make no third call", section);
        Assert.Contains("the deck has no response cards", section);
        Assert.Contains("\"play_policy\": \"goal-aware-hold\"", section);
        Assert.Contains("Send `play_policy` only in the hold run", section);
        Assert.Contains("invalid-play-policy", section);
        Assert.Contains("Do not offer it as a separate tier", section);
    }

    [Fact]
    public void Tier_4_reports_the_hold_run_as_its_own_result_with_its_own_baseline()
    {
        var section = Section("Tier 4: interactive disruption");

        Assert.Contains("labeled \"if the pilot holds up answers\"", section);
        Assert.Contains("`policies.play`", section);
        Assert.Contains("greedy policy", section);
        Assert.Contains("every line of the hold envelope's interactive `assumptions` block", section);
        Assert.Contains("the hold baseline differs from the other baseline", section);
        Assert.Contains("80.1% against 77.5%", section);
        Assert.Contains("Compare each disrupted floor only with the baseline of its own run", section);
    }

    // The acceptance answer shortened the assumption lines and read
    // answered_rate as a share of the runs where the card was cast.
    [Fact]
    public void Tier_4_quotes_the_assumption_lines_word_for_word()
    {
        var section = Section("Tier 4: interactive disruption");
        var contract = PluginFiles.Reference("answer-contract.md");

        Assert.Contains("Quote each line word for word, as the envelope gives it", section);
        Assert.Contains("do not drop a clause", section);
        Assert.Contains("keeps each card's cost, colors and type", section);
        Assert.Contains("next to the hold floors, word for word", section);
        Assert.Contains("only when the two blocks give the same words", section);
        Assert.Contains("Quote each line word for word. Do not shorten, merge, split or skip a line", contract);
        Assert.Contains("Quote a line once for both runs only when the two blocks give the same words", contract);
    }

    [Fact]
    public void Tier_4_reports_the_event_rates_as_shares_of_all_runs()
    {
        var section = Section("Tier 4: interactive disruption");

        Assert.Contains("Each rate is a share of all runs, not of the runs where the card was cast", section);
        Assert.Contains("Say \"of all runs\" next to `answered_rate`", section);
        Assert.Contains("divide `answered_rate` by `fired_rate`", section);
    }

    // One interactive request at 10,000 pairs measured 78.9 s, so
    // the interactive cap stays at the tier-3 cap.
    [Fact]
    public void Tier_4_sends_runs_within_the_interactive_cap()
    {
        var section = Section("Tier 4: interactive disruption");

        Assert.Contains("at most 10000 pairs", section);
        var runs = Regex.Matches(section, @"""runs"":\s*(\d+)").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.NotEmpty(runs);
        Assert.All(runs, r => Assert.InRange(r, 1, 10_000));
    }

    [Fact]
    public void The_idle_opponent_line_has_an_interactive_exception()
    {
        var tier2 = Section("Tier 2: `POST /simulate`");

        Assert.Contains("no opponent that acts", tier2);
        Assert.Contains("Interactive disruption is the one exception", tier2);
    }

    // The answer contract amends its tier-3 lines for tier 4 and adds a
    // Scripted opponent bullet. The format bullet still leads.
    [Fact]
    public void The_answer_contract_states_the_scripted_opponent()
    {
        var contract = PluginFiles.Reference("answer-contract.md");
        var assumptions = contract[contract.IndexOf("## Assumptions", StringComparison.Ordinal)..];

        Assert.Contains("A tier-2, tier-3 or tier-4 probability is a floor", assumptions);
        Assert.Contains("next to each tier-2, tier-3 or tier-4 floor", assumptions);
        var bullet = Regex.Match(assumptions, @"(?m)^- \*\*Scripted opponent\.\*\*.*$").Value;
        Assert.Contains("disruption cards", bullet);
        Assert.Contains("mana rule", bullet);
        Assert.Contains("every line of the interactive `assumptions` block", bullet);

        var bullets = Regex.Matches(assumptions, @"(?m)^- \*\*([^*]+)\*\*").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal("Phrase-to-format mapping.", bullets[0]);
    }

    private static string Section(string heading)
    {
        var match = Regex.Match(SkillText, $@"(?ms)^## {Regex.Escape(heading)}\s*$(.*?)(?=^## |\z)");
        Assert.True(match.Success, $"the skill has a '## {heading}' section");
        return match.Value;
    }

    [Fact]
    public void Floors_name_the_engine_and_its_coverage()
    {
        Assert.Contains("`engine.name`", SkillText);
        Assert.Contains("`engine.version`", SkillText);
        Assert.Contains("`engine_coverage`", SkillText);
        Assert.Contains("`engine_errors`", SkillText);
        Assert.Contains("twice as many games", SkillText);
        var contract = PluginFiles.Reference("answer-contract.md");
        Assert.Contains("`engine.version`", contract);
        Assert.Contains("`engine_coverage`", contract);
    }
}
