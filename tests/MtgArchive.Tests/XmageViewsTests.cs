using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tests;

/// <summary>
/// The XMage adapter's pilot view (glossary: Engine adapter): wire names
/// map to the deck's own cards, a back face to the back face, a blank card
/// or a token to a card with no features, and XMage's mana text to the
/// pool symbols the pilot reads.
/// </summary>
public sealed class XmageViewsTests
{
    private const string Missing = "No Such Card In Xmage";

    private const string Shatterskull = "Shatterskull Smashing // Shatterskull, the Hammer Pass";

    private static readonly DeckCard Island = TestDeckCards.Land("Island", "U", quantity: 30);

    private static readonly DeckCard Needle = TestDeckCards.Spell("Pithing Needle", "{1}", "Artifact");

    private static readonly DeckCard Ritual = TestDeckCards.Spell("Dark Ritual", "{B}", "Instant");

    private static readonly DeckCard Delta = TestDeckCards.Land("Polluted Delta", "C") with { IsBasic = false, Supertypes = [], ProducedColors = [] };

    private static readonly DeckCard Mdfc = TestDeckCards.Spell(Shatterskull, "{X}{R}{R}") with
    {
        FrontFaceName = "Shatterskull Smashing",
        BackFace = TestDeckCards.Land("Shatterskull, the Hammer Pass", "R") with { IsBasic = false, Supertypes = [] },
    };

    private static readonly CardFeatureProfile IslandProfile = new(true, false, ["{U}"], [], null, null, true);

    private static readonly CardFeatureProfile RitualProfile = new(true, false, [], [], "{B}{B}{B}", null, false);

    private static readonly CardFeatureProfile DeltaProfile = new(true, false, [], [], null, new TutorFeature(TutorDestination.Battlefield, false, TutorVia.ActivateOnBattlefield, null), true);

    private static XmageViews Views() => new(
        [Island, Needle, Ritual, Delta, Mdfc, TestDeckCards.Spell(Missing, "{2}")],
        new DeckFeatureProfiles(
            new Dictionary<string, CardFeatureProfile>
            {
                ["Island"] = IslandProfile,
                ["Dark Ritual"] = RitualProfile,
                ["Polluted Delta"] = DeltaProfile,
                ["Shatterskull, the Hammer Pass"] = new(true, false, ["{R}"], [], null, null, true),
                [Missing] = new(false, true, [], [], null, null, false),
            },
            []),
        [Missing]);

    private static WireView View(
        IReadOnlyList<string>? hand = null,
        IReadOnlyList<WirePermanent>? battlefield = null,
        IReadOnlyList<WireAction>? actions = null,
        IReadOnlyList<WireResolved>? resolved = null,
        IReadOnlyList<WireManaSource>? sources = null,
        string pool = "")
        => new(2, "main", 38, true, pool, hand ?? [], ["Dark Ritual"], [], [], battlefield ?? [], 80, sources ?? [], [], resolved ?? [],
            new Dictionary<string, IReadOnlyList<string>> { ["Polluted Delta"] = ["Island"] }, actions ?? []);

    [Fact]
    public void Each_name_maps_to_the_decks_own_card()
    {
        var view = Views().View(View(hand: ["Island", "Island", "Pithing Needle"]));

        Assert.Equal(2, view.Turn);
        Assert.Equal(TurnStep.Main, view.Step);
        Assert.Equal(38, view.Life);
        Assert.True(view.LandDropUsed);
        Assert.Same(Island, view.Hand[0]);
        Assert.Same(Island, view.Hand[1]);
        Assert.Same(Needle, view.Hand[2]);
        Assert.Same(Ritual, view.Graveyard[0]);
        Assert.Same(Island, Assert.Single(view.Finds["Polluted Delta"]));
    }

    [Fact]
    public void A_back_face_maps_to_the_back_face_on_the_battlefield_in_an_action_and_in_a_resolved_cast()
    {
        var view = Views().View(View(
            battlefield: [new WirePermanent("p:1", Shatterskull, "back", false)],
            actions:
            [
                new WireAction("a:1", "land", Shatterskull, "back", "hand", "", null, false),
                new WireAction("a:2", "cast", Shatterskull, "back", "hand", "{R}", null, false),
            ],
            resolved: [new WireResolved(Shatterskull, "back", 1)]));

        Assert.Equal("Shatterskull, the Hammer Pass", view.Battlefield[0].Card.Name);
        Assert.Same(Mdfc, view.Battlefield[0].Card.WholeCard);
        Assert.Equal(EngineActionKind.PlayLand, view.Actions[0].Kind);
        Assert.Equal("Shatterskull, the Hammer Pass", view.Actions[0].Face.Name);
        Assert.Equal(["{R}"], view.Actions[0].ManaAdded);
        Assert.Equal(EngineActionKind.Cast, view.Actions[1].Kind);
        Assert.Equal("Shatterskull, the Hammer Pass", view.Actions[1].Face.Name);
        Assert.True(view.ResolvedCasts[0].Card.Answers("Shatterskull, the Hammer Pass"));
        Assert.Equal(1, view.ResolvedCasts[0].Turn);
    }

    [Fact]
    public void A_front_face_and_a_face_name_map_to_the_whole_card()
    {
        var views = Views();
        var view = views.View(View(battlefield: [new WirePermanent("p:2", "Island", "front", true)]));

        Assert.Same(Island, view.Battlefield[0].Card);
        Assert.True(view.Battlefield[0].Tapped);
        Assert.Same(Mdfc, views.Card("Shatterskull Smashing"));
        Assert.Same(Mdfc, views.Face(Shatterskull, "front"));
    }

    [Fact]
    public void A_name_outside_the_deck_is_one_card_with_no_features()
    {
        var views = Views();
        var token = views.Card("Treasure");

        Assert.Same(token, views.Card("Treasure"));
        Assert.Equal(CardFeatureProfile.None, views.Features(token));
        Assert.Equal(["Treasure"], views.OutsideNames);
    }

    [Fact]
    public void A_blank_card_has_no_features_whatever_its_profile_row()
    {
        var views = Views();

        Assert.Equal(CardFeatureProfile.None, views.Features(views.Card(Missing)));
        Assert.Equal(IslandProfile, views.Features(Island));
        Assert.Empty(views.OutsideNames);
    }

    [Fact]
    public void Mana_reads_the_pool_symbols_and_expands_any_color()
    {
        var views = Views();
        var view = views.View(View(
            pool: "{B}{B}",
            sources: [new WireManaSource("p:1", ["{C}{C}"]), new WireManaSource("p:2", ["{Any}"]), new WireManaSource("p:3", ["{2}", "20{C}"])]));

        Assert.Equal(2, view.Mana.Pool["B"]);
        Assert.Equal(38, view.Mana.Life);
        Assert.Equal(2, view.Mana.Sources.Count); // p:3 has no option the pilot can read
        Assert.Equal(["{C}{C}"], view.Mana.Sources[0].Options);
        Assert.Equal(["{W}", "{U}", "{B}", "{R}", "{G}"], view.Mana.Sources[1].Options);
        Assert.Equal(["20{C}", "{2}"], views.UnreadMana);
    }

    [Fact]
    public void An_action_carries_its_cost_its_features_and_the_mana_it_adds()
    {
        var view = Views().View(View(actions:
        [
            new WireAction("a:c", "cast", "Dark Ritual", "front", "hand", "{B}", null, false),
            new WireAction("a:t", "cast_commander", "Pithing Needle", "front", "command_zone", "{3}", null, false),
            new WireAction("a:f", "activate", "Polluted Delta", "front", "battlefield", "", "p:9", true),
        ]));

        var ritual = view.Actions[0];
        Assert.Equal(EngineActionKind.Cast, ritual.Kind);
        Assert.Same(Ritual, ritual.Face);
        Assert.Equal(RitualProfile, ritual.Features);
        Assert.Equal(["{B}{B}{B}"], ritual.ManaAdded);
        Assert.Equal(0, ritual.Cost.Generic);
        Assert.Single(ritual.Cost.Pips);
        Assert.Null(ritual.PermanentId);

        Assert.Equal(EngineActionKind.CastCommander, view.Actions[1].Kind);
        Assert.Equal(3, view.Actions[1].Cost.Generic);

        var fetch = view.Actions[2];
        Assert.Equal(EngineActionKind.Activate, fetch.Kind);
        Assert.Equal("p:9", fetch.PermanentId);
        Assert.True(fetch.SpendsSource);
        Assert.Empty(fetch.ManaAdded);
        Assert.Equal(TutorVia.ActivateOnBattlefield, fetch.Features.Tutor!.Via);
        Assert.True(fetch.Features.Develops);
        Assert.Empty(fetch.Features.TapMana);
    }

    [Fact]
    public void Only_an_activation_can_spend_its_source()
    {
        var view = Views().View(View(actions:
        [
            new WireAction("a:c", "cast", "Dark Ritual", "front", "hand", "{B}", null, true),
        ]));

        Assert.False(view.Actions[0].SpendsSource);
    }

    [Fact]
    public void An_activation_takes_only_a_battlefield_search_from_the_cards_profile()
    {
        var card = new CardFeatureProfile(true, true, ["{B}"], [], null, new TutorFeature(TutorDestination.Hand, false, TutorVia.Cast, null), false);

        var features = XmageViews.ActivationFeatures(card);

        Assert.Null(features.Tutor);
        Assert.False(features.Develops);
        Assert.True(features.Draws);
        Assert.Empty(features.TapMana);
        Assert.Null(features.SpellMana);
        Assert.True(features.HasActivatedAbility);
    }

    [Theory]
    [InlineData("tutor", PickOption.FindNothingId)]
    [InlineData("scry", PickOption.KeepRestId)]
    [InlineData("other", PickOption.KeepRestId)]
    public void The_stop_option_maps_by_purpose_and_goes_back_as_none(string purpose, string stop)
    {
        var seen = purpose == "tutor" ? null : new[] { "Island" };
        var choice = Views().Pick(new WirePick(purpose, [new WirePickOption("o1", "Island", "Island"), new WirePickOption("none", "Find nothing", null)], seen));

        Assert.Equal(XmageViews.Purpose(purpose), choice.Purpose);
        Assert.Same(Island, choice.Options[0].Card);
        Assert.Equal(stop, choice.Options[1].Id);
        Assert.True(choice.Options[1].IsNothing);
        Assert.Equal("none", XmageViews.WireOptionId(stop));
        Assert.Equal("o1", XmageViews.WireOptionId("o1"));
        Assert.Equal(seen is null, choice.Seen is null);
    }

    /// <summary>
    /// The wire's seen stays the same for every pick of one scry (shim
    /// ruling F4). Our engine's seen drops each card the scry already put on
    /// the bottom, and the pilot bottoms a seen card, so a scry's seen is
    /// its card options: every card it has not put on the bottom, top first.
    /// </summary>
    [Fact]
    public void A_scry_sees_only_the_cards_it_has_not_put_on_the_bottom()
    {
        var views = Views();
        var first = views.Pick(new WirePick("scry", [new WirePickOption("o1", "Pithing Needle", "Pithing Needle"), new WirePickOption("o2", "Island", "Island"), new WirePickOption("o3", "Island", "Island"), new WirePickOption("none", "Keep the rest on top", null)], ["Pithing Needle", "Island", "Island"]));
        var second = views.Pick(new WirePick("scry", [new WirePickOption("o1", "Pithing Needle", "Pithing Needle"), new WirePickOption("o2", "Island", "Island"), new WirePickOption("none", "Keep the rest on top", null)], ["Pithing Needle", "Island", "Island"]));
        var look = views.Pick(new WirePick("other", [new WirePickOption("o1", "Island", "Island"), new WirePickOption("none", "Find nothing", null)], ["Pithing Needle", "Island"]));

        Assert.Equal([Needle, Island, Island], first.Seen);
        Assert.Equal([Needle, Island], second.Seen);
        Assert.Equal([Needle, Island], look.Seen);
    }

    [Fact]
    public void Every_wire_step_and_purpose_maps_and_an_unknown_one_throws()
    {
        Assert.Equal(
            [TurnStep.Mulligan, TurnStep.Untap, TurnStep.Upkeep, TurnStep.Draw, TurnStep.Main, TurnStep.End, TurnStep.Cleanup],
            new[] { "mulligan", "untap", "upkeep", "draw", "main", "end", "cleanup" }.Select(XmageViews.Step));
        Assert.Equal(
            [PickPurpose.Tutor, PickPurpose.Scry, PickPurpose.OrderTop, PickPurpose.Shuffle, PickPurpose.PutBack, PickPurpose.Name, PickPurpose.TriggerOrder, PickPurpose.Other],
            new[] { "tutor", "scry", "order_top", "shuffle", "put_back", "name", "trigger_order", "other" }.Select(XmageViews.Purpose));
        Assert.Throws<InvalidDataException>(() => XmageViews.Step("combat"));
        Assert.Throws<InvalidDataException>(() => XmageViews.Purpose("mode"));
    }

    [Fact]
    public void A_reply_names_the_whole_card()
        => Assert.Equal(Shatterskull, XmageViews.WireName(Mdfc.AsBackFace()!));

    /// <summary>The counterspell rule's library: the deck less every card the view shows and the spell.</summary>
    [Fact]
    public void Reachable_cards_are_the_hand_the_command_zone_and_the_deck_less_what_the_view_shows()
    {
        var views = Views();
        var needleOnBoard = views.View(View(battlefield: [new WirePermanent("p:1", "Pithing Needle", "front", false)]));
        var empty = views.View(View());
        var islandInHand = views.View(View(hand: ["Island"]));

        Assert.DoesNotContain(views.Reachable(needleOnBoard, views.Card("Island")), c => c.Name == "Pithing Needle");
        Assert.Contains(views.Reachable(empty, views.Card("Island")), c => c.Name == "Pithing Needle");
        Assert.DoesNotContain(views.Reachable(empty, views.Card("Pithing Needle")), c => c.Name == "Pithing Needle"); // on the stack
        Assert.DoesNotContain(views.Reachable(empty, views.Card("Island")), c => c.Name == "Dark Ritual"); // the one Ritual is in the graveyard
        Assert.Contains(views.Reachable(islandInHand, views.Card("Island")), c => c.Name == "Island");
        Assert.Same(Island, views.Deck[0]);
    }

    /// <summary>
    /// Roadmap item 21: a back face on the battlefield counts against its whole card's quantity, the
    /// same as the front face would. This fixture's deck has no commander, so it does not cover the
    /// command-zone or cast-as-commander parts of that rule.
    /// </summary>
    [Fact]
    public void Reachable_counts_a_back_face_as_its_whole_card()
    {
        var views = Views();
        var backFaceOnBoard = views.View(View(battlefield: [new WirePermanent("p:1", Shatterskull, "back", false)]));
        var empty = views.View(View());

        Assert.DoesNotContain(views.Reachable(backFaceOnBoard, views.Card("Island")), c => c.Name == Shatterskull);
        Assert.Contains(views.Reachable(empty, views.Card("Island")), c => c.Name == Shatterskull);
    }
}
