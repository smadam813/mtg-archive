using System.Text.Json;
using System.Text.RegularExpressions;

namespace MtgArchive.Tests;

/// <summary>
/// The plugin scaffold. The repo is a plugin marketplace that
/// ships the <c>mtg-archive</c> plugin. The five skill directories exist.
/// The shared references carry the locked conventions.
/// </summary>
public sealed class PluginScaffoldTests
{
    private static readonly string Root = RepoRoot.Find();
    private static readonly string Plugin = PluginFiles.Root;
    private static readonly string[] Skills = ["mtg-lookup", "deck-check", "commander-swap", "build-deck", "archive-admin"];

    [Fact]
    public void The_marketplace_manifest_lists_the_plugin_at_its_manifest_path()
    {
        using var marketplace = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, ".claude-plugin", "marketplace.json")));
        var entry = Assert.Single(marketplace.RootElement.GetProperty("plugins").EnumerateArray());

        Assert.Equal("mtg-archive", entry.GetProperty("name").GetString());
        var source = Path.GetFullPath(Path.Combine(Root, entry.GetProperty("source").GetString()!));
        Assert.Equal(Path.GetFullPath(Plugin), source);

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, ".claude-plugin", "plugin.json")));
        Assert.Equal("mtg-archive", manifest.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void Every_skill_has_a_named_skill_file_that_loads_the_answer_contract()
    {
        var found = Directory.GetDirectories(Path.Combine(Plugin, "skills")).Select(Path.GetFileName).Order();
        Assert.Equal(Skills.Order(), found);

        foreach (var skill in Skills)
        {
            var text = PluginFiles.Skill(skill);
            Assert.Matches($@"(?m)^name:\s*{Regex.Escape(skill)}\s*$", text);
            Assert.Matches(@"(?m)^description:\s*\S", text);
            Assert.Contains("references/answer-contract.md", text);
        }
    }

    [Fact]
    public void The_answer_contract_states_the_service_address_and_the_down_service_rule()
    {
        var contract = PluginFiles.Reference("answer-contract.md");

        Assert.Contains("http://127.0.0.1:8080", contract);
        Assert.Contains("MTG_ARCHIVE_URL", contract);
        Assert.Contains("docker compose up", contract);
        Assert.Contains("## Versions block", contract);
        foreach (var type in new[] { "card", "rule", "ban_event", "legality", "snapshot" })
        {
            Assert.Contains($"`{type}`", contract);
        }

        // No service response carries a run citation, so an answer
        // must not invent an unfetchable footnote for one.
        Assert.DoesNotContain("`run`", contract);
        Assert.Contains("Add no footnote for a run.", contract);
    }

    [Fact]
    public void Only_the_answer_contract_states_the_default_url()
    {
        // Reads every plugin .md in full: the assertion needs the whole set of matches, so there is no early exit.
        var statingTheDefault = Directory.GetFiles(Plugin, "*.md", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("http://127.0.0.1:8080"))
            .Select(f => Path.GetRelativePath(Plugin, f));
        Assert.Equal(["references/answer-contract.md"], statingTheDefault.Select(p => p.Replace('\\', '/')));
    }

    [Fact]
    public void The_phrase_to_bracket_mapping_is_the_locked_table()
    {
        var text = PluginFiles.Reference("phrase-to-bracket.md");
        var rows = Regex.Matches(text, @"(?m)^\|\s*`([^`]+)`\s*\|\s*([1-5])\s*\|")
            .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));

        var expected = new Dictionary<string, int>
        {
            ["precon"] = 2,
            ["jank"] = 2,
            ["theme"] = 2,
            ["casual"] = 2,
            ["upgraded"] = 3,
            ["mid"] = 3,
            ["focused"] = 3,
            ["competition-ready"] = 4,
            ["high-power"] = 4,
            ["optimized"] = 4,
            ["cEDH"] = 5,
        };
        Assert.Equal(expected.OrderBy(p => p.Key), rows.OrderBy(p => p.Key));
        Assert.Contains("ask the user", text);
    }

    // ADR 0012: brackets are Commander-only. In a Brawl format
    // power words set nothing, except that "competitive" or "ranked" Brawl
    // names the Competitive Brawl format.
    [Fact]
    public void The_phrase_to_bracket_mapping_applies_to_commander_only()
    {
        var text = PluginFiles.Reference("phrase-to-bracket.md");

        Assert.Contains("applies to Commander only", text);
        Assert.Contains("No power target: Arena publishes no bracket system for Brawl.", text);
        Assert.Matches(@"""Competitive Brawl"" or ""ranked Brawl"".*`competitive_brawl`", text);
        Assert.Contains("phrase-to-format.md", text);
    }

    // The skills-surface decision and its wording change:
    // answers say "format", and the table still matches "queue".
    [Fact]
    public void The_phrase_to_format_mapping_is_the_locked_table()
    {
        var text = PluginFiles.Reference("phrase-to-format.md");
        var rows = Regex.Matches(text, @"(?m)^\|(?!\s*(?:Wording|-))\s*(.+?)\s*\|\s*(.+?)\s*\|\s*$")
            .Select(m => (Wording: m.Groups[1].Value, Format: m.Groups[2].Value))
            .ToList();
        Assert.Equal(8, rows.Count);

        string FormatFor(string phrase) => Assert.Single(rows, r => r.Wording.Contains(phrase)).Format;

        Assert.StartsWith("`commander`", FormatFor("\"EDH\""));
        Assert.Contains("No format word", Assert.Single(rows, r => r.Format.StartsWith("`commander`")).Wording);
        Assert.StartsWith("`brawl`", FormatFor("\"Historic Brawl\""));
        Assert.StartsWith("`brawl`", FormatFor("\"Arena Brawl\" (one deck)"));
        Assert.StartsWith("`competitive_brawl`", FormatFor("\"ranked Brawl\""));
        Assert.StartsWith("`standard_brawl`", FormatFor("\"Standard Brawl\""));
        Assert.StartsWith("All three Brawl formats, one call per format", FormatFor("\"which queues\""));
        Assert.Contains("The destination is the request format", FormatFor("Competitive Brawl\")"));
        var ask = FormatFor("\"Arena\" with no \"Brawl\"");
        Assert.StartsWith("**Ask.**", ask);
        Assert.Contains("Do not guess from the deck size", ask);
        Assert.Contains("out of scope", FormatFor("\"paper Brawl\""));

        Assert.Contains("without regard to case or hyphens", text);
        Assert.Contains("Treating 'Brawl' as the Brawl format", text);
        Assert.Contains("until the user names another one", text);
    }

    [Fact]
    public void The_answer_contract_states_the_format_first_and_cites_brawl_legality()
    {
        var contract = PluginFiles.Reference("answer-contract.md");

        Assert.Contains("| `legality` | `GET /cards/{id}` |", contract);
        Assert.Contains("`<key> <key status> as of <date>`", contract);
        Assert.Contains("format commander", contract);
        Assert.Contains("format competitive_brawl, Brawl legality as of 2026-10-03 (Scryfall)", contract);
        Assert.Contains("`legality_as_of`", contract);
        Assert.Contains("**Phrase-to-format mapping.**", contract);
        Assert.Contains("phrase-to-format.md", contract);
        Assert.Contains("top of the answer", contract);

        // The format bullet comes first: it shapes every number after it.
        var assumptions = contract[contract.IndexOf("## Assumptions", StringComparison.Ordinal)..];
        Assert.True(
            assumptions.IndexOf("**Phrase-to-format mapping.**", StringComparison.Ordinal)
                < assumptions.IndexOf("**Floor label.**", StringComparison.Ordinal),
            "the format assumption leads the list");
    }

    [Fact]
    public void The_default_disruption_profile_is_the_locked_schedule()
    {
        var text = PluginFiles.Reference("default-disruption-profile.md");
        var fence = Regex.Match(text, @"```json\s*(\[.*?\])\s*```", RegexOptions.Singleline);
        Assert.True(fence.Success, "the file states the profile in one json fence");

        var events = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(fence.Groups[1].Value)!
            .Select(e => (e["kind"].GetString(), e["turn"].GetInt32()));
        Assert.Equal([("counterspell", 3), ("targeted_removal", 4)], events);
        Assert.Contains("board_wipe", text);
    }

    // One sentence on interactive mode, and still one fence.
    [Fact]
    public void The_default_disruption_profile_serves_interactive_mode_with_one_fence()
    {
        var text = PluginFiles.Reference("default-disruption-profile.md");

        Assert.Single(Regex.Matches(text, "```json"));
        Assert.Contains("`\"disruption_mode\": \"interactive\"`", text);
    }
}
