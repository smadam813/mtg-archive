using System.Text.Json;
using MtgArchive.Simulation;

namespace MtgArchive.Tests;

/// <summary>
/// The exact closed-form tail against independently precomputed
/// values in <c>fixtures/tier1/hypergeometric.json</c> (Python
/// <c>fractions</c> over <c>math.comb</c>). No sampling, no seed: the same
/// inputs always give the same exact probability.
/// </summary>
public sealed class HypergeometricTests
{
    public static TheoryData<string, int, int, int, int, double> FixtureCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "tier1", "hypergeometric.json");
        var cases = JsonSerializer.Deserialize<List<FixtureCase>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        var data = new TheoryData<string, int, int, int, int, double>();
        foreach (var c in cases)
        {
            data.Add(c.Note, c.K, c.Successes, c.DeckSize, c.CardsSeen, c.Expected);
        }

        return data;
    }

    private sealed record FixtureCase(
        string Note, int K, int Successes, int DeckSize, int CardsSeen, double Expected);

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void Matches_independently_computed_values(
        string note, int k, int successes, int deckSize, int cardsSeen, double expected)
    {
        Assert.NotEmpty(note);
        var actual = Hypergeometric.TailAtLeast(k, successes, deckSize, cardsSeen);
        Assert.Equal(expected, actual, 1e-15);
    }

    [Fact]
    public void At_least_zero_is_certain()
    {
        Assert.Equal(1, Hypergeometric.TailAtLeast(0, 38, 99, 7));
        Assert.Equal(1, Hypergeometric.TailAtLeast(0, 0, 0, 0));
    }

    [Fact]
    public void More_than_the_reachable_maximum_is_impossible()
    {
        Assert.Equal(0, Hypergeometric.TailAtLeast(8, 38, 99, 7));
        Assert.Equal(0, Hypergeometric.TailAtLeast(5, 4, 99, 30));
        Assert.Equal(0, Hypergeometric.TailAtLeast(1, 0, 99, 7));
    }

    // A partial list smaller than the cards-seen count: seeing "more" cards
    // than the deck holds means seeing the whole deck.
    [Fact]
    public void Seeing_past_the_deck_end_sees_the_whole_deck()
    {
        Assert.Equal(1, Hypergeometric.TailAtLeast(3, 3, 5, 12));
        Assert.Equal(0, Hypergeometric.TailAtLeast(4, 3, 5, 12));
    }

    // Once the non-successes run out, some successes are forced into view:
    // terms below that floor are zero, and the floor itself is certain.
    [Fact]
    public void Forced_successes_floor_is_exact()
    {
        Assert.Equal(1, Hypergeometric.TailAtLeast(7, 8, 10, 9));
        Assert.Equal(0.2, Hypergeometric.TailAtLeast(8, 8, 10, 9), 1e-15);
    }

    [Theory]
    [InlineData(1, -1, 99, 7)]
    [InlineData(1, 100, 99, 7)]
    [InlineData(1, 38, -1, 7)]
    [InlineData(1, 38, 99, -1)]
    [InlineData(1, 38, 10_001, 7)]
    public void Out_of_range_inputs_throw(int k, int successes, int deckSize, int cardsSeen)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => Hypergeometric.TailAtLeast(k, successes, deckSize, cardsSeen));
}
