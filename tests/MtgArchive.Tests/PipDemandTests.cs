using System.Text.Json;
using MtgArchive.Simulation;

namespace MtgArchive.Tests;

/// <summary>
/// Pip demand extraction: per-color pips at demand turn = mana
/// value, hybrid "either" lines, Phyrexian zero-with-note, X adding nothing,
/// and the flagged commander row. Cost cases live in
/// <c>fixtures/tier1/pip-demand.json</c>.
/// </summary>
public sealed class PipDemandTests
{
    public static TheoryData<string, string, int, int, string, string, int> FixtureCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "tier1", "pip-demand.json");
        var cases = JsonSerializer.Deserialize<List<FixtureCase>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        var data = new TheoryData<string, string, int, int, string, string, int>();
        foreach (var c in cases)
        {
            data.Add(
                c.Note,
                c.ManaCost,
                c.ManaValue,
                c.DemandTurn,
                Render(c.Pips),
                Render(c.Either.Select(e => new EitherPips(e.Colors, e.Count))),
                c.Notes);
        }

        return data;
    }

    private sealed record FixtureCase(
        string Note, string ManaCost, int ManaValue, int DemandTurn,
        Dictionary<string, int> Pips, List<EitherCase> Either, int Notes);

    private sealed record EitherCase(string[] Colors, int Count);

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void Cost_cases_match_the_fixture(
        string note, string manaCost, int manaValue, int demandTurn, string pips, string either, int notes)
    {
        Assert.NotEmpty(note);

        var row = PipDemand.Row(Spell(manaCost, manaValue));

        Assert.Equal(demandTurn, row.DemandTurn);
        Assert.Equal(pips, Render(row.Pips));
        Assert.Equal(either, Render(row.Either));
        Assert.Equal(notes, row.Notes.Count);
    }

    private static string Render(IEnumerable<KeyValuePair<string, int>> pips)
        => string.Join(' ', pips.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

    private static string Render(IEnumerable<EitherPips> either)
        => string.Join(' ', either.Select(e => $"{string.Join('/', e.Colors)}={e.Count}"));

    [Fact]
    public void Phyrexian_note_names_the_pip()
    {
        var row = PipDemand.Row(Spell("{W/P}", 1));

        Assert.Empty(row.Pips);
        Assert.Contains("{W/P}", Assert.Single(row.Notes));
    }

    [Fact]
    public void Commander_row_is_present_and_flagged_and_lands_are_skipped()
    {
        var commander = Spell("{2}{G}{G}", 4) with { IsCommander = true, Name = "Commander" };
        var land = new DeckCard { OracleId = Guid.NewGuid(), Name = "Forest", Quantity = 10, IsLand = true };

        var rows = PipDemand.Extract([commander, land, Spell("{W}", 1)]);

        Assert.Equal(2, rows.Count);
        var flagged = Assert.Single(rows, r => r.IsCommander);
        Assert.Equal("Commander", flagged.Name);
        Assert.Equal(2, flagged.Pips["G"]);
        Assert.Equal(4, flagged.DemandTurn);
    }

    private static DeckCard Spell(string manaCost, int manaValue) => new()
    {
        OracleId = Guid.NewGuid(),
        Name = "Spell",
        Quantity = 1,
        ManaCost = manaCost,
        ManaValue = manaValue,
    };
}
