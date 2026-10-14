using System.Text.Json;
using MtgArchive.Api;
using MtgArchive.Bracket;

namespace MtgArchive.Tests;

/// <summary>
/// The pure classifier over the committed 2025-10-21 spec:
/// the four-part output with no bare bracket field, thresholds read from
/// the spec, declared assumptions raising the assumed bracket but never
/// the floor, and per-barometer reasons carrying the firing cards.
/// </summary>
public sealed class BracketClassifierTests
{
    private static readonly BracketSpec Spec = LoadSpec();

    private static readonly ClassifiedCard Commander = new(Guid.NewGuid(), "Test Commander");
    private static readonly ClassifiedCard[] GameChangers =
        [.. Enumerable.Range(1, 4).Select(i => new ClassifiedCard(Guid.NewGuid(), $"Game Changer {i}"))];
    private static readonly ClassifiedCard Armageddon = new(Guid.NewGuid(), "Armageddon");
    private static readonly ClassifiedCard TimeWarp = new(Guid.NewGuid(), "Time Warp");
    private static readonly ClassifiedCard Forest = new(Guid.NewGuid(), "Forest");

    private static readonly BarometerSets Sets = new(
        GameChangers.Select(c => c.OracleId).ToHashSet(),
        new HashSet<Guid> { Armageddon.OracleId },
        new HashSet<Guid> { TimeWarp.OracleId });

    [Fact]
    public void A_deck_with_no_signal_has_the_lowest_floor_and_no_reasons()
    {
        var result = BracketClassifier.Classify(Spec, [Commander, Forest], Sets, 2, BracketAssumptions.None);

        Assert.Equal(1, result.BracketFloor.Bracket);
        Assert.Equal(2, result.BracketFloor.TargetBracket);
        Assert.True(result.BracketFloor.WithinTarget);
        Assert.Empty(result.Reasons);
        Assert.Equal(AssumedBracketStatus.Unknown, result.AssumedBracket.Status);
        Assert.Null(result.AssumedBracket.Bracket);
        Assert.Null(result.AssumedBracket.WithinTarget);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    public void Game_changers_count_reads_the_spec_thresholds(int count, int expectedFloor)
    {
        var deck = GameChangers.Take(count).Prepend(Commander).Append(Forest).ToList();

        var result = BracketClassifier.Classify(Spec, deck, Sets, 3, BracketAssumptions.None);

        Assert.Equal(expectedFloor, result.BracketFloor.Bracket);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal("game_changers_count", reason.Barometer);
        Assert.Equal(count, reason.Count);
        Assert.Equal(expectedFloor, reason.FloorBracket);
        Assert.Equal(Spec["game_changers_count"].Thresholds[expectedFloor].Text, reason.Threshold);
        Assert.Equal(GameChangers.Take(count).Select(c => c.Name), reason.Cards.Select(c => c.Label));
        Assert.All(reason.Cards, c => Assert.Equal(CitationType.Card, c.Type));
    }

    [Fact]
    public void Every_fired_barometer_carries_its_cards_and_the_floor_is_the_highest()
    {
        var deck = new[] { Commander, GameChangers[0], Armageddon, TimeWarp, Forest };

        var result = BracketClassifier.Classify(Spec, deck, Sets, 3, BracketAssumptions.None);

        Assert.Equal(["game_changers_count", "mass_land_denial", "chained_extra_turns"],
            result.Reasons.Select(r => r.Barometer).ToArray());
        Assert.Equal([3, 4, 2], result.Reasons.Select(r => r.FloorBracket).ToArray());
        Assert.Equal(Armageddon.OracleId.ToString(), Assert.Single(result.Reasons[1].Cards).Id);
        Assert.Equal("Time Warp", Assert.Single(result.Reasons[2].Cards).Label);
        Assert.Equal(4, result.BracketFloor.Bracket);
        Assert.False(result.BracketFloor.WithinTarget);
    }

    [Fact]
    public void A_commander_counts_like_any_other_decklist_card()
    {
        var result = BracketClassifier.Classify(Spec, [GameChangers[0], Forest], Sets, 3, BracketAssumptions.None);

        Assert.Equal(1, Assert.Single(result.Reasons).Count);
    }

    [Fact]
    public void Declared_intent_raises_the_assumed_bracket_but_never_the_floor()
    {
        var deck = new[] { Commander, GameChangers[0], Forest };

        var result = BracketClassifier.Classify(Spec, deck, Sets, 4, new BracketAssumptions { IntentBracket = 4 });

        Assert.Equal(3, result.BracketFloor.Bracket);
        Assert.Equal(4, result.AssumedBracket.Bracket);
        Assert.Equal(AssumedBracketStatus.Declared, result.AssumedBracket.Status);
        Assert.True(result.AssumedBracket.WithinTarget);
        var intent = result.Assumptions.Single(a => a.Name == "intent_bracket");
        Assert.True(intent.Declared);
        Assert.Equal(4, intent.Value);
        var chains = result.Assumptions.Single(a => a.Name == "chains_extra_turns");
        Assert.False(chains.Declared);
        Assert.Null(chains.Value);
    }

    [Fact]
    public void Declared_intent_below_the_floor_leaves_the_assumed_bracket_at_the_floor()
    {
        var deck = new[] { Commander, Armageddon, Forest };

        var result = BracketClassifier.Classify(Spec, deck, Sets, 4, new BracketAssumptions { IntentBracket = 2 });

        Assert.Equal(4, result.BracketFloor.Bracket);
        Assert.Equal(4, result.AssumedBracket.Bracket);
    }

    [Fact]
    public void Declared_chaining_raises_extra_turns_to_the_chaining_bracket()
    {
        var deck = new[] { Commander, TimeWarp, Forest };

        var plain = BracketClassifier.Classify(Spec, deck, Sets, 2, BracketAssumptions.None);
        var chained = BracketClassifier.Classify(Spec, deck, Sets, 2, new BracketAssumptions { ChainsExtraTurns = true });
        var declined = BracketClassifier.Classify(Spec, deck, Sets, 2, new BracketAssumptions { ChainsExtraTurns = false });

        Assert.Equal(2, plain.BracketFloor.Bracket);
        Assert.Null(plain.AssumedBracket.Bracket);
        Assert.Equal(2, chained.BracketFloor.Bracket);
        Assert.Equal(4, chained.AssumedBracket.Bracket);
        Assert.False(chained.AssumedBracket.WithinTarget);
        Assert.Equal(2, declined.AssumedBracket.Bracket);
        Assert.Equal(AssumedBracketStatus.Declared, declined.AssumedBracket.Status);
    }

    [Fact]
    public void Chaining_effect_text_agrees_with_the_assumed_bracket_when_intent_is_also_declared()
    {
        var deck = new[] { Commander, TimeWarp, Forest };

        var above = BracketClassifier.Classify(Spec, deck, Sets, 4,
            new BracketAssumptions { IntentBracket = 5, ChainsExtraTurns = true });
        var below = BracketClassifier.Classify(Spec, deck, Sets, 4,
            new BracketAssumptions { IntentBracket = 3, ChainsExtraTurns = true });

        Assert.Equal(5, above.AssumedBracket.Bracket);
        Assert.Contains("at or below the assumed bracket of 5", above.Assumptions.Single(a => a.Name == "chains_extra_turns").Effect);
        Assert.Equal(4, below.AssumedBracket.Bracket);
        Assert.StartsWith("raises the assumed bracket to 4", below.Assumptions.Single(a => a.Name == "chains_extra_turns").Effect);
    }

    [Fact]
    public void Declared_chaining_without_an_extra_turn_card_has_no_effect()
    {
        var result = BracketClassifier.Classify(Spec, [Commander, Forest], Sets, 2, new BracketAssumptions { ChainsExtraTurns = true });

        Assert.Equal(1, result.AssumedBracket.Bracket);
        Assert.Contains("no extra-turn card", result.Assumptions.Single(a => a.Name == "chains_extra_turns").Effect);
    }

    [Fact]
    public void The_wire_shape_has_exactly_the_four_parts_and_no_bare_bracket_field()
    {
        var result = BracketClassifier.Classify(Spec, [Commander, GameChangers[0]], Sets, 3, new BracketAssumptions { IntentBracket = 3 });

        using var doc = JsonSerializer.SerializeToDocument(result, WireJson.Options);
        Assert.Equal(["bracket_floor", "assumed_bracket", "reasons", "assumptions"],
            doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.False(doc.RootElement.TryGetProperty("bracket", out _));
        Assert.Equal(3, doc.RootElement.GetProperty("bracket_floor").GetProperty("bracket").GetInt32());
        Assert.Equal("declared", doc.RootElement.GetProperty("assumed_bracket").GetProperty("status").GetString());
        var reason = Assert.Single(doc.RootElement.GetProperty("reasons").EnumerateArray());
        Assert.Equal("card", Assert.Single(reason.GetProperty("cards").EnumerateArray()).GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.String, reason.GetProperty("threshold").ValueKind);
    }

    [Fact]
    public void A_target_outside_the_spec_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BracketClassifier.Classify(Spec, [Commander], Sets, 6, BracketAssumptions.None));
    }

    private static BracketSpec LoadSpec()
    {
        var errors = new List<string>();
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "bracket", "spec-2025-10-21.json");
        return BracketSpec.Parse("spec-2025-10-21.json", File.ReadAllText(path), errors)
            ?? throw new InvalidOperationException(string.Join("; ", errors));
    }
}
