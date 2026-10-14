using System.Text.RegularExpressions;

namespace MtgArchive.Tests;

/// <summary>
/// The archive-admin skill: the keep-the-archive-current chores,
/// composed from the collection, ingest, and corpus-state routes. The skill
/// file is prose an agent follows. These tests pin the contract lines the
/// acceptance criteria name: the routes and bodies it sends, the reports it
/// renders verbatim, and the judgment it refuses.
/// </summary>
public sealed class ArchiveAdminSkillTests
{
    private static readonly string Text = File.ReadAllText(Path.Combine(
        RepoRoot.Find(), "plugin", "mtg-archive", "skills", "archive-admin", "SKILL.md"));

    [Theory]
    [InlineData("PUT /collection")]
    [InlineData("GET /collection")]
    [InlineData("POST /ingest")]
    [InlineData("GET /ingest/runs/{id}")]
    [InlineData("GET /ingest/runs/latest")]
    [InlineData("GET /corpus-state")]
    public void The_skill_composes_the_six_admin_routes(string route)
        => Assert.Contains(route, Text);

    [Fact]
    public void The_collection_import_sends_the_locked_body_and_states_full_replace_before_importing()
    {
        Assert.Contains("manabox_csv", Text);
        Assert.Matches(@"""format""\s*:", Text);
        Assert.Matches(@"""content""\s*:", Text);
        // The replace warning comes before the import step (ASD-STE100: warning before the step).
        var warning = Text.IndexOf("full replace", StringComparison.Ordinal);
        var import = Text.IndexOf("curl -sS -X PUT", StringComparison.Ordinal);
        Assert.True(warning >= 0 && import > warning, "the full-replace statement precedes the import call");
    }

    [Fact]
    public void The_quarantine_report_is_surfaced_verbatim_inside_the_2xx_and_never_an_error()
    {
        Assert.Contains("quarantine", Text);
        Assert.Contains("verbatim", Text);
        foreach (var field in new[] { "line_number", "reason", "no_match", "ambiguous", "candidate_oracle_ids" })
        {
            Assert.Contains(field, Text);
        }
        Assert.Matches(@"(?i)not an error", Text);
    }

    [Fact]
    public void The_ingest_trigger_expects_202_and_reports_an_active_run_instead_of_retrying()
    {
        Assert.Contains("202", Text);
        Assert.Contains("run_id", Text);
        Assert.Matches(@"""force""\s*:", Text);
        Assert.Matches(@"(?i)do not retry", Text);
    }

    [Fact]
    public void The_ingest_report_lists_every_item_including_drift()
    {
        foreach (var item in new[]
        {
            "new_cards", "changed_cards", "drift_detections", "cr_version_found", "stale_cards",
            "collection_rows_pointing_at_stale_cards", "unmatched_search_lands",
            "unmatched_enters_tapped_lands", "unmatched_opponent_sources", "unprofiled_cards", "response_cards", "brawl_legality",
            "skipped_popularity_rows", "errors",
        })
        {
            Assert.Contains(item, Text);
        }
        Assert.Contains("no_op", Text);
        Assert.Contains("phases", Text);
    }

    [Fact]
    public void The_format_section_names_the_brawl_counts_and_status_changes_and_keeps_drift_commander_only()
    {
        var section = Regex.Match(Text, @"(?ms)^### Format\s*$(.*?)(?=^#{2,3} )");
        Assert.True(section.Success, "the skill has a Format section");
        var body = section.Groups[1].Value;
        foreach (var field in new[]
        {
            "brawl_legality", "format", "legality_as_of", "legal", "banned", "not_legal",
            "status_changes", "from", "to",
        })
        {
            Assert.Contains($"`{field}`", body);
        }
        foreach (var format in new[] { "brawl", "competitive_brawl", "standard_brawl" })
        {
            Assert.Contains($"`{format}`", body);
        }
        Assert.Matches(@"(?i)drift[^.\n]*commander", body);
    }

    [Fact]
    public void The_drift_kinds_the_skill_names_are_the_detectors_kinds()
    {
        foreach (var kind in new[] { "banlist", Bracket.BracketLoadPhase.DriftKind })
        {
            Assert.Contains($"`{kind}`", Text);
        }
    }

    [Fact]
    public void The_phase_list_is_the_canonical_order()
    {
        var listed = Regex.Matches(Text, @"`([a-z_]+)`")
            .Select(m => m.Groups[1].Value)
            .Where(Ingest.IngestPhases.CanonicalOrder.Contains)
            .Distinct();
        Assert.Equal(Ingest.IngestPhases.CanonicalOrder, listed);
    }

    [Fact]
    public void The_corpus_state_renders_the_versions_at_rest()
    {
        foreach (var field in new[]
        {
            "banlist_version", "cr_effective_date", "gc_snapshot",
            "embedding_state", "last_phase_runs", "freshness",
        })
        {
            Assert.Contains(field, Text);
        }
    }

    // A skipped export row names its format, seat, and reason.
    [Fact]
    public void The_format_section_names_the_skipped_popularity_rows_and_their_reasons()
    {
        var body = Section("Format");
        foreach (var field in new[]
        {
            "skipped_popularity_rows", "format", "name", "seat", "reason", "no_match", "ambiguous", "duplicate",
        })
        {
            Assert.Contains($"`{field}`", body);
        }
    }

    // The popularity axes at rest. A format with an export reads
    // untapped as of the window end; every other format reads the EDHREC proxy.
    [Fact]
    public void The_corpus_state_shows_the_popularity_axes()
    {
        var body = Section("Report the corpus state");
        foreach (var field in new[]
        {
            "popularity_exports", "format", "window_start", "window_end", "file_sha256",
            "popularity_source", "popularity_as_of", "untapped", "edhrec",
        })
        {
            Assert.Contains($"`{field}`", body);
        }
    }

    private static string Section(string heading)
    {
        var section = Regex.Match(Text, $@"(?ms)^#{{2,3}} {Regex.Escape(heading)}\s*$(.*?)(?=^#{{2,3}} )");
        Assert.True(section.Success, $"the skill has a {heading} section");
        return section.Groups[1].Value;
    }

    [Fact]
    public void The_skill_exercises_no_judgment_and_names_drift_response_as_human_work()
    {
        Assert.Matches(@"(?i)\bnever\b[^.\n]*\b(filter|resolve|act on)", Text);
        Assert.Matches(@"(?i)re-ingest", Text);
        Assert.Matches(@"(?i)human", Text);
    }

    [Fact]
    public void The_skill_defers_the_service_address_and_the_down_service_rule_to_the_answer_contract()
    {
        Assert.Contains("references/answer-contract.md", Text);
        Assert.Contains("MTG_ARCHIVE_URL", Text);
        Assert.Contains("compose stack", Text);
    }

    [Fact]
    public void Every_shell_step_is_one_fenced_curl_against_the_env_var_address()
    {
        var fences = Regex.Matches(Text, @"```sh\s*(.*?)```", RegexOptions.Singleline);
        Assert.NotEmpty(fences);
        foreach (Match fence in fences)
        {
            foreach (var line in fence.Groups[1].Value.Split('\n').Where(l => l.Contains("curl ")))
            {
                Assert.Contains("$MTG_ARCHIVE_URL", line);
            }
        }
    }
}
