using MtgArchive.Embedding;

namespace MtgArchive.Tests;

/// <summary>
/// The symbol verbalizer (ablation cell B): a pure rewrite of
/// the symbols Scryfall emits in oracle text into words, deterministic and
/// model-free. An unknown symbol passes through verbatim and is counted.
/// </summary>
public sealed class SymbolVerbalizerTests
{
    [Theory]
    [InlineData("{T}: Add {C}{C}.", "tap: Add two colorless mana.")]
    [InlineData("{T}: Add {G}.", "tap: Add green mana.")]
    [InlineData("{T}: Add {C}{C}{C}.", "tap: Add three colorless mana.")]
    [InlineData("{Q}: Draw a card.", "untap: Draw a card.")]
    [InlineData("Miracle {1}{U}", "Miracle one generic mana and blue mana")]
    [InlineData("{2}{U}{U}: Draw a card.", "two generic mana and two blue mana: Draw a card.")]
    [InlineData("{W}{U}{B}{R}{G}: Win the game.", "white mana, blue mana, black mana, red mana, and green mana: Win the game.")]
    [InlineData("This spell costs {1} less to cast.", "This spell costs one generic mana less to cast.")]
    [InlineData("{0}: Sacrifice this artifact.", "zero mana: Sacrifice this artifact.")]
    [InlineData("{X}{G}: Put X +1/+1 counters.", "X mana and green mana: Put X +1/+1 counters.")]
    [InlineData("{S}: Add {C}.", "snow mana: Add colorless mana.")]
    [InlineData("{100}: Add one mana of any color.", "100 generic mana: Add one mana of any color.")]
    [InlineData("{20}: Cast any number of Eldrazi spells.", "twenty generic mana: Cast any number of Eldrazi spells.")]
    public void Mana_and_tap_symbols_become_words(string oracle, string expected)
        => Assert.Equal(expected, SymbolVerbalizer.Verbalize(oracle));

    [Theory]
    [InlineData("{R/W}: This creature becomes a Kithkin Spirit.", "red or white mana: This creature becomes a Kithkin Spirit.")]
    [InlineData("{R/W}{R/W}{R/W}: Do it.", "three red or white mana: Do it.")]
    [InlineData("{2/W}: Do it.", "two generic or white mana: Do it.")]
    [InlineData("{B/P}: Do it.", "Phyrexian black mana: Do it.")]
    [InlineData("{C/P}: Do it.", "Phyrexian colorless mana: Do it.")]
    [InlineData("{R/W/P}: Do it.", "Phyrexian red or white mana: Do it.")]
    public void Hybrid_and_Phyrexian_symbols_are_spelled_out(string oracle, string expected)
        => Assert.Equal(expected, SymbolVerbalizer.Verbalize(oracle));

    [Theory]
    [InlineData("you get {E}.", "you get an energy counter.")]
    [InlineData("you get {E}{E}.", "you get two energy counters.")]
    [InlineData("{T}, Pay six {E}: Look.", "tap, Pay six energy counters: Look.")]
    [InlineData("Pay one {E}: Scry 1.", "Pay one energy counter: Scry 1.")]
    [InlineData("Pay 1 {E}: Scry 1.", "Pay 1 energy counter: Scry 1.")]
    [InlineData("Pay two {E}{E}: Draw.", "Pay two energy counters: Draw.")]
    [InlineData("you get zero {E}.", "you get zero energy counters.")]
    [InlineData("Create {TK}.", "Create a ticket.")]
    [InlineData("Pay {TK}{TK}{TK}: Draw.", "Pay three tickets: Draw.")]
    [InlineData("{P}{P} — Draw a card.", "two pawprints — Draw a card.")]
    [InlineData("Choose up to five {P} worth of modes.", "Choose up to five pawprints worth of modes.")]
    [InlineData("you get {A}.", "you get an acorn counter.")]
    [InlineData("a {CHAOS} roll", "a chaos symbol roll")]
    [InlineData("{T}: Add {∞}.", "tap: Add infinite mana.")]
    [InlineData("cost {½} less", "cost half a generic mana less")]
    [InlineData("Add {HR}.", "Add half a red mana.")]
    [InlineData("pay {D}.", "pay a land drop.")]
    [InlineData("({L} can be paid", "(legendary mana can be paid")]
    [InlineData("a spell with {H} in its mana cost", "a spell with one colored mana or two life in its mana cost")]
    [InlineData("{1}{Z}: Grow.", "one generic mana and Z mana: Grow.")]
    public void Counters_tickets_and_the_rest_of_the_symbology_are_named(string oracle, string expected)
        => Assert.Equal(expected, SymbolVerbalizer.Verbalize(oracle));

    [Fact]
    public void Lowercase_mana_symbols_read_as_their_uppercase_form()
    {
        Assert.Equal("white mana ➧ blue mana", SymbolVerbalizer.Verbalize("{w} ➧ {u}"));
        Assert.Equal("Add two colorless mana.", SymbolVerbalizer.Verbalize("Add {C}{c}."));
    }

    [Fact]
    public void Text_without_symbols_is_unchanged()
    {
        const string oracle = "Put target creature card from a graveyard onto the battlefield under your control.\nYou lose life equal to that card's mana value.";
        Assert.Equal(oracle, SymbolVerbalizer.Verbalize(oracle));
    }

    [Fact]
    public void An_unknown_symbol_passes_through_verbatim_and_is_counted()
    {
        var unknown = new SymbolCounter();

        var text = SymbolVerbalizer.Verbalize("{T}: Add {FOO}. Pay {FOO}{BAR}.", unknown);

        Assert.Equal("tap: Add {FOO}. Pay {FOO}{BAR}.", text);
        Assert.Equal(3, unknown.Total);
        Assert.Equal([new("{FOO}", 2), new("{BAR}", 1)], unknown.Ordered);
    }
}
