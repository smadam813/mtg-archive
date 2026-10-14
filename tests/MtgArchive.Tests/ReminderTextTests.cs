using MtgArchive.Embedding;

namespace MtgArchive.Tests;

/// <summary>
/// The reminder-text stripper (ablation cell A): every
/// parenthetical leaves the embedded oracle text, keyword names and their
/// costs stay, and the text around the cut has no stray spaces.
/// </summary>
public sealed class ReminderTextTests
{
    [Theory]
    [InlineData(
        "Infect (This creature deals damage to creatures in the form of -1/-1 counters and to players in the form of poison counters.)",
        "Infect")]
    [InlineData(
        "Return all nonland permanents to their owners' hands.\nMiracle {1}{U} (You may cast this card for its miracle cost when you draw it if it's the first card you drew this turn.)",
        "Return all nonland permanents to their owners' hands.\nMiracle {1}{U}")]
    [InlineData(
        "Whenever a permanent you control is put into a graveyard, you get {E} (an energy counter).",
        "Whenever a permanent you control is put into a graveyard, you get {E}.")]
    [InlineData(
        "When this creature dies, manifest dread. (Look at the top two cards of your library. Put one onto the battlefield face down as a 2/2 creature and the other into your graveyard. Turn it face up any time for its mana cost if it's a creature card.)",
        "When this creature dies, manifest dread.")]
    [InlineData("{T}: Add {C}{C}.", "{T}: Add {C}{C}.")]
    public void Parenthetical_reminder_text_leaves_and_the_keyword_stays(string oracle, string expected)
        => Assert.Equal(expected, ReminderText.Strip(oracle));

    [Fact]
    public void A_line_that_is_only_reminder_text_is_dropped_not_left_blank()
    {
        // Forest's whole text is reminder text; Gitaxian Probe's first line is.
        Assert.Equal("", ReminderText.Strip("({T}: Add {G}.)"));
        Assert.Equal(
            "Look at target player's hand.\nDraw a card.",
            ReminderText.Strip("({U/P} can be paid with either {U} or 2 life.)\nLook at target player's hand.\nDraw a card."));
    }

    [Fact]
    public void Nested_parentheses_are_removed_as_one_parenthetical()
        => Assert.Equal(
            "Just Desserts deals π damage to target creature.",
            ReminderText.Strip(
                "Just Desserts deals π damage to target creature. (π is the ratio of a circle's circumference to its diameter. (It's a smidgen more than 3.))"));

    [Fact]
    public void An_unclosed_parenthesis_is_cut_to_the_end_of_its_line()
        => Assert.Equal("Flying\nDraw a card.", ReminderText.Strip("Flying (this never closes\nDraw a card."));

    [Fact]
    public void A_stray_closing_parenthesis_leaves_with_the_reminder_text()
        => Assert.Equal("Flying\nDraw a card.", ReminderText.Strip("Flying)\nDraw a card. (Now.))"));
}
