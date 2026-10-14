using MtgArchive.Banlist;
using MtgArchive.Data;

namespace MtgArchive.Tests;

/// <summary>
/// The banlist categories file parser/validator: the exact
/// file format, the closed member-rule vocabulary, and the itemized
/// rejections that keep a bad category out of the database.
/// </summary>
public sealed class BanlistCategoriesFileTests
{
    /// <summary>The curated repo file, resolved from the test output directory.</summary>
    internal static readonly string CuratedCategoriesPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "banlist-categories.json");

    [Fact]
    public void A_valid_file_parses_each_member_rule()
    {
        var errors = new List<string>();
        var categories = BanlistCategoriesFile.Parse(
            $"[{Category("ante", """{ "oracle_word": "ante" }""", "2009-03", "month")},"
            + $"{Category("conspiracy", """{ "card_type": "Conspiracy" }""")},"
            + $"{Category("racist_depictions", """{ "cards": ["Jihad", "Crusade"] }""")}]",
            errors);

        Assert.Empty(errors);
        Assert.Equal(3, categories.Count);
        Assert.Equal("ante", categories[0].Key);
        Assert.Equal("Wording for ante", categories[0].ListWording);
        Assert.Equal(new DateOnly(2009, 3, 1), categories[0].EffectiveDate);
        Assert.Equal(DatePrecision.Month, categories[0].DatePrecision);
        Assert.Equal("https://example.org/categories", categories[0].AnnouncementUrl);
        Assert.Equal(new CategoryMembers.OracleWord("ante"), categories[0].Members);
        Assert.Equal(new CategoryMembers.CardType("Conspiracy"), categories[1].Members);
        Assert.Equal(new CategoryMembers.Cards(["Jihad", "Crusade"]), categories[2].Members);
    }

    [Theory]
    [InlineData("""{ "card_type": "Conspiracy", "oracle_word": "ante" }""", "exactly one rule")]
    [InlineData("""{}""", "exactly one rule")]
    [InlineData("""{ "subtype": "Aura" }""", "unknown rule 'subtype'")]
    [InlineData("""{ "oracle_word": "an.te" }""", "letters only")]
    [InlineData("""{ "cards": [] }""", "non-empty array")]
    [InlineData("""{ "cards": ["Jihad", "Jihad"] }""", "'Jihad' appears 2 times")]
    public void A_bad_member_rule_is_rejected(string members, string expectedFragment)
        => AssertRejected($"[{Category("bad", members)}]", expectedFragment);

    [Fact]
    public void Duplicate_keys_unknown_members_and_bad_fields_are_itemized()
    {
        var errors = new List<string>();
        BanlistCategoriesFile.Parse(
            $"[{Category("ante", """{ "oracle_word": "ante" }""")},"
            + $"{Category("ante", """{ "card_type": "Conspiracy" }""")},"
            + """{ "category": "x", "list_wording": "x", "effective_date": "2020-05-18", "date_precision": "week", "announcement_url": "ftp://x", "members": { "card_type": "Conspiracy" }, "scope": "all" }]""",
            errors);

        Assert.Contains(errors, e => e.Contains("2 categories share the key 'ante'"));
        Assert.Contains(errors, e => e.Contains("categories[2]") && e.Contains("unknown member 'scope'"));
        Assert.Contains(errors, e => e.Contains("categories[2]") && e.Contains("unknown date_precision 'week'"));
        Assert.Contains(errors, e => e.Contains("categories[2]") && e.Contains("not an absolute http(s) URL"));
    }

    [Fact]
    public void A_missing_file_is_an_error()
    {
        var errors = new List<string>();
        Assert.Empty(BanlistCategoriesFile.Read(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"), errors));
        Assert.Contains(errors, e => e.Contains("banlist-categories: no file at"));
    }

    /// <summary>The curated repo file itself, so a bad edit fails here.</summary>
    [Fact]
    public void The_curated_data_file_parses_without_errors()
    {
        var errors = new List<string>();
        var categories = BanlistCategoriesFile.Read(CuratedCategoriesPath, errors);
        Assert.Empty(errors);
        Assert.Equal(["ante", "conspiracy", "racist_depictions"], categories.Select(c => c.Key).Order(StringComparer.Ordinal));
    }

    private static void AssertRejected(string json, string expectedFragment)
    {
        var errors = new List<string>();
        var categories = BanlistCategoriesFile.Parse(json, errors);
        Assert.Empty(categories);
        Assert.Contains(errors, e => e.Contains(expectedFragment));
    }

    private static string Category(string key, string members, string effectiveDate = "2020-05-18", string precision = "day")
        => $$"""
            {
              "category": "{{key}}",
              "list_wording": "Wording for {{key}}",
              "effective_date": "{{effectiveDate}}",
              "date_precision": "{{precision}}",
              "announcement_url": "https://example.org/categories",
              "members": {{members}}
            }
            """;
}
