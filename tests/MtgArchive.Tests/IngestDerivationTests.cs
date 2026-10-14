using MtgArchive.Ingest;

namespace MtgArchive.Tests;

/// <summary>
/// Derivation rules over fixture card records — real Scryfall
/// Oracle Cards records, slimmed, in
/// <c>fixtures/ingest/oracle-cards.jsonl</c>. Notable data facts baked into
/// the fixtures: Partner With cards carry both "Partner with" and "Partner"
/// keywords; the named Partner—variants (Friends forever, …) carry the
/// plain "Partner" keyword and only the Oracle text separates them; and
/// Halana and Alena's current Oracle text carries no partner mechanic at
/// all. The meld pair records in <c>fixtures/ingest/meld-cards.jsonl</c>
/// are kept apart so other suites that ingest the main file keep their
/// counts.
/// </summary>
public sealed class IngestDerivationTests
{
    private static readonly Dictionary<string, SnapshotCard> Cards = Load();

    private static Dictionary<string, SnapshotCard> Load()
    {
        var errors = new List<string>();
        var cards = new[] { "oracle-cards.jsonl", "meld-cards.jsonl" }
            .SelectMany(file => ScryfallSnapshotReader.ReadCards(
                Path.Combine(AppContext.BaseDirectory, "fixtures", "ingest", file), errors));
        var byName = cards.ToDictionary(c => c.Name);
        Assert.Empty(errors);
        return byName;
    }

    private static CardDerivations.Derived Derive(string name) => CardDerivations.Derive(Cards[name]);

    [Theory]
    [InlineData("Forest", null)] // Basic supertype
    [InlineData("Relentless Rats", null)] // "any number of cards named"
    [InlineData("Seven Dwarves", 7)]
    [InlineData("Nazgûl", 9)] // "up to nine cards named"
    [InlineData("Sol Ring", 1)]
    [InlineData("Thrasios, Triton Hero", 1)]
    public void Copy_limit_reads_the_deck_allowance_from_supertype_and_text(string name, int? expected)
        => Assert.Equal(expected, Derive(name).CopyLimit);

    [Theory]
    [InlineData("Thrasios, Triton Hero", true)] // legendary creature
    [InlineData("Teferi, Temporal Archmage", true)] // "can be your commander" text
    [InlineData("Wilson, Refined Grizzly", true)]
    [InlineData("Halana and Alena, Partners", true)]
    [InlineData("Nyssa of Traken", true)]
    [InlineData("Liliana, Heretical Healer // Liliana, Defiant Necromancer", true)] // front face
    [InlineData("Guild Artisan", false)] // legendary, but an enchantment
    [InlineData("Sol Ring", false)]
    [InlineData("Seven Dwarves", false)] // creature, not legendary
    [InlineData("Fire // Ice", false)]
    public void Can_be_commander_is_legendary_creature_or_the_commander_text(string name, bool expected)
        => Assert.Equal(expected, Derive(name).CanBeCommander);

    // A meld result is the combined back of a meld pair, an Oracle object of
    // its own that no one can put in a deck or choose (CR 712.4b). The meld
    // part that is a legendary creature stays eligible.
    [Theory]
    [InlineData("Brisela, Voice of Nightmares", false)]
    [InlineData("Bruna, the Fading Light", true)]
    public void Can_be_commander_excludes_a_meld_result(string name, bool expected)
        => Assert.Equal(expected, Derive(name).CanBeCommander);

    // The Brawl commander rule (ADR 0012) pinned against
    // Scryfall's own is:brawler search. The positive file is a sample of
    // is:brawler results of every kind (legendary creatures, multi-faced
    // cards, planeswalkers, Vehicles, Spacecraft, and the three "can be your
    // commander" planeswalkers). The negative file holds legendary
    // noncreatures, nonlegendary creatures and Vehicles, and multi-faced
    // cards with a legendary back face only, each legal in both brawl and
    // competitivebrawl, so is:brawler leaves them out for their shape alone.
    [Fact]
    public void Brawl_commander_rule_matches_an_is_brawler_sample()
    {
        var brawlers = ReadSample("is-brawler-sample.jsonl");
        var others = ReadSample("not-brawler-sample.jsonl");

        Assert.Equal(36, brawlers.Count);
        Assert.Equal(21, others.Count);
        Assert.Empty(brawlers.Where(c => !CardDerivations.Derive(c).CanBeBrawlCommander).Select(c => c.Name));
        Assert.Empty(others.Where(c => CardDerivations.Derive(c).CanBeBrawlCommander).Select(c => c.Name));
    }

    [Theory]
    [InlineData("Brisela, Voice of Nightmares", false)]
    [InlineData("Bruna, the Fading Light", true)]
    public void Can_be_brawl_commander_excludes_a_meld_result(string name, bool expected)
        => Assert.Equal(expected, Derive(name).CanBeBrawlCommander);

    private static List<SnapshotCard> ReadSample(string file)
    {
        var errors = new List<string>();
        var cards = ScryfallSnapshotReader.ReadCards(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "ingest", file), errors).ToList();
        Assert.Empty(errors);
        return cards;
    }

    // A malformed all_parts entry is not a meld result and must not abort the read.
    [Fact]
    public void A_non_object_all_parts_entry_is_not_a_meld_result()
    {
        var line = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "ingest", "meld-cards.jsonl"))
            .First()
            .Replace("\"all_parts\":[", "\"all_parts\":[null,\"x\",");
        Assert.True(IngestHarness.ReadOne(line).IsMeldResult);
    }

    [Theory]
    //          card                            partner partner_with friends_forever background doctors_companion
    [InlineData("Thrasios, Triton Hero", true, false, false, false, false)]
    [InlineData("Pir, Imaginative Rascal", false, true, false, false, false)]
    [InlineData("Sophina, Spearsage Deserter", false, false, true, false, false)]
    [InlineData("Nyssa of Traken", false, false, false, false, true)]
    [InlineData("Wilson, Refined Grizzly", false, false, false, true, false)]
    [InlineData("Guild Artisan", false, false, false, true, false)]
    [InlineData("Halana and Alena, Partners", false, false, false, false, false)]
    [InlineData("Sol Ring", false, false, false, false, false)]
    public void Partner_flags_separate_the_five_pairing_mechanics(
        string name, bool partner, bool partnerWith, bool friendsForever, bool background, bool doctorsCompanion)
    {
        var derived = Derive(name);
        Assert.Equal(partner, derived.HasPartner);
        Assert.Equal(partnerWith, derived.HasPartnerWith);
        Assert.Equal(friendsForever, derived.HasFriendsForever);
        Assert.Equal(background, derived.HasBackground);
        Assert.Equal(doctorsCompanion, derived.HasDoctorsCompanion);
    }

    [Theory]
    [InlineData("Fire // Ice", 2)] // front face {1}{R}, not the whole-card cmc 4
    [InlineData("Liliana, Heretical Healer // Liliana, Defiant Necromancer", 3)]
    [InlineData("Forest", 0)]
    [InlineData("Sol Ring", 1)]
    public void Mana_value_is_the_front_face_for_multi_faced_cards(string name, decimal expected)
        => Assert.Equal(expected, Derive(name).ManaValue);

    [Theory]
    [InlineData("{2}{R}{G}", 4)]
    [InlineData("{X}{X}{R}", 1)]
    [InlineData("{W/U}{W/U}", 2)]
    [InlineData("{2/W}", 2)]
    [InlineData("{G/P}", 1)]
    [InlineData("{HW}", 0.5)]
    [InlineData("", 0)]
    public void Mana_value_parses_every_symbol_family(string manaCost, decimal expected)
        => Assert.Equal(expected, CardDerivations.ManaValueOf(manaCost));

    [Fact]
    public void Type_line_parses_into_supertypes_card_types_and_subtypes()
    {
        var forest = Derive("Forest");
        Assert.Equal(["Basic"], forest.Supertypes);
        Assert.Equal(["Land"], forest.CardTypes);
        Assert.Equal(["Forest"], forest.Subtypes);

        var liliana = Derive("Liliana, Heretical Healer // Liliana, Defiant Necromancer");
        Assert.Equal(["Legendary"], liliana.Supertypes);
        Assert.Equal(["Creature", "Planeswalker"], liliana.CardTypes);
        Assert.Equal(["Human", "Cleric", "Liliana"], liliana.Subtypes);
    }

    [Fact]
    public void Type_line_keeps_multi_word_subtypes_whole()
    {
        Assert.Equal(["Time Lord", "Doctor"], Derive("The Fourth Doctor").Subtypes);

        var (_, _, planeSubtypes) = CardDerivations.ParseTypeLine("Plane — Serra's Realm");
        Assert.Equal(["Serra's Realm"], planeSubtypes);
    }

    [Fact]
    public void Reader_parses_faces_and_produced_mana()
    {
        Assert.Equal(["G"], Cards["Forest"].ProducedMana);

        var fireIce = Cards["Fire // Ice"];
        Assert.Equal(2, fireIce.Faces.Count);
        Assert.Equal("Fire", fireIce.Faces[0].Name);
        Assert.Equal("{1}{R}", fireIce.Faces[0].ManaCost);
        Assert.Equal("split", fireIce.Layout);
        Assert.Null(Cards["Liliana, Heretical Healer // Liliana, Defiant Necromancer"].Faces[1].ManaCost);
    }

    [Fact]
    public void Reader_drops_art_series_and_front_card_objects_but_keeps_tokens()
    {
        Assert.Equal("Token Artifact — Treasure", Cards["Treasure"].TypeLine); // not the Jumpstart front card
        Assert.DoesNotContain("Beast // Beast", Cards.Keys);
    }
}
