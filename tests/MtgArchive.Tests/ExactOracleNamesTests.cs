using MtgArchive.Data;
using MtgArchive.Ingest;

namespace MtgArchive.Tests;

/// <summary>
/// The curated-file name lookup applies the shared namesake rule:
/// a real card wins over its token namesake, and two real namesakes
/// stay an ingest error. Seeded names carry a unique suffix so the shared
/// database never accumulates accidental namesakes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExactOracleNamesTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_curated_name_with_a_token_namesake_resolves_to_the_real_card()
    {
        var real = Guid.NewGuid();
        var name = $"Ornithopter {real:N}";
        await SeedAsync(Named(real, name, "normal"), Named(Guid.NewGuid(), name, "token"));
        var errors = new List<string>();

        var resolved = await ResolveAsync(name, errors);

        Assert.Empty(errors);
        Assert.Equal(real, resolved[name].OracleId);
    }

    [Fact]
    public async Task Two_real_namesakes_stay_an_error()
    {
        var name = $"Twin Name {Guid.NewGuid():N}";
        await SeedAsync(
            Named(Guid.NewGuid(), name, "normal"),
            Named(Guid.NewGuid(), name, "normal"),
            Named(Guid.NewGuid(), name, "token"));
        var errors = new List<string>();

        var resolved = await ResolveAsync(name, errors);

        Assert.Empty(resolved);
        Assert.Equal([$"ambiguous card name '{name}': 2 corpus cards share it"], errors);
    }

    private async Task<Dictionary<string, ExactOracleNames.Match>> ResolveAsync(string name, List<string> errors)
    {
        await using var db = postgres.CreateCorpusContext();
        return await ExactOracleNames.ResolveAsync(db, [name], errors, CancellationToken.None);
    }

    private async Task SeedAsync(params Card[] cards)
    {
        await using var db = postgres.CreateCorpusContext();
        db.Cards.AddRange(cards);
        await db.SaveChangesAsync();
    }

    private static Card Named(Guid oracleId, string name, string layout)
    {
        var card = TestCards.Minimal(oracleId);
        card.Name = name;
        card.Layout = layout;
        return card;
    }
}
