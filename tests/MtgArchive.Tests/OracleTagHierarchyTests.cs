using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MtgArchive.Embedding;
using MtgArchive.Ingest;
using MtgArchive.Rules;
using static MtgArchive.Tests.IngestHarness;

namespace MtgArchive.Tests;

/// <summary>
/// The oracle-tags reader closes each tagging over Scryfall's tag ancestry:
/// a card tagged with a leaf carries every ancestor as an
/// inherited tag, direct taggings keep their weight, and a malformed
/// hierarchy is reported without failing the snapshot. The ingest test
/// follows the provenance into storage and the upsert diff.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OracleTagHierarchyTests(PostgresFixture postgres)
{
    private static readonly string Pillowfort = Guid.NewGuid().ToString();
    private static readonly string TaxAttack = Guid.NewGuid().ToString();
    private static readonly string Removal = Guid.NewGuid().ToString();
    private static readonly string SpotRemoval = Guid.NewGuid().ToString();
    private static readonly string RemovalCreature = Guid.NewGuid().ToString();
    private static readonly string DoomBlade = Guid.NewGuid().ToString();

    [Fact]
    public void A_child_tagging_carries_its_parent_as_inherited()
    {
        var propaganda = Guid.NewGuid();
        var sphere = Guid.NewGuid();
        var (tags, errors, anomalies) = Read(
            TagLine("pillowfort", Pillowfort, [], (sphere, "median")),
            TagLine("tax attack", TaxAttack, [Pillowfort], (propaganda, "very_strong")));

        Assert.Empty(errors);
        Assert.Empty(anomalies);
        Assert.Equal(
            [new SnapshotTag("pillowfort", true, null), new SnapshotTag("tax attack", false, "very_strong")],
            tags[propaganda].OrderBy(t => t.Label, StringComparer.Ordinal));
        Assert.Equal([new SnapshotTag("pillowfort", false, "median")], tags[sphere]);
    }

    [Fact]
    public void A_chain_yields_every_ancestor_once_even_when_parents_share_one()
    {
        var doomBlade = Guid.NewGuid();
        var (tags, errors, anomalies) = Read(
            TagLine("removal", Removal, []),
            TagLine("spot removal", SpotRemoval, [Removal]),
            TagLine("removal-creature", RemovalCreature, [Removal]),
            TagLine("doom blade", DoomBlade, [SpotRemoval, RemovalCreature], (doomBlade, "median")));

        Assert.Empty(errors);
        Assert.Empty(anomalies);
        Assert.Equal(
            ["doom blade", "removal", "removal-creature", "spot removal"],
            tags[doomBlade].Select(t => t.Label).Order(StringComparer.Ordinal));
        Assert.Equal(["doom blade"], tags[doomBlade].Where(t => !t.Inherited).Select(t => t.Label));
    }

    [Fact]
    public void A_direct_tagging_wins_over_the_same_tag_inherited()
    {
        var card = Guid.NewGuid();
        var (tags, _, _) = Read(
            TagLine("tax attack", TaxAttack, [Pillowfort], (card, "median")),
            TagLine("pillowfort", Pillowfort, [], (card, "strong")));

        Assert.Equal(new SnapshotTag("pillowfort", false, "strong"), Assert.Single(tags[card], t => t.Label == "pillowfort"));
    }

    [Fact]
    public void A_cycle_or_unknown_parent_is_reported_and_the_tags_still_land()
    {
        var card = Guid.NewGuid();
        var a = Guid.NewGuid().ToString();
        var b = Guid.NewGuid().ToString();
        var (tags, errors, anomalies) = Read(
            TagLine("alpha", a, [b], (card, "median")),
            TagLine("beta", b, [a]),
            TagLine("gamma", null, ["not-a-tag"], (card, "median")));

        Assert.Empty(errors);
        Assert.Equal(["alpha", "beta", "gamma"], tags[card].Select(t => t.Label).Order(StringComparer.Ordinal));
        Assert.Contains(anomalies, a => a.Contains("cycle") && a.Contains("alpha"));
        Assert.Contains(anomalies, a => a.Contains("unknown parent") && a.Contains("gamma"));
    }

    [Fact]
    public void An_unknown_parent_is_one_anomaly_however_many_tags_name_it()
    {
        var (_, errors, anomalies) = Read(
            TagLine("alpha", null, ["not-a-tag"]),
            TagLine("beta", null, ["not-a-tag"]));

        Assert.Empty(errors);
        var anomaly = Assert.Single(anomalies);
        Assert.Contains("line 1", anomaly);
        Assert.Contains("alpha", anomaly);
        Assert.Contains("1 more tag(s)", anomaly);
    }

    [Fact]
    public void A_duplicate_id_or_malformed_parent_ids_is_a_validation_error()
    {
        var (_, errors, _) = Read(
            TagLine("alpha", Pillowfort, []),
            TagLine("beta", Pillowfort, []),
            """{"label": "gamma", "parent_ids": "oops", "taggings": []}""");

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.StartsWith("oracle-tags line 2: duplicate id"));
        Assert.Contains("oracle-tags line 3: missing or invalid parent_ids", errors);
    }

    [Fact]
    public void Aliases_map_to_their_label_and_never_land_on_a_card()
    {
        var ephemerate = Guid.NewGuid();
        var (tags, errors, anomalies) = ReadAll(
            TagLine("flicker", Pillowfort, [], ["blink"]),
            TagLine("flicker-creature", TaxAttack, [Pillowfort], null, (ephemerate, "median")),
            TagLine("sweeper", Removal, [], ["wipe", "boardwipe", "mass removal"]));

        Assert.Empty(errors);
        Assert.Empty(anomalies);
        Assert.Equal(
            [("blink", "flicker"), ("boardwipe", "sweeper"), ("mass removal", "sweeper"), ("wipe", "sweeper")],
            tags.Aliases.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => (a.Key, a.Value)));
        Assert.Equal(
            ["flicker", "flicker-creature"],
            tags.ByOracleId[ephemerate].Select(t => t.Label).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_alias_that_is_a_label_is_dropped_and_a_second_claim_is_an_anomaly_the_first_wins()
    {
        var (tags, errors, anomalies) = ReadAll(
            TagLine("counterspell", Removal, [], ["counter", "removal"]),
            TagLine("removal", SpotRemoval, [], ["counter"]),
            TagLine("spot removal", RemovalCreature, [], ["counter"]));

        Assert.Empty(errors);
        Assert.Equal([("counter", "counterspell")], tags.Aliases.Select(a => (a.Key, a.Value)));
        Assert.Equal(
            [
                "oracle-tags line 2: alias 'counter' on tag 'removal' already names tag 'counterspell'",
                "oracle-tags line 3: alias 'counter' on tag 'spot removal' already names tag 'counterspell'",
            ],
            anomalies);
    }

    [Fact]
    public void Malformed_aliases_are_a_validation_error()
    {
        var (_, errors, _) = ReadAll("""{"label": "alpha", "aliases": "oops", "taggings": []}""");

        Assert.Equal(["oracle-tags line 1: missing or invalid aliases"], errors);
    }

    [Fact]
    public async Task Ingest_replaces_the_alias_table_and_an_alias_change_alone_touches_no_card()
    {
        var ephemerate = Guid.NewGuid();
        var wrath = Guid.NewGuid();
        var source = new FakeBulkSource
        {
            OracleCards = Lines(
                CardLine(ephemerate, "Ephemerate", typeLine: "Instant", manaCost: "{W}", cmc: 1),
                CardLine(wrath, "Wrath of God", typeLine: "Sorcery", manaCost: "{2}{W}{W}", cmc: 4)),
            Rulings = "",
            OracleTags = Lines(
                TagLine("flicker", Pillowfort, [], ["blink"]),
                TagLine("flicker-creature", TaxAttack, [Pillowfort], null, (ephemerate, "median")),
                TagLine("sweeper", Removal, [], ["wipe", "boardwipe"], (wrath, "strong")),
                TagLine("mass removal", SpotRemoval, [], ["wipe"])),
        };
        var ollama = new FakeOllama();
        var connectionString = await postgres.CreateDatabaseAsync($"tag_alias_{Guid.NewGuid():N}");
        using var app = new ArchiveApp(autoMigrate: true, connectionString, services =>
        {
            services.RemoveAll<IScryfallBulkSource>();
            services.AddSingleton<IScryfallBulkSource>(source);
            services.RemoveAll<ICrSource>();
            services.AddSingleton<ICrSource>(new FakeCrSource());
            services.AddHttpClient<OllamaEmbedder>().ConfigurePrimaryHttpMessageHandler(() => ollama);
        });
        using var client = app.CreateClient();

        var report = (await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded")).GetProperty("report");

        Assert.Equal(
            ["oracle-tags line 4: alias 'wipe' on tag 'mass removal' already names tag 'sweeper'"],
            report.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(
                [("blink", "flicker"), ("boardwipe", "sweeper"), ("wipe", "sweeper")],
                (await db.TagAliases.OrderBy(a => a.Alias).ToListAsync()).Select(a => (a.Alias, a.Label)));
        }

        source.OracleTags = Lines(
            TagLine("flicker", Pillowfort, [], ["blink", "flash in"]),
            TagLine("flicker-creature", TaxAttack, [Pillowfort], null, (ephemerate, "median")),
            TagLine("sweeper", Removal, [], ["boardwipe"], (wrath, "strong")));
        ollama.Requests.Clear();
        report = (await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded")).GetProperty("report");

        Assert.Equal(0, report.GetProperty("changed_cards").GetInt32());
        Assert.Empty(report.GetProperty("errors").EnumerateArray());
        Assert.DoesNotContain(ollama.Requests, r => r.Path == "/api/embed");
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(
                [("blink", "flicker"), ("boardwipe", "sweeper"), ("flash in", "flicker")],
                (await db.TagAliases.OrderBy(a => a.Alias).ToListAsync()).Select(a => (a.Alias, a.Label)));
            Assert.Equal(0, await db.CardFaceDocuments.CountAsync(d => d.Embedding == null));
        }
    }

    [Fact]
    public async Task Ingest_stores_provenance_and_an_inherited_change_counts_without_a_re_embed()
    {
        var propaganda = Guid.NewGuid();
        var sphere = Guid.NewGuid();
        var source = new FakeBulkSource
        {
            OracleCards = Lines(
                CardLine(propaganda, "Propaganda", typeLine: "Enchantment", manaCost: "{2}{U}", cmc: 3),
                CardLine(sphere, "Sphere of Safety", typeLine: "Enchantment", manaCost: "{4}{W}", cmc: 5)),
            Rulings = "",
            OracleTags = Lines(
                TagLine("pillowfort", Pillowfort, [], (sphere, "median")),
                TagLine("tax attack", TaxAttack, [Pillowfort], (propaganda, "very_strong")),
                TagLine("gamma", null, ["not-a-tag"], (sphere, "median"))),
        };
        var ollama = new FakeOllama();
        var connectionString = await postgres.CreateDatabaseAsync($"tag_hierarchy_{Guid.NewGuid():N}");
        using var app = new ArchiveApp(autoMigrate: true, connectionString, services =>
        {
            services.RemoveAll<IScryfallBulkSource>();
            services.AddSingleton<IScryfallBulkSource>(source);
            services.RemoveAll<ICrSource>();
            services.AddSingleton<ICrSource>(new FakeCrSource());
            services.AddHttpClient<OllamaEmbedder>().ConfigurePrimaryHttpMessageHandler(() => ollama);
        });
        using var client = app.CreateClient();

        var report = (await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded")).GetProperty("report");

        var error = Assert.Single(report.GetProperty("errors").EnumerateArray()).GetString()!;
        Assert.Contains("unknown parent", error);
        Assert.Contains("gamma", error);
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            var tags = await db.CardOracleTags.Join(db.Cards, t => t.CardId, c => c.Id, (t, c) => new { c.OracleId, t.Tag, t.Inherited, t.Weight })
                .ToListAsync();
            Assert.Equal(
                [(propaganda, "pillowfort", true, null), (propaganda, "tax attack", false, "very_strong")],
                tags.Where(t => t.OracleId == propaganda).OrderBy(t => t.Tag).Select(t => (t.OracleId, t.Tag, t.Inherited, t.Weight)));
            Assert.Equal(
                [(sphere, "gamma", false, "median"), (sphere, "pillowfort", false, "median")],
                tags.Where(t => t.OracleId == sphere).OrderBy(t => t.Tag).Select(t => (t.OracleId, t.Tag, t.Inherited, t.Weight)));
            Assert.Equal(0, await db.CardFaceDocuments.CountAsync(d => d.Embedding == null));
        }

        // Tax attack loses its parent: Propaganda's inherited set changes, but
        // documents embed direct tags only, so nothing re-embeds.
        source.OracleTags = Lines(
            TagLine("pillowfort", Pillowfort, [], (sphere, "median")),
            TagLine("tax attack", TaxAttack, [], (propaganda, "very_strong")),
            TagLine("gamma", null, ["not-a-tag"], (sphere, "median")));
        ollama.Requests.Clear();
        report = (await WaitForCompletionAsync(client, await TriggerAsync(client), "succeeded")).GetProperty("report");

        Assert.Equal(1, report.GetProperty("changed_cards").GetInt32());
        Assert.DoesNotContain(ollama.Requests, r => r.Path == "/api/embed");
        await using (var db = PostgresFixture.CreateContext(connectionString))
        {
            Assert.Equal(0, await db.CardFaceDocuments.CountAsync(d => d.Embedding == null));
            Assert.Empty(await db.CardOracleTags.Where(t => t.Inherited).ToListAsync());
        }
    }

    private static (IReadOnlyDictionary<Guid, List<SnapshotTag>> Tags, List<string> Errors, List<string> Anomalies) Read(
        params string[] lines)
    {
        var (tags, errors, anomalies) = ReadAll(lines);
        return (tags.ByOracleId, errors, anomalies);
    }

    private static (SnapshotTags Tags, List<string> Errors, List<string> Anomalies) ReadAll(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"oracle-tags-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, Lines(lines));
        try
        {
            var errors = new List<string>();
            var anomalies = new List<string>();
            return (ScryfallSnapshotReader.ReadOracleTags(path, errors, anomalies), errors, anomalies);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
