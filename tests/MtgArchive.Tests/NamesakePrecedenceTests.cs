using MtgArchive.Cards;

namespace MtgArchive.Tests;

/// <summary>
/// The shared namesake rule: live rows win over stale rows,
/// then non-token rows win over token rows. Each step keeps every row when
/// nothing survives it.
/// </summary>
public sealed class NamesakePrecedenceTests
{
    private sealed record Row(string Id, bool Stale, string? Layout);

    [Fact]
    public void A_real_card_wins_over_its_token_namesake()
    {
        Assert.Equal(["elves"], Candidates(new("elves", false, "normal"), new("token", false, "token")));
    }

    [Theory]
    [InlineData("token")]
    [InlineData("double_faced_token")]
    [InlineData("emblem")]
    public void Every_token_layout_loses_to_a_real_card(string layout)
    {
        Assert.Equal(["real"], Candidates(new("real", false, "normal"), new("token", false, layout)));
    }

    [Fact]
    public void Token_only_names_stand()
    {
        Assert.Equal(["a", "b"], Candidates(new("a", false, "token"), new("b", false, "token")));
    }

    [Fact]
    public void Two_real_cards_and_a_token_keep_only_the_real_cards()
    {
        Assert.Equal(["a", "b"],
            Candidates(new("a", false, "normal"), new("b", false, "normal"), new("token", false, "token")));
    }

    // A database that has not re-ingested since the layout column arrived
    // stays ambiguous: the safe failure.
    [Fact]
    public void A_null_layout_counts_as_non_token()
    {
        Assert.Equal(["a", "b"], Candidates(new("a", false, null), new("b", false, null)));
    }

    [Fact]
    public void Live_over_stale_applies_before_non_token_over_token()
    {
        Assert.Equal(["live-token"], Candidates(new("stale-real", true, "normal"), new("live-token", false, "token")));
    }

    [Fact]
    public void A_lone_stale_row_stands()
    {
        Assert.Equal(["stale"], Candidates(new Row("stale", true, "normal")));
    }

    private static List<string> Candidates(params Row[] rows)
        => [.. NamesakePrecedence.Candidates(rows.ToList(), r => r.Stale, r => r.Layout).Select(r => r.Id)];
}
