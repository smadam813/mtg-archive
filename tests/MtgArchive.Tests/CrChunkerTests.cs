using MtgArchive.Rules;

namespace MtgArchive.Tests;

/// <summary>
/// CR parsing against the excerpt fixture: the effective date
/// from the header, chunk granularity (one chunk per numbered rule with its
/// subrules and examples; glossary one per entry), the skipped table of
/// contents, and the published file's structural anomalies.
/// </summary>
public sealed class CrChunkerTests
{
    private static CrParse ParseFixture()
    {
        var errors = new List<string>();
        var parse = CrChunker.Parse(CrChunker.Normalize(CrFixtures.RawExcerpt()), errors);
        Assert.Empty(errors);
        return parse;
    }

    [Fact]
    public void Effective_date_parses_from_the_header()
    {
        Assert.Equal(new DateOnly(2026, 8, 7), ParseFixture().EffectiveDate);
    }

    [Fact]
    public void Normalize_strips_the_bom_and_crlf_line_endings()
    {
        var normalized = CrChunker.Normalize(CrFixtures.RawExcerpt());

        Assert.StartsWith("Magic: The Gathering", normalized);
        Assert.DoesNotContain('\r', normalized);
    }

    [Fact]
    public void One_chunk_per_numbered_rule_and_the_toc_produces_none()
    {
        var parse = ParseFixture();

        Assert.Equal(
            ["100.1", "100.2", "100.3", "101.1", "119.1", "704.5"],
            parse.Rules.Select(r => r.Number).ToArray());
    }

    [Fact]
    public void A_rule_chunk_carries_its_subrules_verbatim()
    {
        var chunk = ParseFixture().Rules.Single(r => r.Number == "100.1");

        Assert.Equal(
            "100.1. A test game involves two or more players.\n"
            + "\n"
            + "100.1a A two-player test game begins with only two players.\n"
            + "\n"
            + "100.1b A multiplayer test game begins with more than two players. See section 8, “Multiplayer Rules.”",
            chunk.Text);
    }

    [Fact]
    public void Example_paragraphs_stay_in_their_rules_chunk()
    {
        var chunk = ParseFixture().Rules.Single(r => r.Number == "100.2");

        Assert.Contains("Example: A player may track life totals on paper.", chunk.Text);
        Assert.Contains("Example: A player may track life totals with dice.", chunk.Text);
    }

    [Fact]
    public void Indented_note_paragraphs_stay_in_their_rules_chunk()
    {
        var chunk = ParseFixture().Rules.Single(r => r.Number == "100.3");

        Assert.Contains("     A note paragraph indented under the rule stays inside its chunk.", chunk.Text);
    }

    // The published file carries two numbering anomalies: a subrule with a
    // trailing period ("119.1d.") and a two-letter subrule suffix
    // ("704.5aa"). Both are chunk content, never chunk boundaries.
    [Fact]
    public void Anomalous_subrule_numbers_stay_in_their_chunks()
    {
        var parse = ParseFixture();

        Assert.Contains("119.1d. In a two-player variant test game",
            parse.Rules.Single(r => r.Number == "119.1").Text);
        Assert.Contains("704.5aa If a player controls a test permanent",
            parse.Rules.Single(r => r.Number == "704.5").Text);
    }

    [Fact]
    public void Glossary_chunks_one_entry_per_term()
    {
        var parse = ParseFixture();

        Assert.Equal(
            ["Abandon", "Ability", "Active Player"],
            parse.Glossary.Select(g => g.Term).ToArray());
        Assert.Equal(
            "Ability\n"
            + "1. Text on an object that explains what that object does.\n"
            + "2. An activated or triggered ability on the stack.\n"
            + "See rule 113, “Abilities.”",
            parse.Glossary.Single(g => g.Term == "Ability").Text);
    }

    [Fact]
    public void Credits_are_not_glossary_content()
    {
        Assert.DoesNotContain(ParseFixture().Glossary, g => g.Text.Contains("credits line"));
    }

    [Fact]
    public void A_missing_effective_date_is_a_reported_error()
    {
        var text = CrChunker.Normalize(CrFixtures.RawExcerpt())
            .Replace("These rules are effective as of August 7, 2026.", "");
        var errors = new List<string>();

        CrChunker.Parse(text, errors);

        Assert.Contains(errors, e => e.Contains("effective as of"));
    }

    [Fact]
    public void Text_without_the_cr_structure_reports_errors_instead_of_chunking()
    {
        var errors = new List<string>();

        var parse = CrChunker.Parse("<html>an error page, not the CR</html>", errors);

        Assert.NotEmpty(errors);
        Assert.Empty(parse.Rules);
        Assert.Empty(parse.Glossary);
    }
}
