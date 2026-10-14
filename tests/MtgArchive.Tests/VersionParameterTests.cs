using MtgArchive.Api;

namespace MtgArchive.Tests;

public class VersionParameterTests
{
    private static readonly IReadOnlyCollection<string> Known = SeamProbe.KnownBanlistVersions;

    [Fact]
    public void Null_pin_resolves_to_latest()
    {
        var resolved = VersionParameter.Resolve("banlist_version", null, "2025-09-22", Known);

        Assert.Equal("2025-09-22", resolved);
    }

    [Fact]
    public void Known_pin_resolves_to_the_pinned_version()
    {
        var resolved = VersionParameter.Resolve("banlist_version", "2025-04-21", "2025-09-22", Known);

        Assert.Equal("2025-04-21", resolved);
    }

    [Fact]
    public void Unknown_pin_throws_with_parameter_and_value()
    {
        var ex = Assert.Throws<ProblemException>(
            () => VersionParameter.Resolve("banlist_version", "1999-01-01", "2025-09-22", Known));

        Assert.Equal("urn:mtg-archive:problem:unknown-version", ex.Problem.Type);
        Assert.Equal("banlist_version", ex.Problem.Extensions["parameter"]);
        Assert.Equal("1999-01-01", ex.Problem.Extensions["value"]);
    }

    // An axis with no versions at all (nothing ingested yet): no latest to
    // default to, and any pin is unknown.
    [Fact]
    public void Empty_axis_resolves_null_pin_to_no_version()
    {
        Assert.Null(VersionParameter.Resolve("banlist_version", null, null, []));
    }

    [Fact]
    public void Empty_axis_rejects_every_pin()
    {
        var ex = Assert.Throws<ProblemException>(
            () => VersionParameter.Resolve("banlist_version", "2025-09-22", null, []));

        Assert.Equal("2025-09-22", ex.Problem.Extensions["value"]);
    }

    // The null latest and the empty known set must travel together: a caller
    // updating one without the other would silently drop latest-by-default.
    [Fact]
    public void Mismatched_latest_and_known_set_is_a_caller_bug()
    {
        Assert.Throws<InvalidOperationException>(
            () => VersionParameter.Resolve("banlist_version", null, null, Known));
        Assert.Throws<InvalidOperationException>(
            () => VersionParameter.Resolve("banlist_version", null, "2025-09-22", []));
    }
}
