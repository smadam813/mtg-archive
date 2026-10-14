using MtgArchive.Api;

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
}
