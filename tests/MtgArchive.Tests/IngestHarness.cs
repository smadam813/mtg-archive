using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MtgArchive.Ingest;

namespace MtgArchive.Tests;

/// <summary>
/// Shared plumbing for tests that drive real ingest runs over HTTP: JSONL
/// builders for the fixture snapshot, and trigger/poll helpers against the
/// ingest API.
/// </summary>
internal static class IngestHarness
{
    public static string Lines(params string[] lines) => string.Join('\n', lines);

    /// <summary>Reads one JSONL card line through the snapshot reader.</summary>
    public static SnapshotCard ReadOne(string line)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, line);
            var errors = new List<string>();
            var card = Assert.Single(ScryfallSnapshotReader.ReadCards(path, errors));
            Assert.Empty(errors);
            return card;
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static string CardLine(
        Guid oracleId,
        string name,
        string typeLine = "Artifact",
        string manaCost = "{1}",
        decimal cmc = 1,
        string? oracleText = null,
        int? edhrecRank = null,
        string[]? keywords = null,
        string commanderLegality = "legal",
        string[]? colorIdentity = null,
        string[]? producedMana = null,
        bool gameChanger = false,
        string? power = null,
        string? toughness = null,
        BrawlKeys? brawl = null,
        bool? digital = null)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["oracle_id"] = oracleId,
            ["name"] = name,
            ["layout"] = "normal",
            ["type_line"] = typeLine,
            ["mana_cost"] = manaCost,
            ["cmc"] = cmc,
            ["oracle_text"] = oracleText,
            ["color_identity"] = colorIdentity ?? [],
            ["keywords"] = keywords ?? [],
            ["produced_mana"] = producedMana,
            ["released_at"] = "2020-01-01",
            ["game_changer"] = gameChanger,
            ["edhrec_rank"] = edhrecRank,
            ["power"] = power,
            ["toughness"] = toughness,
            ["scryfall_uri"] = $"https://scryfall.com/card/tst/1/{oracleId}",
            ["digital"] = digital,
            ["legalities"] = Legalities(commanderLegality, brawl),
        });

    /// <summary>The three Scryfall Brawl legality keys of a fixture card,
    /// and its Standard key. A null status leaves its key out of the record,
    /// as an older snapshot would.</summary>
    public sealed record BrawlKeys(string? Brawl, string? CompetitiveBrawl, string? StandardBrawl, string? Standard = null)
    {
        /// <summary>In all three Brawl pools; no Standard key.</summary>
        public static readonly BrawlKeys Legal = new("legal", "legal", "legal");
    }

    private static Dictionary<string, object?> Legalities(string commander, BrawlKeys? brawl)
    {
        var legalities = new Dictionary<string, object?> { ["commander"] = commander };
        if (brawl?.Brawl is { } b)
        {
            legalities["brawl"] = b;
        }

        if (brawl?.CompetitiveBrawl is { } c)
        {
            legalities["competitivebrawl"] = c;
        }

        if (brawl?.StandardBrawl is { } s)
        {
            legalities["standardbrawl"] = s;
        }

        if (brawl?.Standard is { } standard)
        {
            legalities["standard"] = standard;
        }

        return legalities;
    }

    /// <summary>A modal double-faced card line in Scryfall's shape: no
    /// top-level cost or text, one entry per face.</summary>
    public static string ModalLine(Guid oracleId, string[] producedMana, params (string Name, string TypeLine, string Text)[] faces)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["oracle_id"] = oracleId,
            ["name"] = string.Join(" // ", faces.Select(f => f.Name)),
            ["layout"] = "modal_dfc",
            ["type_line"] = string.Join(" // ", faces.Select(f => f.TypeLine)),
            ["cmc"] = 1,
            ["color_identity"] = producedMana,
            ["keywords"] = Array.Empty<string>(),
            ["produced_mana"] = producedMana,
            ["released_at"] = "2020-01-01",
            ["game_changer"] = false,
            ["legalities"] = new Dictionary<string, object?> { ["commander"] = "legal" },
            ["card_faces"] = faces.Select(f => new Dictionary<string, object?>
            {
                ["name"] = f.Name,
                ["type_line"] = f.TypeLine,
                ["mana_cost"] = f.TypeLine == "Land" ? "" : "{1}",
                ["oracle_text"] = f.Text,
            }).ToArray(),
        });

    public static string RulingLine(Guid oracleId, string comment)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["oracle_id"] = oracleId,
            ["source"] = "wotc",
            ["published_at"] = "2021-03-19",
            ["comment"] = comment,
        });

    public static string TagLine(string label, params Guid[] oracleIds)
        => TagLine(label, null, null, [.. oracleIds.Select(id => (id, (string?)null))]);

    public static string TagLine(
        string label, string? id, string[]? parentIds, params (Guid OracleId, string? Weight)[] taggings)
        => TagLine(label, id, parentIds, null, taggings);

    public static string TagLine(
        string label,
        string? id,
        string[]? parentIds,
        string[]? aliases,
        params (Guid OracleId, string? Weight)[] taggings)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["label"] = label,
            ["parent_ids"] = parentIds,
            ["aliases"] = aliases,
            ["taggings"] = taggings.Select(t => new Dictionary<string, object?>
            {
                ["oracle_id"] = t.OracleId,
                ["weight"] = t.Weight,
            }).ToArray(),
        });

    public static async Task<int> TriggerAsync(HttpClient client, bool force = false)
    {
        var response = await client.PostAsJsonAsync("/ingest", new { force });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = body.RootElement.GetProperty("run_id").GetInt32();
        Assert.Equal($"/ingest/runs/{runId}", response.Headers.Location?.ToString());
        return runId;
    }

    public static async Task<JsonElement> WaitForCompletionAsync(HttpClient client, int runId, string expected)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var run = await GetRunAsync(client, $"/ingest/runs/{runId}");
            if (run.GetProperty("status").GetString() != "running")
            {
                Assert.True(expected == run.GetProperty("status").GetString(), run.ToString());
                return run;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Ingest run {runId} did not complete.");
    }

    public static async Task<JsonElement> GetRunAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }
}

/// <summary>Fills an empty canonical slot and fails on demand, so a test
/// can fail a run without changing any source.</summary>
internal sealed class SwitchablePhase(string name) : IIngestPhase
{
    public bool Fail { get; set; }

    public string Name => name;

    public Task RunAsync(IngestContext context, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("fixture failure");
        }

        return Task.CompletedTask;
    }
}

/// <summary>Serves fixture JSONL as if downloaded, tracking the files so
/// tests can assert the temp-artifact lifecycle. Setting any content bumps
/// <see cref="UpdatedAt"/> — a fixture edit is a Scryfall republish, so the
/// corpus-state gate sees the source as changed.</summary>
internal sealed class FakeBulkSource : IScryfallBulkSource
{
    private string _oracleCards = "";
    private string _rulings = "";
    private string _oracleTags = "";

    public DateTimeOffset UpdatedAt { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public string OracleCards
    {
        get => _oracleCards;
        set
        {
            _oracleCards = value;
            UpdatedAt = UpdatedAt.AddSeconds(1);
        }
    }

    public string Rulings
    {
        get => _rulings;
        set
        {
            _rulings = value;
            UpdatedAt = UpdatedAt.AddSeconds(1);
        }
    }

    public string OracleTags
    {
        get => _oracleTags;
        set
        {
            _oracleTags = value;
            UpdatedAt = UpdatedAt.AddSeconds(1);
        }
    }

    public List<string> Downloaded { get; } = [];

    public Task<DateTimeOffset> LatestUpdatedAtAsync(CancellationToken cancellationToken)
        => Task.FromResult(UpdatedAt);

    public Task<DateTimeOffset> UpdatedAtAsync(ScryfallBulkType type, CancellationToken cancellationToken)
        => Task.FromResult(UpdatedAt);

    public async Task<string> DownloadAsync(ScryfallBulkType type, string workDir, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workDir);
        var path = Path.Combine(workDir, $"{type}.jsonl");
        await File.WriteAllTextAsync(path, type switch
        {
            ScryfallBulkType.OracleCards => OracleCards,
            ScryfallBulkType.Rulings => Rulings,
            _ => OracleTags,
        }, cancellationToken);
        lock (Downloaded)
        {
            Downloaded.Add(path);
        }

        return path;
    }
}
