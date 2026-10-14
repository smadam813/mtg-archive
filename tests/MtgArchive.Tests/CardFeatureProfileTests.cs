using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

public sealed class CardFeatureProfileTests
{
    [Fact]
    public void None_has_no_feature()
    {
        var none = CardFeatureProfile.None;
        Assert.False(none.Develops);
        Assert.False(none.Draws);
        Assert.Empty(none.TapMana);
        Assert.Empty(none.HandMana);
        Assert.Null(none.SpellMana);
        Assert.Null(none.Tutor);
        Assert.False(none.HasActivatedAbility);
        Assert.Empty(none.ProducedColors);
    }

    [Fact]
    public void Produced_colors_read_every_tap_option_once()
    {
        var dual = CardFeatureProfile.None with { TapMana = ["{G}", "{U}"] };
        var solRing = CardFeatureProfile.None with { TapMana = ["{C}{C}"] };

        Assert.Equal(["G", "U"], dual.ProducedColors);
        Assert.Equal(["C"], solRing.ProducedColors);
    }

    [Fact]
    public void A_generic_symbol_produces_no_color()
    {
        var generic = CardFeatureProfile.None with { TapMana = ["{2}"] };

        Assert.Empty(generic.ProducedColors);
    }

    [Fact]
    public void Profiles_with_the_same_lists_are_equal()
    {
        var a = CardFeatureProfile.None with { TapMana = new List<string> { "{C}{C}" } };
        var b = CardFeatureProfile.None with { TapMana = new[] { "{C}{C}" } };
        var empty = new CardFeatureProfile(false, false, new List<string>(), new List<string>(), null, null, false);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(CardFeatureProfile.None, empty);
        Assert.NotEqual(a, CardFeatureProfile.None);
    }

    [Fact]
    public void Profiles_with_the_same_response_are_equal()
    {
        static CardFeatureProfile Negate(IReadOnlyList<string> excluded, IReadOnlyList<ProtectionGrant> grants)
            => CardFeatureProfile.None with { Response = new ResponseFeature(new CounterFeature(CounterReach.Spell, [], excluded, [], null, false), grants) };

        var a = Negate(new List<string> { "Creature" }, new List<ProtectionGrant>());
        var b = Negate(new[] { "Creature" }, Array.Empty<ProtectionGrant>());
        var other = Negate(["Land"], []);

        Assert.Null(CardFeatureProfile.None.Response);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, other);
        Assert.NotEqual(a, CardFeatureProfile.None);
    }

    [Fact]
    public void A_tutor_feature_keeps_its_parts()
    {
        var fetch = new TutorFeature(TutorDestination.Battlefield, EntersTapped: true, TutorVia.ActivateOnBattlefield, ActivationCost: null);

        Assert.Equal(TutorDestination.Battlefield, fetch.Destination);
        Assert.True(fetch.EntersTapped);
        Assert.Equal(TutorVia.ActivateOnBattlefield, fetch.Via);
        Assert.Null(fetch.ActivationCost);
    }
}
