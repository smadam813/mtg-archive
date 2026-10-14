using MtgArchive.Data;
using MtgArchive.Ingest;

namespace MtgArchive.Tests;

/// <summary>
/// Source-profile derivation over committed Oracle-text samples
/// in <c>fixtures/tier1/source-profiles/oracle-cards.jsonl</c>: the closed
/// fetch template family, unmatched search-lands, and produced colors from
/// <c>produced_mana</c> with no card-type filter; the enters-tapped state
/// from the land faces' closed template family; and the fetch
/// activation from the closed tap-and-sacrifice template.
/// </summary>
public sealed class SourceProfileDerivationTests
{
    private static readonly Dictionary<string, SnapshotCard> Cards = Load();

    private static Dictionary<string, SnapshotCard> Load()
    {
        var errors = new List<string>();
        var cards = ScryfallSnapshotReader.ReadCards(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "tier1", "source-profiles", "oracle-cards.jsonl"),
            errors);
        Assert.Empty(errors);
        return cards.ToDictionary(c => c.Name);
    }

    private static SourceProfileDerivation.Result Derive(string name)
    {
        var card = Cards[name];
        return SourceProfileDerivation.Derive(card.ProducedMana, SourceProfileDerivation.CombinedText(
            new[] { card.OracleText }.Concat(card.Faces.Select(face => face.OracleText))));
    }

    [Fact]
    public void Classic_fetch_yields_exactly_its_land_types()
    {
        var heath = Derive("Windswept Heath");
        Assert.Null(heath.ProducedColors);
        Assert.False(heath.UnmatchedSearchLand);
        Assert.Equal(["Forest", "Plains"], heath.Fetch!.LandTypes);
        Assert.False(heath.Fetch.BasicOnly);
    }

    [Theory]
    [InlineData("Evolving Wilds")]
    [InlineData("Cultivate")] // "up to two", trailing "reveal those cards" clause
    public void Basic_land_fetch_stores_the_basic_constraint_with_no_types(string name)
    {
        var result = Derive(name);
        Assert.False(result.UnmatchedSearchLand);
        Assert.Empty(result.Fetch!.LandTypes);
        Assert.True(result.Fetch.BasicOnly);
    }

    [Fact]
    public void And_joined_targets_union_their_types()
        => Assert.Equal(["Forest", "Plains"], Derive("Krosan Verge").Fetch!.LandTypes);

    [Fact]
    public void Basic_typed_list_keeps_types_and_the_basic_constraint()
    {
        var panorama = Derive("Bant Panorama");
        Assert.Equal(["Forest", "Plains", "Island"], panorama.Fetch!.LandTypes);
        Assert.True(panorama.Fetch.BasicOnly);
    }

    [Theory]
    [InlineData("Scapeshift")] // "up to that many" is outside the family
    [InlineData("Into the North")] // a snow constraint is not a land type
    [InlineData("Circuitous Route")] // "and/or" is outside the family
    public void Non_template_search_land_is_unmatched_and_gets_no_fetch(string name)
    {
        var result = Derive(name);
        Assert.True(result.UnmatchedSearchLand);
        Assert.Null(result.Fetch);
    }

    [Theory]
    [InlineData("Sol Ring", new[] { "C" })] // mana rock — no card-type filter
    [InlineData("Llanowar Elves", new[] { "G" })] // mana dork
    public void Non_empty_produced_mana_yields_produced_colors(string name, string[] expected)
        => Assert.Equal(expected, Derive(name).ProducedColors);

    [Fact]
    public void A_card_can_carry_both_facets()
    {
        var verge = Derive("Krosan Verge");
        Assert.Equal(["C"], verge.ProducedColors);
        Assert.NotNull(verge.Fetch);
    }

    [Fact]
    public void Generic_land_fetch_stores_no_types_and_no_basic_constraint()
    {
        var map = Derive("Expedition Map");
        Assert.Empty(map.Fetch!.LandTypes);
        Assert.False(map.Fetch.BasicOnly);
    }

    [Fact]
    public void Non_land_tutors_get_no_fetch_and_no_report_entry()
    {
        var tutor = Derive("Demonic Tutor");
        Assert.Null(tutor.Fetch);
        Assert.False(tutor.UnmatchedSearchLand);
    }

    [Fact]
    public void Cards_with_neither_facet_get_nothing()
    {
        var bears = Derive("Grizzly Bears");
        Assert.Null(bears.ProducedColors);
        Assert.Null(bears.Fetch);
        Assert.False(bears.UnmatchedSearchLand);
    }

    // The producer's text binds its five colors to the
    // commander's identity or to lands the players control.
    [Theory]
    [InlineData("Command Tower")]
    [InlineData("Arcane Signet")]
    [InlineData("Commander's Sphere")]
    [InlineData("Path of Ancestry")]
    [InlineData("Opal Palace")]
    [InlineData("Exotic Orchard")]
    [InlineData("Fellwar Stone")]
    [InlineData("Reflecting Pool")]
    [InlineData("Star Compass")]
    public void Identity_bound_producers_are_flagged(string name)
        => Assert.True(Derive(name).IdentityBound);

    [Theory]
    [InlineData("Mana Confluence")] // any color, unbound
    [InlineData("Plaza of Heroes")] // one unbound any-color ability
    [InlineData("Sol Ring")]
    [InlineData("Gond Gate")] // no produced_mana in the fixture
    public void Other_producers_are_not_identity_bound(string name)
        => Assert.False(Derive(name).IdentityBound);

    [Fact]
    public void One_unbound_any_one_color_ability_leaves_the_card_unbound()
        => Assert.False(SourceProfileDerivation.Derive(
            ["W", "U", "B", "R", "G"],
            "{T}: Add one mana of any color in your commander's color identity.\n"
            + "{2}, {T}: Add three mana of any one color.").IdentityBound);

    [Fact]
    public void Identity_bound_text_needs_the_full_five_color_produced_set()
        => Assert.False(SourceProfileDerivation.Derive(
            ["W", "U"], "{T}: Add one mana of any color in your commander's color identity.").IdentityBound);

    // Every mana-producing ability depends on another player or
    // on combat, through the closed template family.
    [Theory]
    [InlineData("Smothering Tithe")] // a trigger on an opponent's action
    [InlineData("Revel in Riches")] // a trigger on a creature an opponent controls
    [InlineData("Dockside Extortionist")] // X counts what opponents control
    [InlineData("Carpet of Flowers")] // X counts what target opponent controls
    [InlineData("Waste Not")]
    [InlineData("Hoard Robber")] // a trigger on combat damage
    [InlineData("Bejeweled Warg")] // the Treasure is a mode of the combat trigger
    public void Opponent_dependent_producers_are_flagged(string name)
    {
        var result = Derive(name);
        Assert.True(result.OpponentDependent);
        Assert.False(result.UnmatchedOpponentSource);
    }

    [Theory]
    [InlineData("Coveted Jewel")] // its {T} ability does not depend on opponents
    [InlineData("Sol Ring")]
    [InlineData("Birds of Paradise")]
    [InlineData("Dark Ritual")]
    [InlineData("Lotus Petal")]
    [InlineData("Lion's Eye Diamond")]
    [InlineData("Simian Spirit Guide")]
    [InlineData("Fellwar Stone")] // an identity-bound clause, not an opponent dependency
    [InlineData("Exotic Orchard")]
    public void Other_producers_are_not_opponent_dependent(string name)
    {
        var result = Derive(name);
        Assert.False(result.OpponentDependent);
        Assert.False(result.UnmatchedOpponentSource);
    }

    [Fact]
    public void A_producer_that_mentions_an_opponent_outside_the_family_is_unmatched()
    {
        var lavinia = Derive("Lavinia, Foil to Conspiracy");

        Assert.False(lavinia.OpponentDependent);
        Assert.True(lavinia.UnmatchedOpponentSource);
    }

    [Fact]
    public void A_producer_that_mentions_combat_outside_the_family_is_unmatched()
    {
        var result = SourceProfileDerivation.Derive(
            ["R"], "Whenever this creature attacks, add {R}. Until end of combat, you don't lose this mana as steps end.");

        Assert.False(result.OpponentDependent);
        Assert.True(result.UnmatchedOpponentSource);
    }

    [Fact]
    public void A_producer_that_triggers_on_an_attack_is_unmatched()
    {
        var result = SourceProfileDerivation.Derive(
            ["B", "G", "R", "U", "W"], "Whenever this creature attacks, create a Treasure token.");

        Assert.False(result.OpponentDependent);
        Assert.True(result.UnmatchedOpponentSource);
    }

    // Each mode is its own ability: an unconditional mana mode keeps the card a source.
    [Fact]
    public void A_modal_producer_with_an_independent_mana_mode_is_not_opponent_dependent()
    {
        var result = SourceProfileDerivation.Derive(
            ["R"], "Choose one —\n• Add {R}{R}{R}.\n• Add {R} for each card in target opponent's hand.");

        Assert.False(result.OpponentDependent);
        Assert.False(result.UnmatchedOpponentSource);
    }

    [Fact]
    public void An_unmatched_ability_beside_an_independent_one_is_still_reported()
    {
        var result = SourceProfileDerivation.Derive(
            ["C"], "{T}: Add {C}.\n{T}: Add {C}{C}. Activate only during an opponent's turn.");

        Assert.False(result.OpponentDependent);
        Assert.True(result.UnmatchedOpponentSource);
    }

    [Fact]
    public void A_card_with_no_produced_mana_is_never_opponent_dependent()
    {
        var result = SourceProfileDerivation.Derive(
            null, "Whenever an opponent draws a card, you create a Treasure token.");

        Assert.False(result.OpponentDependent);
        Assert.False(result.UnmatchedOpponentSource);
    }

    [Theory]
    [InlineData("Add {R} for each artifact your opponents control.")]
    [InlineData("Add {R} for each card in target opponent's hand.")]
    [InlineData("For each artifact or creature card in target opponent's graveyard, add {C} and you gain 1 life.")]
    [InlineData("At the beginning of your first main phase, add {C} for each land target opponent controls.")]
    [InlineData("At the beginning of each of your postcombat main phases, add {R} for each 1 life your opponents have lost this turn.")]
    [InlineData("Enchanted creature has \"Whenever this creature deals combat damage to a player, create a Treasure token.\"")]
    [InlineData("Whenever one or more creatures you control deal combat damage to a player, create a Treasure token.")]
    public void Constructed_opponent_dependent_shapes_are_flagged(string text)
        => Assert.True(SourceProfileDerivation.Derive(["R"], text).OpponentDependent);

    // Fetch abilities join the facet, and a condition that an
    // opponent controls more than you joins the family.
    [Theory]
    [InlineData("Land Tax")]
    [InlineData("Knight of the White Orchid")]
    [InlineData("Weathered Wayfarer")] // the condition is a later sentence of the ability
    [InlineData("Gift of Estates")]
    [InlineData("Keeper of the Accord")] // "that player controls more"
    [InlineData("Aerial Surveyor")] // "defending player controls more"
    [InlineData("Sunstar Expansionist")] // the Lander's fetch ability is reminder text of the ability
    [InlineData("Oreskos Explorer")] // X counts the players who control more lands than you
    [InlineData("Surveyor's Scope")] // "control at least two more lands than you"
    [InlineData("Ticket Tortoise")]
    [InlineData("Discerning Financier")]
    [InlineData("Dora Milaje Elite")] // the Vibranium's mana ability is reminder text
    public void Opponent_dependent_fetches_and_more_than_you_sources_are_flagged(string name)
    {
        var result = Derive(name);
        Assert.True(result.OpponentDependent);
        Assert.False(result.UnmatchedOpponentSource);
    }

    [Theory]
    [InlineData("Sword of the Animist")]
    [InlineData("Primeval Titan")]
    public void A_fetch_that_triggers_on_an_attack_is_unmatched(string name)
    {
        var result = Derive(name);
        Assert.NotNull(result.Fetch);
        Assert.False(result.OpponentDependent);
        Assert.True(result.UnmatchedOpponentSource);
    }

    [Theory]
    [InlineData("Rampant Growth")]
    [InlineData("Wood Elves")]
    [InlineData("Ash Barrens")] // landcycling reminder text
    public void Other_fetches_are_not_opponent_dependent(string name)
    {
        var result = Derive(name);
        Assert.NotNull(result.Fetch);
        Assert.False(result.OpponentDependent);
        Assert.False(result.UnmatchedOpponentSource);
    }

    [Theory]
    [InlineData( // a dependent producer and an independent fetch
        "Whenever an opponent draws a card, add {G}.\n"
        + "When this creature enters, search your library for a basic land card, put it onto the battlefield tapped, then shuffle.")]
    [InlineData( // an independent producer and a dependent fetch
        "{T}: Add {G}.\n"
        + "When this creature enters, if an opponent controls more lands than you, search your library for a basic land card, put it onto the battlefield tapped, then shuffle.")]
    public void One_independent_ability_of_either_kind_keeps_the_card_a_source(string text)
    {
        var result = SourceProfileDerivation.Derive(["G"], text);
        Assert.NotNull(result.Fetch);
        Assert.False(result.OpponentDependent);
        Assert.False(result.UnmatchedOpponentSource);
    }

    // A card whose every source ability gives its token to another
    // player is no source of its own. A separate facet records this.
    [Theory]
    [InlineData("An Offer You Can't Refuse")] // "Its controller creates" after a counter
    [InlineData("Buy Your Silence")] // "Its controller creates" after a removal
    [InlineData("Wanted Scoundrels")] // "target opponent creates"
    [InlineData("Vraska, the Cutting Glare")] // "They create" after an opponent is named
    [InlineData("Bilbo's Gambit")] // Gift a Treasure, no other source
    [InlineData("Blooming Blast")] // Gift a Treasure, no other source
    public void A_card_that_gives_its_only_token_away_is_flagged(string name)
    {
        var result = Derive(name);
        Assert.True(result.TokenToAnotherPlayer);
        Assert.False(result.OpponentDependent);
        Assert.False(result.UnmatchedOpponentSource);
    }

    [Theory]
    [InlineData("Deadly Dispute")] // "create a Treasure token": you get it
    [InlineData("Marching Duodrone")] // "each player creates": that includes you
    [InlineData("Smothering Tithe")]
    public void A_token_you_get_does_not_set_the_facet(string name)
        => Assert.False(Derive(name).TokenToAnotherPlayer);

    [Fact]
    public void Deadly_Dispute_is_a_matched_source()
    {
        var result = Derive("Deadly Dispute");
        Assert.False(result.OpponentDependent);
        Assert.False(result.UnmatchedOpponentSource);
    }

    [Fact]
    public void Smothering_Tithe_stays_opponent_dependent_without_the_facet()
    {
        var result = Derive("Smothering Tithe");
        Assert.True(result.OpponentDependent);
        Assert.False(result.TokenToAnotherPlayer);
    }

    [Fact]
    public void A_gift_beside_another_source_ability_is_ignored()
    {
        var result = SourceProfileDerivation.Derive(
            ["B", "G", "R", "U", "W"],
            "Gift a Treasure (You may promise an opponent a gift as you cast this spell. If you do, they create a Treasure token before its other effects. It's an artifact with \"{T}, Sacrifice this token: Add one mana of any color.\")\n"
            + "Add {R}{R}{R}.");
        Assert.False(result.OpponentDependent);
        Assert.False(result.TokenToAnotherPlayer);
        Assert.False(result.UnmatchedOpponentSource);
    }

    // A gift gives a token away, so a gift beside a dependent ability is a mix.
    [Fact]
    public void A_gift_beside_a_dependent_ability_is_unmatched()
    {
        var result = SourceProfileDerivation.Derive(
            ["B", "G", "R", "U", "W"],
            "Gift a Treasure (You may promise an opponent a gift as you cast this spell. If you do, they create a Treasure token.)\nWhenever an opponent casts a spell, add {R}.");
        Assert.False(result.TokenToAnotherPlayer);
        Assert.False(result.OpponentDependent);
        Assert.True(result.UnmatchedOpponentSource);
    }

    [Fact]
    public void They_after_an_opponent_in_the_same_sentence_gives_the_token_away()
    {
        var result = SourceProfileDerivation.Derive(
            ["B", "G", "R", "U", "W"], "When an opponent dies, they create a Treasure token.");
        Assert.True(result.TokenToAnotherPlayer);
    }

    // A sentence that also makes a token for you keeps the card a source.
    [Fact]
    public void A_sentence_that_also_creates_your_own_token_keeps_the_card_a_source()
    {
        var result = SourceProfileDerivation.Derive(
            ["B", "G", "R", "U", "W"], "Each opponent creates a Treasure token and you create a Treasure token.");
        Assert.False(result.TokenToAnotherPlayer);
    }

    [Fact]
    public void They_before_the_opponent_in_the_same_sentence_is_not_given_away()
    {
        var result = SourceProfileDerivation.Derive(
            ["B", "G", "R", "U", "W"], "They create a Treasure token for each opponent you have.");
        Assert.False(result.TokenToAnotherPlayer);
    }

    [Theory]
    [InlineData("Counter target spell. Its controller then creates a Treasure token.")]
    [InlineData("Exile target permanent. Its controller may create a Treasure token.")]
    public void A_modal_word_before_the_verb_still_gives_the_token_away(string text)
        => Assert.True(SourceProfileDerivation.Derive(["B", "G", "R", "U", "W"], text).TokenToAnotherPlayer);

    [Fact]
    public void One_own_ability_beside_a_given_away_token_keeps_the_card_a_source()
    {
        var result = SourceProfileDerivation.Derive(
            ["R"], "Add {R}. Its controller creates a Treasure token.");
        Assert.False(result.TokenToAnotherPlayer);
        Assert.False(result.OpponentDependent);
        Assert.False(result.UnmatchedOpponentSource);
    }

    // A mix of dependent and given-away abilities, with no independent one, is unmatched.
    [Fact]
    public void Dependent_and_given_away_abilities_without_an_independent_one_are_unmatched()
    {
        var result = SourceProfileDerivation.Derive(
            ["B", "G", "R", "U", "W"],
            "Whenever an opponent casts a spell, add {R}.\nWhen this creature dies, target opponent creates a Treasure token.");
        Assert.False(result.TokenToAnotherPlayer);
        Assert.False(result.OpponentDependent);
        Assert.True(result.UnmatchedOpponentSource);
    }

    // A token subject outside both lists gives the same result as before #337.
    [Fact]
    public void An_unlisted_token_subject_keeps_the_pre_337_result()
    {
        var result = SourceProfileDerivation.Derive(
            ["B", "G", "R", "U", "W"], "Target player creates a Treasure token.");
        Assert.False(result.TokenToAnotherPlayer);
        Assert.False(result.OpponentDependent);
        Assert.False(result.UnmatchedOpponentSource);
    }

    // Firebending makes mana only through its reminder text: an attack trigger.
    [Theory]
    [InlineData("Firebending 1 (Whenever this creature attacks, add {R}. This mana lasts until end of combat.)")]
    [InlineData("Whenever an opponent draws a card, add {R}.\nFirebending 1 (Whenever this creature attacks, add {R}. This mana lasts until end of combat.)")]
    public void A_firebending_producer_is_unmatched(string text)
    {
        var result = SourceProfileDerivation.Derive(["R"], text);
        Assert.False(result.OpponentDependent);
        Assert.True(result.UnmatchedOpponentSource);
    }

    // Reminder text that explains a keyword is not part of the mana ability.
    [Fact]
    public void A_keyword_reminder_beside_a_mana_ability_is_ignored()
    {
        var result = SourceProfileDerivation.Derive(
            ["R"], "Gain control of target creature until end of turn. It gains haste until end of turn. Add {R}{R}. (The creature can attack and {T} this turn.)");
        Assert.False(result.OpponentDependent);
        Assert.False(result.UnmatchedOpponentSource);
    }

    [Fact]
    public void A_dependent_fetch_with_no_produced_mana_is_opponent_dependent()
        => Assert.True(DeriveText(
            "Whenever an opponent casts a spell, you may search your library for a basic land card, put it onto the battlefield tapped, then shuffle.").OpponentDependent);

    // Constructed texts, for shapes no committed fixture card carries.
    private static SourceProfileDerivation.Result DeriveText(string oracleText)
        => SourceProfileDerivation.Derive(null, oracleText);

    [Fact]
    public void Separate_fetch_clauses_union_into_one_profile()
    {
        var result = DeriveText(
            "{T}: Search your library for a Forest card, put it onto the battlefield, then shuffle.\n"
            + "{2}, {T}: Search your library for an Island card, put it onto the battlefield, then shuffle.");
        Assert.Equal(["Forest", "Island"], result.Fetch!.LandTypes);
        Assert.False(result.UnmatchedSearchLand);
    }

    [Fact]
    public void Clauses_with_differing_basic_constraints_are_unmatched()
    {
        var result = DeriveText(
            "Basic landcycling {1} ({1}, Discard this card: Search your library for a basic land card, "
            + "reveal it, put it into your hand, then shuffle.)\n"
            + "{T}, Sacrifice this land: Search your library for a Gate card, "
            + "put it onto the battlefield, then shuffle.");
        Assert.Null(result.Fetch);
        Assert.True(result.UnmatchedSearchLand);
    }

    [Fact]
    public void Land_condition_in_a_later_sentence_is_still_a_search_land()
    {
        var result = DeriveText(
            "Search your library for a card and reveal it. If it's a land card, "
            + "put it onto the battlefield tapped. Then shuffle.");
        Assert.Null(result.Fetch);
        Assert.True(result.UnmatchedSearchLand);
    }

    [Fact]
    public void Plural_land_type_is_still_a_search_land()
    {
        var result = DeriveText(
            "Search your library for any number of cards that share a land type with Islands you control, "
            + "put them onto the battlefield tapped, then shuffle.");
        Assert.Null(result.Fetch);
        Assert.True(result.UnmatchedSearchLand);
    }

    [Fact]
    public void Landcycling_reminder_text_is_a_fetch_never_silently_dropped()
    {
        var barrens = Derive("Ash Barrens");
        Assert.Equal(["C"], barrens.ProducedColors);
        Assert.Empty(barrens.Fetch!.LandTypes);
        Assert.True(barrens.Fetch.BasicOnly);
        Assert.False(barrens.UnmatchedSearchLand);
    }

    [Theory]
    [InlineData("Windswept Heath", FetchActivation.Untapped)] // "Pay 1 life" is in the template
    [InlineData("Evolving Wilds", FetchActivation.Tapped)]
    public void A_tap_and_sacrifice_fetch_records_the_found_lands_state(string name, FetchActivation expected)
        => Assert.Equal(expected, Derive(name).Fetch!.Activation);

    [Theory]
    [InlineData("Bant Panorama")] // a mana cost
    [InlineData("Krosan Verge")] // a mana cost, two lands
    [InlineData("Ash Barrens")] // landcycling, to hand
    [InlineData("Expedition Map")] // an artifact, to hand
    [InlineData("Cultivate")] // a spell
    public void Any_other_fetch_shape_keeps_its_types_and_gets_no_activation(string name)
    {
        var result = Derive(name);
        Assert.NotNull(result.Fetch);
        Assert.Null(result.Fetch.Activation);
    }

    [Fact]
    public void A_fetch_trigger_gets_no_activation()
    {
        var result = DeriveText(
            "When this land is put into a graveyard from the battlefield, you may search your library "
            + "for a Plains card, put it onto the battlefield tapped, then shuffle.");
        Assert.Equal(["Plains"], result.Fetch!.LandTypes);
        Assert.Null(result.Fetch.Activation);
    }

    [Fact]
    public void A_sentence_after_the_shuffle_keeps_the_found_lands_state()
    {
        var result = DeriveText(
            "{T}, Sacrifice this land: Search your library for a basic land card, put it onto the battlefield tapped, "
            + "then shuffle. Then if you control four or more lands, untap that land.");
        Assert.Equal(FetchActivation.Tapped, result.Fetch!.Activation);
    }

    private static SourceProfileDerivation.EntersTappedResult EntersTapped(string name)
    {
        var card = Cards[name];
        return SourceProfileDerivation.EntersTapped(SourceProfileDerivation.FacesOf(
            card.Name,
            card.TypeLine,
            card.OracleText,
            [.. card.Faces.Select(face => new SourceProfileDerivation.Face(face.Name, face.TypeLine, face.OracleText))]));
    }

    [Theory]
    [InlineData("Plain Tapped Land", EntersTappedState.Tapped)]
    [InlineData("Alchemy Tapped Land", EntersTappedState.Tapped)] // "enters the battlefield tapped"
    [InlineData("Counter Land", EntersTappedState.Tapped)] // "enters tapped with N counters"
    [InlineData("Unless Land", EntersTappedState.Conditional)]
    [InlineData("If Land", EntersTappedState.Conditional)] // trailing "if", leading clause
    [InlineData("Leading If Land", EntersTappedState.Conditional)] // "If …, this land enters tapped."
    [InlineData("Shock Land", EntersTappedState.Untapped)] // pay life: a player choice
    [InlineData("Variable Shock Land", EntersTappedState.Untapped)] // "pay X life" is the same choice
    [InlineData("Reveal Land", EntersTappedState.Conditional)] // reveal from hand: gated on state
    [InlineData("Copy Land", EntersTappedState.Untapped)] // "You may have this land enter tapped"
    [InlineData("Rainbow Land", EntersTappedState.Untapped)] // no tapped text at all
    [InlineData("Short Name, the Long Title", EntersTappedState.Tapped)] // the name's short form as subject
    [InlineData("A-Rebalanced Land", EntersTappedState.Tapped)] // the Alchemy prefix is not in the text
    [InlineData("A-Real Prefix Land", EntersTappedState.Tapped)] // a real "A-" name, used in full
    [InlineData("Quoted Boon Land", EntersTappedState.Tapped)] // granted-ability text in quotes is not self
    [InlineData("Reminder Land", EntersTappedState.Tapped)] // reminder text is not rules text
    [InlineData("Keyword Line Land", EntersTappedState.Tapped)] // a keyword line has no period
    [InlineData("Untapped Trigger Land", EntersTappedState.Conditional)] // "enters untapped" is not "tapped"
    [InlineData("Gond Gate", EntersTappedState.Untapped)] // "Gates you control enter untapped": no self clause
    public void Every_template_line_derives_its_state(string name, EntersTappedState expected)
    {
        var result = EntersTapped(name);
        Assert.Equal(expected, result.State);
        Assert.False(result.Unmatched);
    }

    [Theory]
    [InlineData("The Belligerent and Useless Island")] // plural "enter" is outside the family
    [InlineData("Tapped Side // Untapped Side")] // two land faces disagree
    [InlineData("Orphan Choice Land")] // "If you don't" with no choice before it
    public void Off_template_text_is_unmatched_and_gets_no_state(string name)
    {
        var result = EntersTapped(name);
        Assert.Null(result.State);
        Assert.True(result.Unmatched);
    }

    [Fact]
    public void A_land_face_only_modal_card_reads_its_land_face()
    {
        var result = EntersTapped("Awakening Spell // Awakening Land");
        Assert.Equal(EntersTappedState.Untapped, result.State);
        Assert.False(result.Unmatched);
    }

    [Fact]
    public void A_spell_face_with_tapped_text_does_not_leak_into_the_land_face()
    {
        var result = EntersTapped("Spell Face Leak // Plain Back Land");
        Assert.Equal(EntersTappedState.Untapped, result.State);
        Assert.False(result.Unmatched);
    }

    [Fact]
    public void A_card_with_no_land_face_has_no_state()
    {
        var result = EntersTapped("Grizzly Bears");
        Assert.Null(result.State);
        Assert.False(result.Unmatched);
        Assert.False(result.Land);
    }

    [Theory]
    [InlineData("Rainbow Land")]
    [InlineData("Awakening Spell // Awakening Land")]
    [InlineData("The Belligerent and Useless Island")]
    public void Any_land_face_marks_the_card_a_land(string name)
        => Assert.True(EntersTapped(name).Land);

    [Theory]
    [InlineData("{T}: Add {W}.", "W")]
    [InlineData("{T}: Add {B}.", "B")]
    [InlineData("As Shatterskull, the Hammer Pass enters, you may pay 3 life. If you don't, it enters tapped.\n{T}: Add {R}.", "R")]
    [InlineData("{T}: Add {W} or {B}. Spend {G} only on lands.", "W,B")]
    public void A_land_face_makes_the_union_colors_its_own_add_text_names(string faceText, string expected)
        => Assert.Equal(expected.Split(','), SourceProfileDerivation.FaceColors(["W", "B", "R"], faceText));

    [Theory]
    [InlineData("{T}: Add one mana of any color.")]
    [InlineData("{T}: Add {G}.")]
    [InlineData(null)]
    public void A_land_face_that_names_no_union_color_has_no_face_colors(string? faceText)
        => Assert.Null(SourceProfileDerivation.FaceColors(["W", "B"], faceText));
}
