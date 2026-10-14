using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Data;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using Npgsql;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The card-feature ingest phase end to end (spec §5.1). Every card face
/// gets a stored profile from a real run. An unprofiled face reaches the
/// ingest report with its sentence. The staging/swap rebuild keeps a rerun
/// idempotent under canonical constraint names.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CardFeaturePhaseTests(PostgresFixture postgres)
{
    private const string AwakeningSentence =
        "Put any number of cards from your hand on the bottom of your library, then draw that many cards plus one";

    private static readonly Guid Ring = Guid.NewGuid();
    private static readonly Guid Whisper = Guid.NewGuid();
    private static readonly Guid Bears = Guid.NewGuid();
    private static readonly Guid Awakening = Guid.NewGuid();
    private static readonly Guid Counterspell = Guid.NewGuid();
    private static readonly Guid Snake = Guid.NewGuid();

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["Counterspell#0"] = "counter=spell",
        ["Mystic Snake#0"] = "response_unprofiled(When this creature enters, counter target spell)",
        ["Grizzly Bears#0"] = "",
        ["Night's Whisper#0"] = "draws",
        ["Sol Ring#0"] = "develops tap={C}{C} activated",
        ["Valakut Awakening // Valakut Stoneforge#0"] = $"unprofiled({AwakeningSentence})",
        ["Valakut Awakening // Valakut Stoneforge#1"] = "develops tap={R} activated",
    };

    [Fact]
    public async Task Run_stores_a_profile_per_face_and_reports_unprofiled_faces()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();

        var run = await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");

        var entry = Assert.Single(run.GetProperty("report").GetProperty("unprofiled_cards").EnumerateArray());
        Assert.Equal(Awakening, entry.GetProperty("oracle_id").GetGuid());
        Assert.Equal("Valakut Awakening // Valakut Stoneforge", entry.GetProperty("name").GetString());
        Assert.Equal("Valakut Awakening", entry.GetProperty("face").GetString());
        Assert.Equal(AwakeningSentence, entry.GetProperty("sentence").GetString());

        // The response list holds profiled and unprofiled faces, apart from
        // the unprofiled list above.
        var responses = run.GetProperty("report").GetProperty("response_cards").EnumerateArray()
            .Select(response => (
                response.GetProperty("oracle_id").GetGuid(),
                response.GetProperty("face").GetString(),
                response.GetProperty("profiled").GetBoolean(),
                response.TryGetProperty("sentence", out var sentence) ? sentence.GetString() : null))
            .OrderBy(response => response.Item2, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
        [
            (Counterspell, "Counterspell", true, null),
            (Snake, "Mystic Snake", false, "When this creature enters, counter target spell"),
        ], responses);

        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(Expected, await RowsAsync(db));
    }

    [Fact]
    public async Task Rerun_rebuilds_the_same_rows_under_canonical_names()
    {
        var connectionString = await SetUpAsync();
        using var app = NewApp(connectionString);
        using var client = app.CreateClient();
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        await WaitForCompletionAsync(client, await TriggerAsync(client, force: true), "succeeded");

        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(Expected, await RowsAsync(db));
        await AssertCanonicalNamesAsync(connectionString);
    }

    private Task<string> SetUpAsync()
        => postgres.CreateDatabaseAsync($"card_features_{Guid.NewGuid():N}");

    private static ArchiveApp NewApp(string connectionString)
    {
        var source = new FakeBulkSource
        {
            OracleCards = Lines(
                CardLine(Ring, "Sol Ring", oracleText: "{T}: Add {C}{C}.", producedMana: ["C"]),
                CardLine(Whisper, "Night's Whisper", typeLine: "Sorcery", manaCost: "{1}{B}", cmc: 2,
                    oracleText: "You draw two cards and lose 2 life."),
                CardLine(Bears, "Grizzly Bears", typeLine: "Creature — Bear", manaCost: "{1}{G}", cmc: 2),
                CardLine(Counterspell, "Counterspell", typeLine: "Instant", manaCost: "{U}{U}", cmc: 2, oracleText: "Counter target spell."),
                CardLine(Snake, "Mystic Snake", typeLine: "Creature — Snake", manaCost: "{1}{G}{U}{U}", cmc: 4,
                    oracleText: "Flash\nWhen this creature enters, counter target spell."),
                ModalLine(Awakening, ["R"],
                    ("Valakut Awakening", "Instant", AwakeningSentence + "."),
                    ("Valakut Stoneforge", "Land", "This land enters tapped.\n{T}: Add {R}."))),
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

    /// <summary>Every stored row as "card name#face index" → its profile line.</summary>
    private static async Task<Dictionary<string, string>> RowsAsync(ArchiveDbContext db)
        => (await db.CardFeatureProfileRows
                .Join(db.Cards, row => row.CardId, card => card.Id, (row, card) => new { card.Name, Row = row })
                .ToListAsync())
            .ToDictionary(x => $"{x.Name}#{x.Row.FaceIndex}", x => ProfileText.Of(x.Row.ToProfile(), x.Row.UnmatchedSentence, x.Row.ResponseUnmatchedSentence));

    /// <summary>After any number of swaps, the live table carries the
    /// canonical constraint names the migration declared.</summary>
    private static async Task AssertCanonicalNamesAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT conrelid::regclass::text, conname FROM pg_constraint
            WHERE contype IN ('p', 'f') AND conrelid::regclass::text LIKE 'card_feature_profile%'
            ORDER BY conname
            """, conn);
        var constraints = new List<(string Table, string Name)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                constraints.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        Assert.Equal(
        [
            ("card_feature_profile", "fk_card_feature_profile_card"),
            ("card_feature_profile_staging", "fk_card_feature_profile_card_staging"),
            ("card_feature_profile", "pk_card_feature_profile"),
            ("card_feature_profile_staging", "pk_card_feature_profile_staging"),
        ], constraints);
    }
}
