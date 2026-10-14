using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Data;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The source-profile ingest phase end to end. Profiles
/// persist from a real run, so tier-1 code and card search never re-parse
/// Oracle text. Every land has a row. Unmatched search-lands and unmatched
/// enters-tapped lands reach the ingest report. The staging/swap rebuild
/// keeps a rerun idempotent.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SourceProfileTests(PostgresFixture postgres)
{
    private static readonly Guid Fetch = Guid.NewGuid();
    private static readonly Guid Rock = Guid.NewGuid();
    private static readonly Guid Odd = Guid.NewGuid();
    private static readonly Guid Vanilla = Guid.NewGuid();
    private static readonly Guid Tapped = Guid.NewGuid();
    private static readonly Guid OddTapped = Guid.NewGuid();

    [Fact]
    public async Task Run_persists_profiles_and_reports_unmatched_search_lands()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();

        var runId = await TriggerAsync(client);
        var run = await WaitForCompletionAsync(client, runId, "succeeded");

        var unmatched = Assert.Single(
            run.GetProperty("report").GetProperty("unmatched_search_lands").EnumerateArray());
        Assert.Equal(Odd, unmatched.GetProperty("oracle_id").GetGuid());
        Assert.Equal("Odd Fetch", unmatched.GetProperty("name").GetString());

        var oddTapped = Assert.Single(
            run.GetProperty("report").GetProperty("unmatched_enters_tapped_lands").EnumerateArray());
        Assert.Equal(OddTapped, oddTapped.GetProperty("oracle_id").GetGuid());
        Assert.Equal("Odd Tapped Land", oddTapped.GetProperty("name").GetString());

        await using var db = PostgresFixture.CreateContext(connectionString);
        var profiles = await ProfilesAsync(db);
        // Every land has a row, facets or not; a nonland only with a facet.
        Assert.Equal(["Chromatic Rock", "Classic Fetch", "Odd Tapped Land", "Tapped Land"], profiles.Keys.Order());

        var fetch = profiles["Classic Fetch"];
        Assert.Null(fetch.ProducedColors);
        Assert.Equal(["Forest", "Plains"], fetch.FetchLandTypes);
        Assert.False(fetch.FetchBasicOnly);
        Assert.Equal(EntersTappedState.Untapped, fetch.EntersTapped);
        Assert.Equal(FetchActivation.Untapped, fetch.FetchActivation);

        var rock = profiles["Chromatic Rock"];
        Assert.Equal(["C"], rock.ProducedColors);
        Assert.Null(rock.FetchLandTypes);
        Assert.Null(rock.EntersTapped);
        Assert.Null(rock.FetchActivation);

        var tapped = profiles["Tapped Land"];
        Assert.Null(tapped.ProducedColors);
        Assert.Null(tapped.FetchLandTypes);
        Assert.Equal(EntersTappedState.Tapped, tapped.EntersTapped);

        Assert.Null(profiles["Odd Tapped Land"].EntersTapped);
    }

    [Fact]
    public async Task Rerun_over_the_same_data_rebuilds_the_same_profiles()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        await WaitForCompletionAsync(client, await TriggerAsync(client, force: true), "succeeded");

        await using var db = PostgresFixture.CreateContext(connectionString);
        var profiles = await ProfilesAsync(db);
        Assert.Equal(4, profiles.Count);
        Assert.Equal(["Forest", "Plains"], profiles["Classic Fetch"].FetchLandTypes);
        Assert.Equal(EntersTappedState.Tapped, profiles["Tapped Land"].EntersTapped);
        Assert.Null(profiles["Odd Tapped Land"].EntersTapped);
    }

    [Fact]
    public async Task Land_faces_of_modal_double_faced_cards_get_their_own_colors()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString, Lines(
            ModalLine(Guid.NewGuid(), ["B", "W"],
                ("Brightclimb Pathway", "Land", "{T}: Add {W}."),
                ("Grimclimb Pathway", "Land", "{T}: Add {B}.")),
            ModalLine(Guid.NewGuid(), ["R"],
                ("Shatterskull Smashing", "Sorcery", "Shatterskull Smashing deals X damage divided as you choose among up to two target creatures and/or planeswalkers."),
                ("Shatterskull, the Hammer Pass", "Land", "As Shatterskull, the Hammer Pass enters, you may pay 3 life. If you don't, it enters tapped.\n{T}: Add {R}.")),
            ModalLine(Guid.NewGuid(), ["G", "U"],
                ("Odd Spell", "Instant", "Draw a card."),
                ("Odd Land", "Land", "{T}: Add one mana of any color in your commander's color identity."))));
        using var client = app.CreateClient();

        var run = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        Assert.Contains(
            run.GetProperty("report").GetProperty("errors").EnumerateArray(),
            e => e.GetString()!.StartsWith("Land face Odd Land of Odd Spell // Odd Land names no produced_mana color", StringComparison.Ordinal));

        var expected = new Dictionary<string, string[]?>
        {
            ["Brightclimb Pathway"] = ["W"],
            ["Grimclimb Pathway"] = ["B"],
            ["Shatterskull Smashing"] = null,
            ["Shatterskull, the Hammer Pass"] = ["R"],
            ["Odd Spell"] = null,
            ["Odd Land"] = ["G", "U"],
        };
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(expected, await db.CardFaces.ToDictionaryAsync(f => f.Name, f => f.ProducedColors));
        }

        await WaitForCompletionAsync(client, await TriggerAsync(client, force: true), "succeeded");
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(expected, await db.CardFaces.ToDictionaryAsync(f => f.Name, f => f.ProducedColors));
        }
    }

    // The opponent-dependent facet persists for nonland
    // sources, and an unmatched opponent or combat source reaches the ingest report.
    [Fact]
    public async Task Run_persists_the_opponent_dependent_facet_and_reports_unmatched_sources()
    {
        var unmatched = Guid.NewGuid();
        var unmatchedFetch = Guid.NewGuid();
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString, Lines(
            CardLine(Guid.NewGuid(), "Tithe", typeLine: "Enchantment", producedMana: ["B", "G", "R", "U", "W"],
                oracleText: "Whenever an opponent draws a card, you create a Treasure token."),
            CardLine(unmatched, "Odd Producer", typeLine: "Creature — Human", producedMana: ["C"],
                oracleText: "{T}: Add {C}{C}. Activate only during an opponent's turn."),
            CardLine(Guid.NewGuid(), "Land Tax", typeLine: "Enchantment",
                oracleText: "At the beginning of your upkeep, if an opponent controls more lands than you, you may search your library for up to three basic land cards, reveal them, put them into your hand, then shuffle."),
            CardLine(unmatchedFetch, "Sword of the Animist", typeLine: "Legendary Artifact — Equipment",
                oracleText: "Equipped creature gets +1/+1.\nWhenever equipped creature attacks, you may search your library for a basic land card, put it onto the battlefield tapped, then shuffle.\nEquip {2}"),
            CardLine(Guid.NewGuid(), "Opponent Land", typeLine: "Land", manaCost: "", cmc: 0, producedMana: ["R"],
                oracleText: "Whenever an opponent draws a card, add {R}."),
            CardLine(Guid.NewGuid(), "Odd Land", typeLine: "Land", manaCost: "", cmc: 0, producedMana: ["C"],
                oracleText: "{T}: Add {C}{C}. Activate only during an opponent's turn.")));
        using var client = app.CreateClient();

        var run = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        var entries = run.GetProperty("report").GetProperty("unmatched_opponent_sources").EnumerateArray()
            .ToDictionary(e => e.GetProperty("name").GetString()!, e => e.GetProperty("oracle_id").GetGuid());
        Assert.Equal(new Dictionary<string, Guid> { ["Odd Producer"] = unmatched, ["Sword of the Animist"] = unmatchedFetch }, entries);
        await using var db = PostgresFixture.CreateContext(connectionString);
        var profiles = await ProfilesAsync(db);
        Assert.True(profiles["Tithe"].OpponentDependent);
        Assert.True(profiles["Land Tax"].OpponentDependent);
        Assert.False(profiles["Sword of the Animist"].OpponentDependent);
        Assert.False(profiles["Odd Producer"].OpponentDependent);
        Assert.False(profiles["Opponent Land"].OpponentDependent);
    }

    // A nonland card that gives its only token to another player
    // persists the token-to-another-player facet and does not appear in the report.
    [Fact]
    public async Task Run_persists_the_token_to_another_player_facet()
    {
        var mixed = Guid.NewGuid();
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString, Lines(
            CardLine(Guid.NewGuid(), "Offer", typeLine: "Instant", producedMana: ["B", "G", "R", "U", "W"],
                oracleText: "Counter target noncreature spell. Its controller creates two Treasure tokens."),
            CardLine(Guid.NewGuid(), "Dispute", typeLine: "Instant", producedMana: ["B", "G", "R", "U", "W"],
                oracleText: "Draw two cards and create a Treasure token."),
            CardLine(Guid.NewGuid(), "Gifter", typeLine: "Instant", producedMana: ["B", "G", "R", "U", "W"],
                oracleText: "Gift a Treasure (You may promise an opponent a gift as you cast this spell. If you do, they create a Treasure token.)\nDraw a card."),
            CardLine(mixed, "Mixed", typeLine: "Creature — Human", producedMana: ["B", "G", "R", "U", "W"],
                oracleText: "Whenever an opponent draws a card, add {R}.\nWhen this creature dies, target opponent creates a Treasure token.")));
        using var client = app.CreateClient();

        var run = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        // Only the mixed card (dependent plus given away, no independent ability) is unmatched.
        var entry = Assert.Single(run.GetProperty("report").GetProperty("unmatched_opponent_sources").EnumerateArray());
        Assert.Equal(mixed, entry.GetProperty("oracle_id").GetGuid());
        await using var db = PostgresFixture.CreateContext(connectionString);
        var profiles = await ProfilesAsync(db);
        Assert.True(profiles["Gifter"].TokenToAnotherPlayer);
        Assert.False(profiles["Mixed"].TokenToAnotherPlayer);
        Assert.False(profiles["Mixed"].OpponentDependent);
        Assert.True(profiles["Offer"].TokenToAnotherPlayer);
        Assert.False(profiles["Offer"].OpponentDependent);
        Assert.False(profiles["Dispute"].TokenToAnotherPlayer);
    }

    private Task<string> SetUpAsync()
        => postgres.CreateDatabaseAsync($"source_profiles_{Guid.NewGuid():N}");

    private static ArchiveApp NewApp(string connectionString, string? oracleCards = null)
    {
        var source = new FakeBulkSource
        {
            OracleCards = oracleCards ?? Lines(
                CardLine(Fetch, "Classic Fetch", typeLine: "Land", manaCost: "", cmc: 0,
                    oracleText: "{T}, Sacrifice this land: Search your library for a Forest or Plains card, "
                        + "put it onto the battlefield, then shuffle."),
                CardLine(Rock, "Chromatic Rock", oracleText: "{T}: Add {C}.", producedMana: ["C"]),
                CardLine(Odd, "Odd Fetch", typeLine: "Sorcery", oracleText:
                    "Sacrifice any number of lands, then search your library for up to that many land cards, "
                    + "put them onto the battlefield tapped, then shuffle."),
                CardLine(Vanilla, "Vanilla Bear", typeLine: "Creature — Bear"),
                CardLine(Tapped, "Tapped Land", typeLine: "Land", manaCost: "", cmc: 0,
                    oracleText: "This land enters tapped.\nWhen this land enters, you gain 1 life."),
                CardLine(OddTapped, "Odd Tapped Land", typeLine: "Land", manaCost: "", cmc: 0,
                    oracleText: "Odd Tapped Land enter the battlefield tapped.\n{T}: Add {C}.")),
        };
        return new ArchiveApp(
            autoMigrate: true,
            connectionString,
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(source);
                services.RemoveAll<ICrSource>();
                services.AddSingleton<ICrSource>(new FakeCrSource());
            });
    }

    private static async Task<IReadOnlyDictionary<string, SourceProfile>> ProfilesAsync(
        ArchiveDbContext db)
        => (await db.SourceProfiles
            .Join(db.Cards, p => p.CardId, c => c.Id, (p, c) => new { c.Name, Profile = p })
            .ToListAsync())
            .ToDictionary(x => x.Name, x => x.Profile);
}
