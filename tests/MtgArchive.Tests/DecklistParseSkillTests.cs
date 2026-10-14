namespace MtgArchive.Tests;

/// <summary>
/// Pasted deck text in the skills (ADR 0012): every skill that
/// takes a deck points to the parse route, an export with no format word
/// means ask, and every deck answer reports the <c>A-</c> rewrites under
/// Assumptions.
/// </summary>
public sealed class DecklistParseSkillTests
{
    [Theory]
    [InlineData("deck-check")]
    [InlineData("build-deck")]
    [InlineData("commander-swap")]
    public void Every_skill_that_takes_a_deck_points_to_the_parse_route(string skill)
    {
        Assert.Contains("POST /decklist-parse", PluginFiles.Skill(skill));
    }

    [Fact]
    public void Deck_check_shows_the_notes_and_asks_for_the_format_of_an_export_with_no_format_word()
    {
        var line = PluginFiles.Skill("deck-check").Split('\n').Single(l => l.Contains("POST /decklist-parse"));

        Assert.Contains("`notes`", line);
        Assert.Contains("phrase-to-format.md", line);
        Assert.Contains("ask", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_answer_contract_reports_each_name_rewrite_as_an_assumption()
    {
        var contract = PluginFiles.Reference("answer-contract.md");
        var assumptions = contract[contract.IndexOf("## Assumptions", StringComparison.Ordinal)..];

        Assert.Contains("`name_rewrites`", assumptions);
        Assert.Contains("`A-`", assumptions);
    }
}
