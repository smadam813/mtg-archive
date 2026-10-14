using MtgArchive.Bracket;

namespace MtgArchive.Tests;

/// <summary>
/// The bracket data file readers: the committed spec, snapshot,
/// and curated files parse clean; a malformed file lists its problems
/// and contributes nothing (the banlist-events pattern).
/// </summary>
public sealed class BracketFilesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mtg-archive-bracket-files-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void The_committed_data_files_parse_clean()
    {
        var root = RepoRoot.Find();
        var errors = new List<string>();

        var specs = BracketFiles.ReadSpecs(Path.Combine(root, "data", "bracket-spec"), errors);
        var snapshots = BracketFiles.ReadSnapshots(Path.Combine(root, "data", "game-changers"), errors);
        var lists = BracketFiles.ReadCuratedLists(Path.Combine(root, "data", "curated"), errors);

        Assert.Empty(errors);
        var spec = Assert.Single(specs).Spec;
        Assert.Equal(new DateOnly(2025, 10, 21), spec.Date);
        Assert.Equal(["game_changers_count", "mass_land_denial", "chained_extra_turns"], spec.Barometers.Select(b => b.Name));
        Assert.Equal([1, 2, 3, 4, 5], spec.Brackets);
        Assert.Equal(3, spec["game_changers_count"].Thresholds[3].MaxCount);
        Assert.Null(spec["game_changers_count"].Thresholds[4].MaxCount);
        Assert.False(spec["mass_land_denial"].Thresholds[3].Allowed);
        Assert.True(spec["chained_extra_turns"].Thresholds[2].CardsAllowed);
        Assert.False(spec["chained_extra_turns"].Thresholds[2].ChainingAllowed);
        Assert.Equal(4, snapshots.Count);
        Assert.Equal(new DateOnly(2026, 2, 9), snapshots[^1].Date);
        Assert.Contains("Demonic Tutor", snapshots[^1].Cards);
        Assert.Equal(["mass_land_denial", "chained_extra_turns"], lists.Select(l => l.Barometer));
        Assert.Contains("Armageddon", lists[0].Cards);
    }

    [Theory]
    [InlineData("2025-10-21.json", "not json", "bracket-spec/2025-10-21.json: invalid JSON")]
    [InlineData("2025-10-21.json", """{ "date": "2025-10-22", "source": "s", "barometers": [] }""", "$.barometers: must not be empty")]
    [InlineData("october.json", "{}", "bracket-spec/october.json: the file name must be a yyyy-MM-dd date")]
    public void A_malformed_spec_file_lists_its_problems(string file, string content, string expected)
    {
        File.WriteAllText(Path.Combine(_dir, file), content);
        var errors = new List<string>();

        var specs = BracketFiles.ReadSpecs(_dir, errors);

        Assert.Empty(specs);
        Assert.Contains(errors, e => e.Contains(expected));
    }

    [Fact]
    public void A_spec_missing_a_barometer_or_with_a_date_mismatch_lists_its_problems()
    {
        var committed = File.ReadAllText(Path.Combine(RepoRoot.Find(), "data", "bracket-spec", "2025-10-21.json"));
        File.WriteAllText(Path.Combine(_dir, "2025-10-22.json"), committed);
        File.WriteAllText(Path.Combine(_dir, "2025-10-21.json"), committed.Replace("\"mass_land_denial\"", "\"tutors\""));
        var errors = new List<string>();

        var specs = BracketFiles.ReadSpecs(_dir, errors);

        Assert.Empty(specs);
        Assert.Contains(errors, e => e.Contains("'date' is 2025-10-21 but the file name says 2025-10-22"));
        Assert.Contains(errors, e => e.Contains("'tutors' is not one of"));
        Assert.Contains(errors, e => e.Contains("exactly one barometer named 'mass_land_denial' is required"));
    }

    [Theory]
    [InlineData("""{ "date": "2026-02-09", "source": "s", "cards": ["Sol Ring", "Sol Ring"] }""", "duplicate entry 'Sol Ring'")]
    [InlineData("""{ "date": "2026-02-10", "source": "s", "cards": [] }""", "is 2026-02-10 but the file name says 2026-02-09")]
    [InlineData("""{ "date": "2026-02-09", "cards": [] }""", "missing required property 'source'")]
    [InlineData("""{ "date": "2026-02-09", "source": "s", "cards": [], "notes": "x" }""", "unknown property 'notes'")]
    public void A_malformed_snapshot_file_lists_its_problems(string content, string expected)
    {
        File.WriteAllText(Path.Combine(_dir, "2026-02-09.json"), content);
        var errors = new List<string>();

        var snapshots = BracketFiles.ReadSnapshots(_dir, errors);

        Assert.Empty(snapshots);
        Assert.Contains(errors, e => e.Contains(expected));
    }

    [Fact]
    public void A_missing_curated_list_names_the_setting()
    {
        var errors = new List<string>();

        var lists = BracketFiles.ReadCuratedLists(_dir, errors);

        Assert.Empty(lists);
        Assert.Contains(errors, e => e.StartsWith("mass-land-denial.json: not found"));
        Assert.Contains(errors, e => e.StartsWith("chained-extra-turns.json: not found"));
    }

    [Fact]
    public void A_missing_directory_names_the_setting()
    {
        var errors = new List<string>();

        BracketFiles.ReadSpecs(Path.Combine(_dir, "nope"), errors);
        BracketFiles.ReadSnapshots(Path.Combine(_dir, "nope"), errors);

        Assert.Contains(errors, e => e.Contains("check Ingest:BracketSpecDir"));
        Assert.Contains(errors, e => e.Contains("check Ingest:GameChangersDir"));
    }
}
