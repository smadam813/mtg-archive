using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Data;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The corpus the index tests run against: a fixed-seed 48-card snapshot
/// (fresh oracle_ids, the same shape every build) spanning every legality
/// case — color identities from colorless to five-color, legendary and
/// plain creatures, "can be your commander" text, the Companion keyword,
/// Scryfall <c>not_legal</c>, bans of every scope with later unbans, the
/// three Scryfall Brawl keys with pools and bans that differ by format, cards
/// that go stale on a second ingest, and every copy-limit case (basic land,
/// "seven cards named", singleton) — put through the real pipeline (card
/// upsert, banlist replay, legality rebuild), with a collection imported
/// through <c>PUT /collection</c> between the two ingests. The legality
/// table and the collection under test are therefore the production ones,
/// never test-side logic. Built once per suite run and shared read-only.
/// </summary>
internal static class IndexCorpus
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _connectionString;

    public static async Task<string> ConnectionStringAsync(PostgresFixture postgres)
    {
        await Gate.WaitAsync();
        try
        {
            return _connectionString ??= await BuildAsync(postgres);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<string> BuildAsync(PostgresFixture postgres)
    {
        var connectionString = await postgres.CreateDatabaseAsync($"index_corpus_{Guid.NewGuid():N}");
        var cards = Cards();
        var eventsPath = Path.Combine(
            Directory.CreateTempSubdirectory("mtg-archive-index-").FullName, "banlist-events.json");
        await File.WriteAllTextAsync(eventsPath, Events);

        var source = new FakeBulkSource();
        using var app = new ArchiveApp(
            autoMigrate: true,
            connectionString,
            services =>
            {
                services.RemoveAll<IScryfallBulkSource>();
                services.AddSingleton<IScryfallBulkSource>(source);
                services.RemoveAll<ICrSource>();
                services.AddSingleton<ICrSource>(new FakeCrSource());
            },
            new Dictionary<string, string?> { ["Banlist:EventsPath"] = eventsPath });
        using var client = app.CreateClient();

        source.OracleCards = Lines([.. cards.Select(Line)]);
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        // Imported before the second snapshot, so one owned card goes stale
        // with its collection row intact.
        var import = await client.PutAsJsonAsync("/collection", new
        {
            format = "list",
            content = Lines([.. cards.Where(c => c.Owned > 0).Select(c => $"{c.Owned} {c.Name}")]),
        });
        import.EnsureSuccessStatusCode();
        // The second snapshot drops some cards: they go stale, keeping their
        // legality rows, and the boundary must leave them out.
        source.OracleCards = Lines([.. cards.Where(c => !c.GoesStale).Select(Line)]);
        await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded");
        return connectionString;
    }

    private sealed record Seed(
        Guid OracleId,
        string Name,
        string TypeLine,
        string[] ColorIdentity,
        string[] Keywords,
        string CommanderLegality,
        string? OracleText,
        bool GoesStale,
        int Owned,
        BrawlKeys Brawl);

    private static string Name(int i) => $"Index Card {i:00}";

    /// <summary>
    /// The availability cases, by seed index: a basic land (unlimited copy
    /// limit) owned 3, a "seven cards named" card owned 9, an ordinary
    /// singleton owned 4, an owned card that goes stale, and a legal card
    /// nobody owns. Every third other card is owned 1–4; the rest are
    /// unowned.
    /// </summary>
    public const int BasicLand = 15;

    public const int Unowned = 8;

    public const int SevenDwarves = 10;

    public const int Singleton = 9;

    public const int OwnedStale = 22;

    public static Guid OracleIdOf(MaterializedLegality oracle, int i) => oracle.OracleIdOf(Name(i));

    private static List<Seed> Cards()
    {
        var rng = new Random(36);
        var seeds = new List<Seed>();
        for (var i = 0; i < 48; i++)
        {
            var identity = rng.Next(4) == 0
                ? []
                : new[] { "W", "U", "B", "R", "G" }.Where(_ => rng.Next(5) < 2).ToArray();
            seeds.Add(new Seed(
                Guid.NewGuid(),
                Name(i),
                (i % 6) switch
                {
                    0 or 1 => "Legendary Creature — Test",
                    2 => "Creature — Test",
                    3 => i % 12 == 3 ? "Basic Land — Forest" : "Artifact",
                    4 => "Instant",
                    _ => i % 12 == 11 ? "Legendary Planeswalker — Test" : "Legendary Artifact",
                },
                identity,
                i % 7 == 3 ? ["Companion"] : [],
                i % 11 == 5 ? "not_legal" : "legal",
                i switch
                {
                    SevenDwarves => $"A deck can have up to seven cards named {Name(SevenDwarves)}.",
                    _ when i % 13 == 8 => "This card can be your commander.",
                    _ => null,
                },
                GoesStale: i % 9 == 4,
                Owned: i switch
                {
                    BasicLand => 3,
                    SevenDwarves => 9,
                    Singleton => 4,
                    OwnedStale => 2,
                    _ when i % 3 == 0 => 1 + i % 4,
                    _ => 0,
                },
                // The two 100-card pools match and their bans differ;
                // Standard Brawl is a smaller pool, and some records lack
                // its key, as an older snapshot would.
                new BrawlKeys(
                    i % 5 == 1 ? "not_legal" : i % 8 == 7 ? "banned" : "legal",
                    i % 5 == 1 ? "not_legal" : i % 8 == 6 ? "banned" : "legal",
                    i % 16 == 15 ? null : i % 3 == 1 ? "legal" : "not_legal")));
        }

        return seeds;
    }

    private static string Line(Seed seed) => CardLine(
        seed.OracleId,
        seed.Name,
        typeLine: seed.TypeLine,
        oracleText: seed.OracleText,
        keywords: seed.Keywords,
        commanderLegality: seed.CommanderLegality,
        colorIdentity: seed.ColorIdentity,
        brawl: seed.Brawl);

    // Every scope, an unban of each, across four versions; none of these
    // cards goes stale (stale = i % 9 == 4), and the companion-scope bans
    // land on Companion-keyword cards (i % 7 == 3).
    private static readonly string Events =
        $$"""
        [
          {
            "effective_date": "2004-10-04",
            "date_precision": "day",
            "announcement_url": "https://example.org/2004",
            "events": [
              { "card": "{{Name(0)}}", "action": "ban", "scope": "all" },
              { "card": "{{Name(1)}}", "action": "ban", "scope": "commander_only" },
              { "card": "{{Name(2)}}", "action": "ban", "scope": "all" }
            ]
          },
          {
            "effective_date": "2020-04-17",
            "date_precision": "day",
            "announcement_url": "https://example.org/2020",
            "events": [
              { "card": "{{Name(3)}}", "action": "ban", "scope": "companion" },
              { "card": "{{Name(6)}}", "action": "ban", "scope": "all" },
              { "card": "{{Name(2)}}", "action": "unban", "scope": "all" }
            ]
          },
          {
            "effective_date": "2024-01-01",
            "date_precision": "day",
            "announcement_url": "https://example.org/2024",
            "events": [
              { "card": "{{Name(1)}}", "action": "unban", "scope": "commander_only" },
              { "card": "{{Name(12)}}", "action": "ban", "scope": "all" },
              { "card": "{{Name(19)}}", "action": "ban", "scope": "commander_only" }
            ]
          },
          {
            "effective_date": "2025-04-21",
            "date_precision": "day",
            "announcement_url": "https://example.org/2025",
            "events": [
              { "card": "{{Name(18)}}", "action": "ban", "scope": "all" },
              { "card": "{{Name(24)}}", "action": "ban", "scope": "companion" },
              { "card": "{{Name(30)}}", "action": "ban", "scope": "all" }
            ]
          }
        ]
        """;
}

/// <summary>
/// The truth the index tests check candidates against, read straight from
/// the materialized <c>legality</c> table, the card rows, and the collection:
/// per (format, banlist version, role) status, staleness, color identity, copy
/// limit, and owned quantity. Also the fuzz
/// vocabulary — the corpus's own names, type words, and keywords — so
/// fuzzed filters mostly hit something.
/// </summary>
internal sealed class MaterializedLegality
{
    // A Brawl row has no banlist version: its version key is null.
    private readonly Dictionary<(Guid OracleId, Format Format, string? Version, Role Role), LegalityStatus> _status;
    private readonly Dictionary<Guid, (string Name, string[] Identity, bool Stale, int? CopyLimit)> _cards;
    private readonly Dictionary<Guid, int> _owned;
    private readonly string[] _words;

    private MaterializedLegality(
        Dictionary<(Guid, Format, string?, Role), LegalityStatus> status,
        Dictionary<Guid, (string, string[], bool, int?)> cards,
        Dictionary<Guid, int> owned,
        IReadOnlyList<string> versions)
    {
        _status = status;
        _cards = cards;
        _owned = owned;
        Owned = owned.Keys.ToHashSet();
        Versions = versions;
        _words = [.. cards.Values.Select(c => c.Item1)
            .Concat(["Creature", "Artifact", "Instant", "Legendary", "Companion", "Index Card", "Card 1", "zzz"])];
    }

    /// <summary>Wire names, oldest first; the last is the latest.</summary>
    public IReadOnlyList<string> Versions { get; }

    public string Latest => Versions[^1];

    public IReadOnlyCollection<Guid> OracleIds => _cards.Keys;

    public static async Task<MaterializedLegality> LoadAsync(string connectionString)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        var versions = await db.BanlistVersions.OrderBy(v => v.EffectiveDate)
            .Select(v => new { v.Id, v.EffectiveDate }).ToListAsync();
        var wire = versions.ToDictionary(v => v.Id, v => v.EffectiveDate.ToString("O"));
        var cards = await db.Cards
            .Select(c => new { c.Id, c.OracleId, c.Name, c.ColorIdentity, c.Stale, c.CopyLimit })
            .ToListAsync();
        var byId = cards.ToDictionary(c => c.Id, c => c.OracleId);
        var rows = await db.Legalities.Select(l => new { l.CardId, l.Format, l.BanlistVersionId, l.Role, l.Status }).ToListAsync();
        var owned = await db.CollectionCards.ToDictionaryAsync(o => o.OracleId, o => o.Quantity);
        return new MaterializedLegality(
            rows.ToDictionary(
                r => (byId[r.CardId], r.Format, r.BanlistVersionId is { } id ? wire[id] : null, r.Role),
                r => r.Status),
            cards.ToDictionary(c => c.OracleId, c => (c.Name, c.ColorIdentity, c.Stale, c.CopyLimit)),
            owned,
            [.. versions.Select(v => wire[v.Id])]);
    }

    public Guid OracleIdOf(string name) => _cards.Single(c => c.Value.Name == name).Key;

    /// <summary>The owned oracle ids — what collection scope must keep to.</summary>
    public IReadOnlySet<Guid> Owned { get; }

    /// <summary>Scoped availability: min(copy limit, owned quantity); null when unowned.</summary>
    public int? Available(Guid oracleId)
        => _owned.TryGetValue(oracleId, out var quantity)
            ? Math.Min(_cards[oracleId].CopyLimit ?? int.MaxValue, quantity)
            : null;

    /// <summary>The legal, non-stale set for a (format, version, role) — what
    /// <c>legal_candidates</c> must return. A Brawl format has no version: pass null.</summary>
    public IReadOnlySet<Guid> Legal(string? version, Role role, Format format = Format.Commander)
        => _status
            .Where(s => s.Key.Format == format && s.Key.Version == version && s.Key.Role == role
                && s.Value == LegalityStatus.Legal)
            .Select(s => s.Key.OracleId)
            .Where(id => !_cards[id].Stale)
            .ToHashSet();

    public string[] IdentityOf(Guid oracleId) => _cards[oracleId].Identity;

    /// <summary>Why <paramref name="oracleId"/> may not be a candidate for the query, or null when it may.</summary>
    public string? Objection(
        Guid oracleId, Format format, string? version, Role role, string[]? deckIdentity, bool collectionScope)
    {
        if (!_cards.TryGetValue(oracleId, out var card))
        {
            return $"{oracleId} is not a corpus card";
        }

        if (card.Stale)
        {
            return $"{card.Name} ({oracleId}) is stale";
        }

        LegalityStatus? status = _status.TryGetValue((oracleId, format, version, role), out var found) ? found : null;
        if (status != LegalityStatus.Legal)
        {
            return $"{card.Name} ({oracleId}) is not legal as {role} in {format} at {version ?? "the current state"}: legality status "
                + (status?.ToString() ?? "none");
        }

        if (deckIdentity is not null && card.Identity.Except(deckIdentity).Any())
        {
            return $"{card.Name} ({oracleId}) identity [{string.Join("", card.Identity)}] escapes the deck "
                + $"identity [{string.Join("", deckIdentity)}]";
        }

        if (collectionScope && !_owned.ContainsKey(oracleId))
        {
            return $"{card.Name} ({oracleId}) is not owned";
        }

        return null;
    }

    public string Word(Random rng) => _words[rng.Next(_words.Length)];

    public Guid OracleId(Random rng) => _cards.Keys.ElementAt(rng.Next(_cards.Count));
}
