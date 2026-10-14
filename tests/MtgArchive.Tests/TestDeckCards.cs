using System.Text;
using MtgArchive.Ingest;
using MtgArchive.Simulation;
using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tests;

/// <summary>Deck-card builders for tests: corpus rows with no card feature profile.</summary>
internal static class TestDeckCards
{
    public static DeckCard Land(string name, string color, int quantity = 1) => new()
    {
        OracleId = Id(name),
        Name = name,
        Quantity = quantity,
        IsLand = true,
        IsBasic = true,
        IsPermanent = true,
        CardTypes = ["Land"],
        Supertypes = ["Basic"],
        ProducedColors = [color],
    };

    public static DeckCard Spell(string name, string manaCost, string cardType = "Sorcery", int quantity = 1)
    {
        var cost = ManaCost.Parse(manaCost);
        return new DeckCard
        {
            OracleId = Id(name),
            Name = name,
            Quantity = quantity,
            IsPermanent = CardDerivations.PermanentTypes.Contains(cardType),
            CardTypes = [cardType],
            ManaCost = manaCost,
            ManaValue = cost.Generic + cost.Pips.Count,
        };
    }

    public static DeckCard Commander(string name, string manaCost)
        => Spell(name, manaCost, "Creature") with { IsCommander = true };

    private static Guid Id(string name)
    {
        var bytes = new byte[16];
        Encoding.UTF8.GetBytes(name).Take(16).ToArray().CopyTo(bytes, 0);
        return new Guid(bytes);
    }
}
