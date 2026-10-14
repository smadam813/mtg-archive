using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>
/// The goal-aware pilot's response rule (ADR 0013) on pilot
/// views built by hand. A counterspell threatens the goal when the goal
/// wants a victim's face; removal threatens it when a victim answers an
/// <c>on_battlefield</c> leaf due this turn or later. The
/// pilot then takes the cheapest action whose
/// counter reaches the disruption card's types and colors, or whose
/// one-shot protection grant answers the card and covers a threatened
/// victim. It skips goal cards, "unless its controller pays"
/// counters and pacts.
/// </summary>
public sealed class GoalAwarePilotResponseTests
{
    private static readonly DeckCard Needle = TestDeckCards.Spell("Pithing Needle", "{1}", "Artifact");
    private static readonly DeckCard Bear = TestDeckCards.Spell("Grizzly Bears", "{1}{G}", "Creature");
    private static readonly DeckCard Counterspell = TestDeckCards.Spell("Counterspell", "{U}{U}", "Instant");
    private static readonly DeckCard Negate = TestDeckCards.Spell("Negate", "{1}{U}", "Instant");
    private static readonly DeckCard Pyroblast = TestDeckCards.Spell("Pyroblast", "{R}", "Instant");
    private static readonly DeckCard Hydroblast = TestDeckCards.Spell("Hydroblast", "{U}", "Instant");
    private static readonly DeckCard EssenceScatter = TestDeckCards.Spell("Essence Scatter", "{1}{U}", "Instant");
    private static readonly DeckCard SpellPierce = TestDeckCards.Spell("Spell Pierce", "{U}", "Instant");
    private static readonly DeckCard Pact = TestDeckCards.Spell("Pact of Negation", "{0}", "Instant");
    private static readonly DeckCard Stifle = TestDeckCards.Spell("Stifle", "{U}", "Instant");
    private static readonly DeckCard Dissolve = TestDeckCards.Spell("Dissolve", "{1}{U}{U}", "Instant");

    private static readonly GoalPredicate NeedleByThree = new GoalPredicate.OnBattlefield("Pithing Needle", 3);

    private static CounterFeature Counter(IReadOnlyList<string>? types = null, IReadOnlyList<string>? excluded = null, IReadOnlyList<string>? colors = null, string? unlessPays = null, bool losesGame = false, CounterReach reach = CounterReach.Spell)
        => new(reach, types ?? [], excluded ?? [], colors ?? [], unlessPays, losesGame);

    private static readonly IReadOnlyDictionary<string, CardFeatureProfile> Profiles = new Dictionary<string, CardFeatureProfile>
    {
        ["Counterspell"] = Answer(Counter()),
        ["Dissolve"] = Answer(Counter()),
        ["Negate"] = Answer(Counter(excluded: ["Creature"])),
        ["Pyroblast"] = Answer(Counter(colors: ["U"])),
        ["Hydroblast"] = Answer(Counter(colors: ["R"])),
        ["Essence Scatter"] = Answer(Counter(types: ["Creature"])),
        ["Spell Pierce"] = Answer(Counter(excluded: ["Creature"], unlessPays: "{2}")),
        ["Pact of Negation"] = Answer(Counter(losesGame: true)),
        ["Stifle"] = Answer(Counter(reach: CounterReach.Ability)),
    };

    private static readonly DeckCard SnakeskinVeil = TestDeckCards.Spell("Snakeskin Veil", "{G}", "Instant");
    private static readonly DeckCard HeroicIntervention = TestDeckCards.Spell("Heroic Intervention", "{1}{G}", "Instant");
    private static readonly DeckCard TeferisProtection = TestDeckCards.Spell("Teferi's Protection", "{2}{W}", "Instant");
    private static readonly DeckCard VeilOfSummer = TestDeckCards.Spell("Veil of Summer", "{G}", "Instant");
    private static readonly DeckCard RedWard = TestDeckCards.Spell("Test Protection From Red", "{W}", "Instant");

    private static CardFeatureProfile Answer(CounterFeature counter) => CardFeatureProfile.None with { Response = new ResponseFeature(counter, []) };

    private static CardFeatureProfile Grants(params (string Grant, string Scope)[] grants)
        => CardFeatureProfile.None with { Response = new ResponseFeature(null, [.. grants.Select(g => new ProtectionGrant(g.Grant, g.Scope))]) };

    private static readonly IReadOnlyDictionary<string, CardFeatureProfile> GrantProfiles = new Dictionary<string, CardFeatureProfile>
    {
        ["Snakeskin Veil"] = Grants((ProtectionGrant.Hexproof, ProtectionGrant.TargetCreature)),
        ["Heroic Intervention"] = Grants((ProtectionGrant.Hexproof, ProtectionGrant.Permanents), (ProtectionGrant.Indestructible, ProtectionGrant.Permanents)),
        ["Teferi's Protection"] = Grants((ProtectionGrant.PhaseOut, ProtectionGrant.Permanents)),
        ["Flawless Maneuver"] = Grants((ProtectionGrant.Indestructible, ProtectionGrant.Creatures)),
        ["Veil of Summer"] = Grants(
            (ProtectionGrant.CantBeCountered, ProtectionGrant.Spells),
            (ProtectionGrant.HexproofFrom("U"), ProtectionGrant.Permanents),
            (ProtectionGrant.HexproofFrom("B"), ProtectionGrant.Permanents)),
        ["Test Protection From Red"] = Grants((ProtectionGrant.ProtectionFrom("R"), ProtectionGrant.TargetCreature)),
    };

    private static CardFeatureProfile Features(DeckCard face)
        => Profiles.GetValueOrDefault(face.Name) ?? GrantProfiles.GetValueOrDefault(face.Name) ?? CardFeatureProfile.None;

    private static EngineAction Cast(DeckCard card) => new($"a:{card.Name}", EngineActionKind.Cast, card, ManaCost.Parse(card.ManaCost), null, Features(card), [], false);

    private static PilotView View(IReadOnlyList<ViewPermanent>? battlefield, params DeckCard[] castable) => new(
        Turn: 3,
        TurnStep.Main,
        Life: 40,
        LandDropUsed: true,
        new ManaBoard(new ManaPool(), 40, [], []),
        castable,
        battlefield ?? [],
        [],
        [],
        [],
        [],
        new Dictionary<string, IReadOnlyList<DeckCard>>(),
        [.. castable.Select(Cast)],
        Features);

    /// <summary>The opponent's Counterspell aims at <paramref name="victim"/>.</summary>
    private static ResponseChoice CounterspellOn(DeckCard victim) => new(DisruptionCards.Counterspell, "s:9", [new ResponseVictim("s:1", victim)]);

    [Fact]
    public void A_counter_on_a_goal_spell_is_answered()
    {
        var pilot = new GoalAwarePilot(NeedleByThree);

        Assert.Equal("a:Counterspell", pilot.Respond(View(null, Counterspell), CounterspellOn(Needle)));
    }

    [Fact]
    public void With_no_goal_threat_the_pilot_lets_the_card_resolve()
    {
        var pilot = new GoalAwarePilot(NeedleByThree);

        // The goal does not want the Bear.
        Assert.Null(pilot.Respond(View(null, Counterspell), CounterspellOn(Bear)));
        // The Needle is on the battlefield already: the goal holds and wants no other Needle.
        Assert.Null(pilot.Respond(View([new ViewPermanent("p:1", Needle, false)], Counterspell), CounterspellOn(Needle)));
        // A wipe whose creatures answer no goal leaf threatens nothing.
        Assert.Null(pilot.Respond(View([new ViewPermanent("p:1", Bear, false)], Counterspell), RoutOn(Bear)));
    }

    /// <summary>The opponent's Rout: its victims are our creatures.</summary>
    private static ResponseChoice RoutOn(params DeckCard[] creatures)
        => new(DisruptionCards.Rout, "s:9", [.. creatures.Select((c, i) => new ResponseVictim($"p:{i + 1}", c))]);

    private static readonly DeckCard FlawlessManeuver = TestDeckCards.Spell("Flawless Maneuver", "{2}{W}", "Instant");

    /// <summary>A wipe threatens the goal when a creature on
    /// the battlefield answers an <c>on_battlefield</c> leaf due this turn
    /// or later. A team indestructible or team phase-out grant answers it
    /// (Heroic Intervention, Flawless Maneuver, Teferi's Protection); a
    /// target grant and a counter that reaches no white sorcery do not.</summary>
    [Fact]
    public void A_wipe_of_a_goal_creature_is_answered_by_a_team_grant()
    {
        var bears = BearsOnTheBattlefield(SnakeskinVeil, HeroicIntervention, FlawlessManeuver, Hydroblast);
        Assert.Equal("a:Heroic Intervention", new GoalAwarePilot(BearsByThree).Respond(bears, RoutOn(Bear)));
        Assert.Equal("a:Flawless Maneuver", new GoalAwarePilot(BearsByThree).Respond(BearsOnTheBattlefield(SnakeskinVeil, FlawlessManeuver), RoutOn(Bear)));
        Assert.Null(new GoalAwarePilot(BearsByThree).Respond(BearsOnTheBattlefield(SnakeskinVeil, Hydroblast), RoutOn(Bear)));
        Assert.Equal("a:Counterspell", new GoalAwarePilot(BearsByThree).Respond(BearsOnTheBattlefield(Counterspell, HeroicIntervention), RoutOn(Bear)));

        // A leaf due before this turn (the view is turn 3) has its verdict already.
        Assert.Null(new GoalAwarePilot(new GoalPredicate.OnBattlefield("Grizzly Bears", 2)).Respond(bears, RoutOn(Bear)));

        // Phase-out holds only when every threatened leaf is due after this turn.
        Assert.Null(new GoalAwarePilot(BearsByThree).Respond(BearsOnTheBattlefield(TeferisProtection), RoutOn(Bear)));
        Assert.Equal("a:Teferi's Protection", new GoalAwarePilot(new GoalPredicate.OnBattlefield("Grizzly Bears", 4)).Respond(BearsOnTheBattlefield(TeferisProtection), RoutOn(Bear)));
    }

    /// <summary>The opponent's Atomize aims at <paramref name="victim"/> on the battlefield.</summary>
    private static ResponseChoice AtomizeOn(DeckCard victim) => new(DisruptionCards.Atomize, "s:9", [new ResponseVictim("p:1", victim)]);

    private static PilotView NeedleOnTheBattlefield(params DeckCard[] castable) => View([new ViewPermanent("p:1", Needle, false)], castable);

    /// <summary>Removal threatens the goal when its victim
    /// answers an <c>on_battlefield</c> leaf due this turn or later, even
    /// while the leaf holds.</summary>
    [Fact]
    public void Removal_of_a_goal_permanent_due_this_turn_or_later_is_answered()
    {
        Assert.Equal("a:Counterspell", new GoalAwarePilot(NeedleByThree).Respond(NeedleOnTheBattlefield(Counterspell), AtomizeOn(Needle)));
        Assert.Equal("a:Counterspell", new GoalAwarePilot(new GoalPredicate.OnBattlefield("Pithing Needle", 5)).Respond(NeedleOnTheBattlefield(Counterspell), AtomizeOn(Needle)));
    }

    [Fact]
    public void Removal_threatens_nothing_once_the_leaf_is_due_before_this_turn_or_off_the_goal()
    {
        // The view is turn 3: a leaf due on turn 2 has its verdict already.
        Assert.Null(new GoalAwarePilot(new GoalPredicate.OnBattlefield("Pithing Needle", 2)).Respond(NeedleOnTheBattlefield(Counterspell), AtomizeOn(Needle)));
        Assert.Null(new GoalAwarePilot(NeedleByThree).Respond(View([new ViewPermanent("p:1", Bear, false)], Counterspell), AtomizeOn(Bear)));
        // A cast leaf is not an on_battlefield leaf: a resolved cast holds for good.
        Assert.Null(new GoalAwarePilot(new GoalPredicate.Cast("Pithing Needle", 3)).Respond(NeedleOnTheBattlefield(Counterspell), AtomizeOn(Needle)));
    }

    /// <summary>Atomize is a black and green instant: Negate reaches it;
    /// Pyroblast (blue only), Hydroblast (red only), a creature-only and an
    /// ability counter do not. The cheapest answer goes first.</summary>
    [Fact]
    public void Against_atomize_only_a_counter_whose_reach_covers_a_black_green_instant_qualifies()
    {
        var pilot = new GoalAwarePilot(NeedleByThree);

        Assert.Null(pilot.Respond(NeedleOnTheBattlefield(Pyroblast, Hydroblast, EssenceScatter, Stifle), AtomizeOn(Needle)));
        Assert.Equal("a:Negate", pilot.Respond(NeedleOnTheBattlefield(Pyroblast, Dissolve, Negate), AtomizeOn(Needle)));
        Assert.Null(pilot.Respond(NeedleOnTheBattlefield(SpellPierce, Pact), AtomizeOn(Needle)));
    }

    /// <summary>Counterspell is a blue instant: Negate and Pyroblast reach
    /// it; a creature-only, a red-only and an ability counter do not.</summary>
    [Fact]
    public void Only_a_counter_whose_reach_covers_the_disruption_card_qualifies()
    {
        var pilot = new GoalAwarePilot(NeedleByThree);

        Assert.Null(pilot.Respond(View(null, EssenceScatter, Hydroblast, Stifle), CounterspellOn(Needle)));
        Assert.Equal("a:Negate", pilot.Respond(View(null, EssenceScatter, Negate), CounterspellOn(Needle)));
        Assert.Equal("a:Pyroblast", pilot.Respond(View(null, Hydroblast, Pyroblast), CounterspellOn(Needle)));
        Assert.Null(pilot.Respond(View(null, Bear), CounterspellOn(Needle))); // a card with no response feature
    }

    [Fact]
    public void The_pilot_skips_unless_pays_counters_pacts_and_goal_cards()
    {
        Assert.Null(new GoalAwarePilot(NeedleByThree).Respond(View(null, SpellPierce, Pact), CounterspellOn(Needle)));

        // A goal that wants Counterspell cast keeps it: the pilot answers with Dissolve.
        var goal = new GoalPredicate.AllOf([NeedleByThree, new GoalPredicate.Cast("Counterspell", 3)]);
        Assert.Equal("a:Dissolve", new GoalAwarePilot(goal).Respond(View(null, Counterspell, Dissolve), CounterspellOn(Needle)));
        Assert.Null(new GoalAwarePilot(goal).Respond(View(null, Counterspell), CounterspellOn(Needle)));
    }

    /// <summary>The lowest mana value goes first, then the view's order.</summary>
    [Fact]
    public void The_cheapest_answer_goes_first_then_the_view_order()
    {
        var pilot = new GoalAwarePilot(NeedleByThree);

        Assert.Equal("a:Pyroblast", pilot.Respond(View(null, Dissolve, Counterspell, Pyroblast), CounterspellOn(Needle)));
        Assert.Equal("a:Negate", pilot.Respond(View(null, Dissolve, Negate, Counterspell), CounterspellOn(Needle)));
        Assert.Equal("a:Counterspell", pilot.Respond(View(null, Counterspell, Negate), CounterspellOn(Needle)));
    }

    /// <summary>Each grant family against each disruption card:
    /// "can't be countered" only against Counterspell; hexproof,
    /// shroud, indestructible, phase-out and protection or hexproof from
    /// black or green against Atomize; only team indestructible or team
    /// phase-out against Rout.</summary>
    [Theory]
    [InlineData("cant_be_countered", "spells", true, false, false)]
    [InlineData("hexproof", "target_creature", false, true, false)]
    [InlineData("hexproof", "permanents", false, true, false)]
    [InlineData("shroud", "target_creature", false, true, false)]
    [InlineData("indestructible", "target_permanent", false, true, false)]
    [InlineData("indestructible", "creatures", false, true, true)]
    [InlineData("indestructible", "permanents", false, true, true)]
    [InlineData("phase_out", "target_creature", false, true, false)]
    [InlineData("phase_out", "permanents", false, true, true)]
    [InlineData("protection_from_black", "target_creature", false, true, false)]
    [InlineData("protection_from_green", "target_creature", false, true, false)]
    [InlineData("protection_from_red", "target_creature", false, false, false)]
    [InlineData("protection_from_blue", "target_creature", false, false, false)]
    [InlineData("protection_from_white", "creatures", false, false, false)]
    [InlineData("protection_from_everything", "target_creature", false, true, false)]
    [InlineData("protection_from_all_colors", "target_creature", false, true, false)]
    [InlineData("hexproof_from_black", "permanents", false, true, false)]
    [InlineData("hexproof_from_green", "permanents", false, true, false)]
    [InlineData("hexproof_from_blue", "permanents", false, false, false)]
    public void Each_grant_family_answers_its_disruption_cards(string grant, string scope, bool counterspell, bool atomize, bool rout)
    {
        var protection = new ProtectionGrant(grant, scope);

        Assert.Equal(
            (counterspell, atomize, rout),
            (protection.Answers(DisruptionCards.Counterspell), protection.Answers(DisruptionCards.Atomize), protection.Answers(DisruptionCards.Rout)));
        Assert.Equal(counterspell || atomize || rout, DisruptionCards.AnswersAny(new ResponseFeature(null, [protection])));
    }

    /// <summary>A response card answers a disruption card: Veil of Summer
    /// does by its "can't be countered" and its hexproof from black; a face
    /// whose only grant is hexproof from blue or protection from red does
    /// not.</summary>
    [Fact]
    public void A_response_card_needs_a_grant_that_answers_a_card_of_the_table()
    {
        Assert.True(DisruptionCards.AnswersAny(GrantProfiles["Veil of Summer"].Response!));
        Assert.False(DisruptionCards.AnswersAny(GrantProfiles["Test Protection From Red"].Response!));
        Assert.False(DisruptionCards.AnswersAny(new ResponseFeature(null, [new ProtectionGrant(ProtectionGrant.HexproofFrom("U"), ProtectionGrant.Permanents)])));
    }

    private static readonly GoalPredicate BearsByThree = new GoalPredicate.OnBattlefield("Grizzly Bears", 3);

    private static PilotView BearsOnTheBattlefield(params DeckCard[] castable) => View([new ViewPermanent("p:1", Bear, false)], castable);

    /// <summary>A "can't be countered" grant answers Counterspell on a goal
    /// spell; a permanent grant does not.</summary>
    [Fact]
    public void Against_counterspell_a_cant_be_countered_grant_qualifies()
    {
        var pilot = new GoalAwarePilot(NeedleByThree);

        Assert.Equal("a:Veil of Summer", pilot.Respond(View(null, SnakeskinVeil, HeroicIntervention, VeilOfSummer), CounterspellOn(Needle)));
        Assert.Null(pilot.Respond(View(null, SnakeskinVeil, HeroicIntervention, TeferisProtection), CounterspellOn(Needle)));
    }

    /// <summary>The scope check: a grant to target creatures covers the
    /// Bears and not the Needle, an artifact; a grant to the permanents we
    /// control covers both.</summary>
    [Fact]
    public void Against_atomize_a_grant_qualifies_when_its_scope_covers_the_victim()
    {
        Assert.Equal("a:Snakeskin Veil", new GoalAwarePilot(BearsByThree).Respond(BearsOnTheBattlefield(HeroicIntervention, SnakeskinVeil), AtomizeOn(Bear)));
        Assert.Null(new GoalAwarePilot(NeedleByThree).Respond(NeedleOnTheBattlefield(SnakeskinVeil, RedWard), AtomizeOn(Needle)));
        Assert.Equal("a:Heroic Intervention", new GoalAwarePilot(NeedleByThree).Respond(NeedleOnTheBattlefield(SnakeskinVeil, HeroicIntervention), AtomizeOn(Needle)));
        // Hexproof from black covers the Needle: Atomize is black and green.
        Assert.Equal("a:Veil of Summer", new GoalAwarePilot(NeedleByThree).Respond(NeedleOnTheBattlefield(HeroicIntervention, VeilOfSummer), AtomizeOn(Needle)));
        // Protection from red does not stop a black and green card.
        Assert.Null(new GoalAwarePilot(BearsByThree).Respond(BearsOnTheBattlefield(RedWard), AtomizeOn(Bear)));
    }

    /// <summary>The phase-out turn rule: a phased-out permanent is still
    /// gone at the end of this turn, so phase-out qualifies only when every
    /// threatened leaf is due after this turn (the view is turn 3).</summary>
    [Fact]
    public void Phase_out_qualifies_only_when_every_threatened_leaf_is_due_after_this_turn()
    {
        Assert.Null(new GoalAwarePilot(NeedleByThree).Respond(NeedleOnTheBattlefield(TeferisProtection), AtomizeOn(Needle)));
        Assert.Equal("a:Teferi's Protection", new GoalAwarePilot(new GoalPredicate.OnBattlefield("Pithing Needle", 4)).Respond(NeedleOnTheBattlefield(TeferisProtection), AtomizeOn(Needle)));

        // Two leaves on the Needle: one due this turn keeps phase-out out.
        var both = new GoalPredicate.AllOf([NeedleByThree, new GoalPredicate.OnBattlefield("Pithing Needle", 5)]);
        Assert.Null(new GoalAwarePilot(both).Respond(NeedleOnTheBattlefield(TeferisProtection), AtomizeOn(Needle)));
        Assert.Equal("a:Heroic Intervention", new GoalAwarePilot(both).Respond(NeedleOnTheBattlefield(TeferisProtection, HeroicIntervention), AtomizeOn(Needle)));
    }

    /// <summary>A grant card the goal wants is kept; the cheapest grant goes first.</summary>
    [Fact]
    public void The_pilot_skips_a_grant_the_goal_wants_and_takes_the_cheapest()
    {
        var goal = new GoalPredicate.AllOf([BearsByThree, new GoalPredicate.Cast("Snakeskin Veil", 3)]);
        Assert.Equal("a:Heroic Intervention", new GoalAwarePilot(goal).Respond(BearsOnTheBattlefield(SnakeskinVeil, HeroicIntervention), AtomizeOn(Bear)));
        Assert.Equal("a:Snakeskin Veil", new GoalAwarePilot(BearsByThree).Respond(BearsOnTheBattlefield(TeferisProtection, HeroicIntervention, SnakeskinVeil), AtomizeOn(Bear)));
    }

    /// <summary>The response rule reads only the view and the choice: the same inputs give the same answer.</summary>
    [Fact]
    public void The_response_rule_is_stateless()
    {
        var pilot = new GoalAwarePilot(NeedleByThree);
        var view = View(null, Negate, Counterspell);

        Assert.Equal(pilot.Respond(view, CounterspellOn(Needle)), pilot.Respond(view, CounterspellOn(Needle)));
        Assert.Null(pilot.Respond(view, CounterspellOn(Bear)));
        Assert.Equal("a:Negate", pilot.Respond(view, CounterspellOn(Needle)));
    }
}
