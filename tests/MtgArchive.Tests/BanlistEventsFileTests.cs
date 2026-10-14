using MtgArchive.Banlist;
using MtgArchive.Data;

namespace MtgArchive.Tests;

/// <summary>
/// The banlist events file parser/validator: the exact file
/// format, verbatim enum literals, month-precision date tolerance, and the
/// itemized rejections that keep a bad transcription out of the database.
/// </summary>
public sealed class BanlistEventsFileTests
{
    /// <summary>The transcribed repo file, resolved from the test output directory.</summary>
    internal static readonly string TranscribedEventsPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "banlist-events.json");

    [Fact]
    public void A_valid_file_parses_versions_dates_and_events()
    {
        var errors = new List<string>();
        var versions = BanlistEventsFile.Parse(
            """
            [
              {
                "effective_date": "2004-10-04",
                "date_precision": "day",
                "announcement_url": "https://example.org/2004",
                "events": [
                  { "card": "Ancestral Recall", "action": "ban", "scope": "all" },
                  { "card": "Braids, Cabal Minion", "action": "ban", "scope": "commander_only" }
                ]
              },
              {
                "effective_date": "2007-06",
                "date_precision": "month",
                "announcement_url": "https://example.org/2007",
                "events": [
                  { "card": "Lutri, the Spellchaser", "action": "unban", "scope": "companion" }
                ]
              }
            ]
            """, errors);

        Assert.Empty(errors);
        Assert.Equal(2, versions.Count);
        Assert.Equal(new DateOnly(2004, 10, 4), versions[0].EffectiveDate);
        Assert.Equal(DatePrecision.Day, versions[0].DatePrecision);
        Assert.Equal("https://example.org/2004", versions[0].AnnouncementUrl);
        Assert.Equal(
            [new BanlistFileEvent("Ancestral Recall", BanAction.Ban, BanScope.All),
             new BanlistFileEvent("Braids, Cabal Minion", BanAction.Ban, BanScope.CommanderOnly)],
            versions[0].Events);

        // The 2002–2009 era announced by month; yyyy-MM lands on the 1st
        // with the precision recording the caveat.
        Assert.Equal(new DateOnly(2007, 6, 1), versions[1].EffectiveDate);
        Assert.Equal(DatePrecision.Month, versions[1].DatePrecision);
        Assert.Equal(
            [new BanlistFileEvent("Lutri, the Spellchaser", BanAction.Unban, BanScope.Companion)],
            versions[1].Events);
    }

    [Theory]
    [InlineData("banned", "action", "ban|unban")]
    [InlineData("Ban", "action", "ban|unban")]
    public void Unknown_actions_are_rejected_verbatim(string action, string member, string allowed)
        => AssertRejected(
            Version(action: action),
            $"unknown {member} '{action}' (expected {allowed})");

    [Theory]
    [InlineData("commander")]
    [InlineData("commanderOnly")]
    [InlineData("COMMANDER_ONLY")]
    public void Unknown_scopes_are_rejected_verbatim(string scope)
        => AssertRejected(
            Version(scope: scope),
            $"unknown scope '{scope}' (expected all|commander_only|companion)");

    [Theory]
    [InlineData("Day")]
    [InlineData("year")]
    public void Unknown_date_precisions_are_rejected_verbatim(string precision)
        => AssertRejected(
            Version(precision: precision),
            $"unknown date_precision '{precision}' (expected day|month)");

    [Fact]
    public void Day_precision_requires_a_full_date()
        => AssertRejected(
            Version(effectiveDate: "2007-06"),
            "effective_date '2007-06' is not yyyy-MM-dd");

    // A month-precision date naming a day claims precision the announcement
    // doesn't have; the first of the month is the one day that claims none.
    [Fact]
    public void Month_precision_rejects_a_date_that_names_a_day()
    {
        AssertRejected(
            Version(effectiveDate: "2007-06-15", precision: "month"),
            "effective_date '2007-06-15' names a day but date_precision is month");

        var errors = new List<string>();
        var versions = BanlistEventsFile.Parse(
            Version(effectiveDate: "2007-06-01", precision: "month"), errors);
        Assert.Empty(errors);
        Assert.Equal(new DateOnly(2007, 6, 1), Assert.Single(versions).EffectiveDate);
    }

    [Fact]
    public void Announcement_urls_must_be_absolute_http()
        => AssertRejected(
            Version(url: "example.org/2004"),
            "announcement_url 'example.org/2004' is not an absolute http(s) URL");

    [Fact]
    public void Unknown_members_are_rejected_so_typos_never_pass()
    {
        AssertRejected(
            """[{ "effective_date": "2004-10-04", "date_precision": "day", "anouncement_url": "https://x.org", "events": [] }]""",
            "unknown member 'anouncement_url'");
        AssertRejected(
            Version(eventJson: """{ "card": "Ancestral Recall", "action": "ban", "scope": "all", "note": "oops" }"""),
            "unknown member 'note'");
    }

    [Fact]
    public void Duplicate_effective_dates_are_rejected()
    {
        var errors = new List<string>();
        BanlistEventsFile.Parse($"[{VersionObject()}, {VersionObject()}]", errors);
        Assert.Contains(errors, e => e.Contains("2 versions share effective_date 2004-10-04"));
    }

    // An unrelated defect in one entry must not mask the duplicate date.
    [Fact]
    public void Duplicate_effective_dates_are_flagged_even_when_a_duplicate_is_otherwise_broken()
    {
        var errors = new List<string>();
        BanlistEventsFile.Parse(
            $"[{VersionObject()}, {VersionObject(url: "not-a-url")}]", errors);
        Assert.Contains(errors, e => e.Contains("not an absolute http(s) URL"));
        Assert.Contains(errors, e => e.Contains("2 versions share effective_date 2004-10-04"));
    }

    [Fact]
    public void Duplicate_cards_within_a_version_are_rejected()
        => AssertRejected(
            Version(eventJson:
                """
                { "card": "Ancestral Recall", "action": "ban", "scope": "all" },
                { "card": "Ancestral Recall", "action": "unban", "scope": "all" }
                """),
            "'Ancestral Recall' appears 2 times");

    [Fact]
    public void The_file_must_be_a_json_array()
    {
        AssertRejected("""{ "versions": [] }""", "must be a JSON array");
        AssertRejected("not json", "invalid JSON");
    }

    [Fact]
    public void Every_problem_is_itemized_not_just_the_first()
    {
        var errors = new List<string>();
        BanlistEventsFile.Parse(
            """
            [
              { "effective_date": "2004-10-04", "date_precision": "day", "announcement_url": "https://x.org",
                "events": [ { "card": "A", "action": "banned", "scope": "all" } ] },
              { "effective_date": "2005-10-04", "date_precision": "day", "announcement_url": "https://x.org",
                "events": [ { "card": "B", "action": "ban", "scope": "commander" } ] }
            ]
            """, errors);
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("versions[0]") && e.Contains("'banned'"));
        Assert.Contains(errors, e => e.Contains("versions[1]") && e.Contains("'commander'"));
    }

    /// <summary>The curated repo file itself, so a bad append fails here.</summary>
    [Fact]
    public void The_transcribed_data_file_parses_without_errors()
    {
        var errors = new List<string>();
        var versions = BanlistEventsFile.Read(TranscribedEventsPath, errors);
        Assert.Empty(errors);
        Assert.NotEmpty(versions);
    }

    private static void AssertRejected(string json, string expectedFragment)
    {
        var errors = new List<string>();
        var versions = BanlistEventsFile.Parse(json, errors);
        Assert.Empty(versions);
        Assert.Contains(errors, e => e.Contains(expectedFragment));
    }

    private static string Version(
        string effectiveDate = "2004-10-04",
        string precision = "day",
        string url = "https://example.org/2004",
        string action = "ban",
        string scope = "all",
        string? eventJson = null)
        => $"[{VersionObject(effectiveDate, precision, url, action, scope, eventJson)}]";

    private static string VersionObject(
        string effectiveDate = "2004-10-04",
        string precision = "day",
        string url = "https://example.org/2004",
        string action = "ban",
        string scope = "all",
        string? eventJson = null)
        => $$"""
            { "effective_date": "{{effectiveDate}}", "date_precision": "{{precision}}",
              "announcement_url": "{{url}}",
              "events": [ {{eventJson ?? $$"""{ "card": "Ancestral Recall", "action": "{{action}}", "scope": "{{scope}}" }"""}} ] }
            """;
}
