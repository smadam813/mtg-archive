using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// Disruption events: the closed vocabulary and its wire
/// parsing, the one-shot schedule, and the assumptions block. XMage plays
/// the mechanics; the XMage tier-3 tests cover them on real games.
/// </summary>
public sealed class DisruptionTests
{
    private static readonly DeckCard Sheoldred = TestDeckCards.Commander("Sheoldred, the Apocalypse", "{2}{B}{B}");
    private static readonly DeckCard Swamp = TestDeckCards.Land("Swamp", "B", quantity: 30);
    private static readonly DeckCard Needle = TestDeckCards.Spell("Pithing Needle", "{1}", "Artifact", quantity: 2);
    private static readonly DeckCard Signet = TestDeckCards.Spell("Orzhov Signet", "{2}", "Artifact");
    private static readonly DeckCard Nighthawk = TestDeckCards.Spell("Vampire Nighthawk", "{1}{B}{B}", "Creature");
    private static readonly DeckCard Bear = TestDeckCards.Spell("Grizzly Bears", "{1}{B}", "Creature");
    private static readonly DeckCard Aardvark = TestDeckCards.Spell("Aardvark", "{1}", "Artifact");
    private static readonly DeckCard Zebra = TestDeckCards.Spell("Zebra", "{1}", "Artifact");

    private static readonly IReadOnlyList<DeckCard> Deck = [Sheoldred, Swamp, Needle, Signet, Nighthawk, Bear, Aardvark, Zebra];

    private static DisruptionProfile Profile(params (DisruptionKind Kind, int Turn)[] events)
        => new([.. events.Select(e => new DisruptionEvent(e.Kind, e.Turn))]);

    /// <summary>The batch runner aggregates per event on this order, so
    /// the schedule keeps one outcome per profile event in profile order,
    /// duplicates included.</summary>
    [Fact]
    public void The_schedule_keeps_one_outcome_per_profile_event_in_profile_order()
    {
        var profile = Profile((DisruptionKind.BoardWipe, 6), (DisruptionKind.Counterspell, 3), (DisruptionKind.Counterspell, 3), (DisruptionKind.TargetedRemoval, 4));
        var schedule = new DisruptionSchedule(profile, null, Deck);

        Assert.Equal(profile.Events, schedule.Events.Select(o => o.Event));
        Assert.All(schedule.Events, o => Assert.True(o.Unused));
    }

    [Fact]
    public void The_vocabulary_is_closed_at_three_kinds_and_parses_from_the_wire()
    {
        Assert.Equal(["counterspell", "targeted_removal", "board_wipe"], DisruptionProfile.Vocabulary);

        var errors = new List<string>();
        var profile = DisruptionProfileParser.Parse(
            """[{"kind":"counterspell","turn":3},{"kind":"targeted_removal","turn":4},{"kind":"targeted_removal","turn":4},{"kind":"board_wipe","turn":6}]""",
            errors);

        Assert.Empty(errors);
        Assert.Equal(
            Profile((DisruptionKind.Counterspell, 3), (DisruptionKind.TargetedRemoval, 4), (DisruptionKind.TargetedRemoval, 4), (DisruptionKind.BoardWipe, 6)),
            profile);
        Assert.Equal(new DisruptionProfile([]), DisruptionProfileParser.Parse("[]", errors));
        Assert.Empty(errors);
    }

    [Fact]
    public void An_unknown_kind_a_bad_turn_or_a_bad_shape_is_a_request_error_naming_the_vocabulary()
    {
        static IReadOnlyList<string> Errors(string json)
        {
            var errors = new List<string>();
            Assert.Null(DisruptionProfileParser.Parse(json, errors));
            Assert.NotEmpty(errors);
            return errors;
        }

        var unknown = Assert.Single(Errors("""[{"kind":"discard","turn":2}]"""));
        Assert.Contains("$[0].kind", unknown);
        Assert.Contains("counterspell, targeted_removal, board_wipe", unknown);

        Assert.Contains(Errors("""[{"kind":"counterspell","turn":0}]"""), e => e.Contains("$[0].turn"));
        Assert.Contains(Errors("""[{"kind":"counterspell"}]"""), e => e.Contains("missing required property 'turn'"));
        Assert.Contains(Errors("""[{"kind":"counterspell","turn":1,"target":"x"}]"""), e => e.Contains("unknown property 'target'"));
        Assert.Contains(Errors("""{"kind":"counterspell","turn":1}"""), e => e.Contains("must be an array"));
        Assert.Contains(Errors("""["counterspell"]"""), e => e.Contains("$[0]") && e.Contains("must be an object"));

        Assert.Throws<ArgumentOutOfRangeException>(() => new DisruptionEvent(DisruptionKind.Counterspell, 0));
    }

    [Fact]
    public void The_assumptions_block_declares_no_protection_blind_pilot_adversarial_targeting_and_no_indestructible()
    {
        Assert.Equal(4, DisruptionProfile.Assumptions.Count);
        Assert.StartsWith("No protection", DisruptionProfile.Assumptions[0]);
        Assert.StartsWith("Blind pilot", DisruptionProfile.Assumptions[1]);
        Assert.StartsWith("Adversarial targeting", DisruptionProfile.Assumptions[2]);
        Assert.StartsWith("No indestructible", DisruptionProfile.Assumptions[3]);
    }

    /// <summary>The mode travels with the profile, so two
    /// profiles with the same events and another mode are not equal.</summary>
    [Fact]
    public void A_profile_carries_its_mode_and_its_equality_and_text_include_it()
    {
        var abstracted = Profile((DisruptionKind.Counterspell, 3));
        var interactive = abstracted with { Mode = DisruptionMode.Interactive };

        Assert.Equal(DisruptionMode.Abstracted, abstracted.Mode);
        Assert.NotEqual(abstracted, interactive);
        Assert.Equal(interactive, Profile((DisruptionKind.Counterspell, 3)) with { Mode = DisruptionMode.Interactive });
        Assert.NotEqual(abstracted.GetHashCode(), interactive.GetHashCode());
        Assert.NotEqual(new DisruptionProfile([]), new DisruptionProfile([], DisruptionMode.Interactive));
        Assert.Equal("[counterspell (turn 3)]", abstracted.ToString());
        Assert.Equal("interactive [counterspell (turn 3)]", interactive.ToString());
    }

    [Fact]
    public void The_modes_parse_from_their_wire_names()
    {
        Assert.Equal(["abstracted", "interactive"], DisruptionProfile.Modes);
        Assert.Equal(DisruptionMode.Abstracted, DisruptionProfile.ModeOf("abstracted"));
        Assert.Equal(DisruptionMode.Interactive, DisruptionProfile.ModeOf("interactive"));
        Assert.Equal("interactive", DisruptionProfile.NameOf(DisruptionMode.Interactive));
        Assert.Throws<ArgumentOutOfRangeException>(() => DisruptionProfile.ModeOf("scripted"));
    }

    /// <summary>ADR 0013: the owner-approved card table, one card per kind.</summary>
    [Fact]
    public void The_disruption_card_table_maps_each_kind_to_its_card()
    {
        var counterspell = DisruptionCards.For(DisruptionKind.Counterspell);
        Assert.Equal("Counterspell", counterspell.Name);
        Assert.Equal(2, counterspell.ManaValue);
        Assert.Equal(["Instant"], counterspell.Types);
        Assert.Equal(["U"], counterspell.Colors);

        var atomize = DisruptionCards.For(DisruptionKind.TargetedRemoval);
        Assert.Equal("Atomize", atomize.Name);
        Assert.Equal(4, atomize.ManaValue);
        Assert.Equal(["Instant"], atomize.Types);
        Assert.Equal(["B", "G"], atomize.Colors);

        var rout = DisruptionCards.For(DisruptionKind.BoardWipe);
        Assert.Equal("Rout", rout.Name);
        Assert.Equal(5, rout.ManaValue);
        Assert.Equal(["Sorcery"], rout.Types);
        Assert.Equal(["W"], rout.Colors);

        Assert.Equal(["Counterspell", "Atomize", "Rout"], DisruptionCards.All.Select(c => c.Name));
        Assert.Equal(Enum.GetValues<DisruptionKind>(), DisruptionCards.All.Select(c => c.Kind));
    }

    /// <summary>The interactive assumptions say how Rout is
    /// cast, that it fires under the tier-3 wipe condition with no target,
    /// and that tokens and animated creatures die to it.</summary>
    [Fact]
    public void The_interactive_assumptions_state_the_rout_rules()
    {
        var assumptions = DisruptionProfile.InteractiveAssumptions;
        Assert.Equal(11, assumptions.Count);
        Assert.Contains("board_wipe casts Rout ({3}{W}{W}, white sorcery) through its \"pay {2} more\" flash ability", assumptions[0]);
        Assert.Contains("A board wipe fires when the tier-3 rule fires it and has no target: Rout's own text decides what dies.", assumptions[3]);
        Assert.Contains("Tokens and animated creatures die to Rout.", assumptions[7]);
    }
}
