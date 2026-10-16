using MtgArchive.Api;
using MtgArchive.Data;

namespace MtgArchive.Tests;

public class ProblemsTests
{
    [Fact]
    public void Extension_members_get_snake_case_keys()
    {
        var problem = Problems
            .UnknownVersion("banlist_version", "1999-01-01")
            .WithExtension("unresolvedNames", new[] { "Sol Ring" });

        Assert.Contains("unresolved_names", problem.Extensions.Keys);
        Assert.Contains("parameter", problem.Extensions.Keys);
    }

    [Theory]
    [InlineData(Format.Brawl, "brawl", true)]
    [InlineData(Format.CompetitiveBrawl, "competitive_brawl", true)]
    [InlineData(Format.StandardBrawl, "standard_brawl", true)]
    [InlineData(Format.Standard, "standard", false)]
    public void Commander_only_names_the_format_and_mentions_Brawl_only_for_a_Brawl_format(
        Format format, string wireName, bool isBrawl)
    {
        var problem = Problems.CommanderOnly(format);

        Assert.Equal("urn:mtg-archive:problem:commander-only", problem.Type);
        Assert.Equal("Commander only", problem.Title);
        Assert.Equal(400, problem.Status);
        Assert.Equal("format", problem.Extensions["parameter"]);
        Assert.Equal(wireName, problem.Extensions["value"]);
        Assert.Contains($"The {wireName} format", problem.Detail);
        Assert.Contains("Commander only", problem.Detail);
        Assert.EndsWith("Omit format, or use commander.", problem.Detail);
        Assert.Equal(isBrawl, problem.Detail!.Contains("Brawl", StringComparison.Ordinal));
    }

    [Fact]
    public void Commander_only_gives_Standard_a_reason_that_fits_Standard()
    {
        var detail = Problems.CommanderOnly(Format.Standard).Detail;

        Assert.Contains("Wizards publishes no bracket system for Standard.", detail);
        Assert.DoesNotContain("Arena", detail);
    }

    [Fact]
    public void Commander_only_rejects_the_Commander_format()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Problems.CommanderOnly(Format.Commander));
    }
}
