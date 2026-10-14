using MtgArchive.Embedding;
using MtgArchive.Ingest;

namespace MtgArchive.Tests;

/// <summary>
/// The card document composer over the fixture faces in
/// <c>fixtures/embedding/</c>, read through the real snapshot reader and
/// card mapping: field order, symbols verbatim, the P/T and loyalty suffix
/// forms, tags appended, rulings excluded, and one document per face.
/// </summary>
public sealed class CardDocumentTests
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Documents = Compose(CardDocumentComposition.Verbatim);

    [Fact]
    public void Fields_come_in_order_with_symbols_verbatim_and_tags_appended()
    {
        Assert.Equal(
            ["Llanowar Elves\nCreature — Elf Druid\n{T}: Add {G}.\n1/1\n{G}\nTags: mana-dork, ramp"],
            Documents["Llanowar Elves"]);
        Assert.Equal(
            ["Sol Ring\nArtifact\n{T}: Add {C}{C}.\n{1}\nTags: mana-rock, ramp"],
            Documents["Sol Ring"]);
    }

    [Fact]
    public void Loyalty_is_a_plain_suffix_after_the_oracle_text()
    {
        var teferi = Assert.Single(Documents["Teferi, Temporal Archmage"]);

        Assert.StartsWith("Teferi, Temporal Archmage\nLegendary Planeswalker — Teferi\n+1: Look at the top two cards", teferi);
        Assert.EndsWith("can be your commander.\nLoyalty 5\n{4}{U}{U}", teferi);
    }

    [Fact]
    public void An_inherited_tag_stays_off_the_tags_line()
    {
        // The fixture tags Liliana reanimate-creature only; reanimate is its
        // inherited parent and is a filter, not document text.
        Assert.All(
            Documents["Liliana, Heretical Healer // Liliana, Defiant Necromancer"],
            face => Assert.EndsWith("\nTags: reanimate-creature", face));
    }

    [Fact]
    public void Rulings_are_never_part_of_the_document()
    {
        Assert.DoesNotContain("cannot be countered", Documents["Llanowar Elves"][0]);
        Assert.DoesNotContain("adds two colorless mana", Documents["Sol Ring"][0]);
    }

    [Fact]
    public void A_multi_faced_card_yields_one_document_per_face_each_carrying_the_tags()
    {
        var liliana = Documents["Liliana, Heretical Healer // Liliana, Defiant Necromancer"];
        Assert.Equal(2, liliana.Count);
        Assert.StartsWith("Liliana, Heretical Healer\nLegendary Creature — Human Cleric\nLifelink\n", liliana[0]);
        Assert.EndsWith("Zombie creature token.\n2/3\n{1}{B}{B}\nTags: reanimate-creature", liliana[0]);
        Assert.StartsWith("Liliana, Defiant Necromancer\nLegendary Planeswalker — Liliana\n+2: Each player discards a card.\n", liliana[1]);
        // The back face has no mana cost: the line is omitted, not left blank.
        Assert.EndsWith("next end step.\"\nLoyalty 3\nTags: reanimate-creature", liliana[1]);

        Assert.Equal(
            [
                "Fire\nInstant\nFire deals 2 damage divided as you choose among one or two targets.\n{1}{R}",
                "Ice\nInstant\nTap target permanent.\nDraw a card.\n{1}{U}",
            ],
            Documents["Fire // Ice"]);
    }

    [Fact]
    public void Stripping_reminder_text_keeps_the_keyword_and_leaves_symbols_as_printed()
    {
        var stripped = Compose(CardDocumentComposition.StripReminderText);

        // Glistener Elf's reminder text carries its only functional words; the
        // ablation accepts that loss and keeps the keyword.
        Assert.Equal(["Glistener Elf\nCreature — Elf Warrior\nInfect\n1/1\n{G}\nTags: infect"], stripped["Glistener Elf"]);
        Assert.Equal(Documents["Sol Ring"], stripped["Sol Ring"]);
    }

    [Fact]
    public void Verbalizing_symbols_rewrites_the_oracle_text_only_and_keeps_the_mana_cost_line()
    {
        var verbalized = Compose(CardDocumentComposition.VerbalizeSymbols);

        // Sol Ring's whole rules text is symbols: this is the card cell B exists for.
        Assert.Equal(["Sol Ring\nArtifact\ntap: Add two colorless mana.\n{1}\nTags: mana-rock, ramp"], verbalized["Sol Ring"]);
        Assert.Equal(["Llanowar Elves\nCreature — Elf Druid\ntap: Add green mana.\n1/1\n{G}\nTags: mana-dork, ramp"], verbalized["Llanowar Elves"]);
        // Cell B includes cell A.
        Assert.Equal(["Glistener Elf\nCreature — Elf Warrior\nInfect\n1/1\n{G}\nTags: infect"], verbalized["Glistener Elf"]);
    }

    private static Dictionary<string, IReadOnlyList<string>> Compose(CardDocumentComposition composition)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "embedding");
        var errors = new List<string>();
        var snapshot = new OracleSnapshot(
            ScryfallSnapshotReader.ReadCards(Path.Combine(dir, "oracle-cards.jsonl"), errors),
            ScryfallSnapshotReader.ReadRulings(Path.Combine(dir, "rulings.jsonl"), errors),
            ScryfallSnapshotReader.ReadOracleTags(Path.Combine(dir, "oracle-tags.jsonl"), errors, errors));
        Assert.Empty(errors);
        return snapshot.Cards.ToDictionary(
            c => c.Name,
            c => CardDocument.Compose(CardUpsertPhase.Map(c, snapshot), composition));
    }
}
