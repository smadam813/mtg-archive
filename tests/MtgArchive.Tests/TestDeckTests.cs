using System.Text.Json.Nodes;

namespace MtgArchive.Tests;

/// <summary>The test decks carry their card feature profiles, so no
/// XMage test derives a profile from anything else.</summary>
public sealed class TestDeckTests
{
    [Fact]
    public void Every_case_file_carries_a_profile_for_every_face()
    {
        foreach (var name in TestDecks.Names())
        {
            var deck = TestDecks.Load(name);
            foreach (var card in deck.Deck)
            {
                Assert.True(deck.Profiles.ByFace.ContainsKey(card.FrontFaceName ?? card.Name), $"{name}: no profile for {card.Name}");
                if (card.AsBackFace() is { } back)
                {
                    Assert.True(deck.Profiles.ByFace.ContainsKey(back.Name), $"{name}: no profile for the back face {back.Name}");
                }
            }
        }
    }

    /// <summary>Phase 4, Review Focus 5: an old case file without profiles is an error, never a deck of featureless cards.</summary>
    [Fact]
    public void A_case_file_without_profiles_is_an_error()
    {
        var name = TestDecks.Names()[0];
        var node = JsonNode.Parse(File.ReadAllText(TestDecks.CasePath(name)))!.AsObject();
        node.Remove("profiles");
        node.Remove("unprofiled");
        var dir = Path.Combine(Path.GetTempPath(), $"decks-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".case.json");
        File.WriteAllText(path, node.ToJsonString());

        var ex = Assert.Throws<InvalidOperationException>(() => TestDecks.LoadFile(path));

        Assert.Contains("no profiles", ex.Message);
        Assert.Contains(name, ex.Message);
    }

    [Fact]
    public void The_dq2_needle_profile_names_the_ritual_as_spell_mana()
    {
        var deck = TestDecks.Load("dq2-needle-goal-aware");

        Assert.Equal("{B}{B}{B}", deck.Profiles.ByFace["Dark Ritual"].SpellMana);
        Assert.NotNull(deck.Profiles.ByFace["Demonic Tutor"].Tutor);
    }
}
