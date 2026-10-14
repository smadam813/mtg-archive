using System.Globalization;
using System.Text.Json;
using MtgArchive.Api;

namespace MtgArchive.Bracket;

/// <summary>
/// Reads the bracket data files under the banlist-events pattern. The
/// files are the git-versioned source of truth, they name cards by exact
/// Oracle name, and a malformed file is a hard ingest failure, never a
/// warning. The dated directories (<c>data/bracket-spec/</c>,
/// <c>data/game-changers/</c>) hold one <c>yyyy-MM-dd.json</c> per version.
/// The two curated lists live in <c>data/curated/</c>. Every problem goes
/// to the caller's error list; a file with errors contributes nothing.
/// </summary>
public static class BracketFiles
{
    public const string MassLandDenialFile = "mass-land-denial.json";
    public const string ChainedExtraTurnsFile = "chained-extra-turns.json";

    private static readonly string[] SnapshotKeys = ["date", "source", "cards"];
    private static readonly string[] CuratedKeys = ["source", "notes", "cards"];

    /// <summary>Relative paths resolve against the content root, so the dev
    /// loop finds the directories wherever the host was launched from.</summary>
    public static string ResolveSpecDir(IConfiguration configuration, IHostEnvironment environment)
        => Resolve(configuration["Ingest:BracketSpecDir"] ?? Path.Combine("data", "bracket-spec"), environment);

    public static string ResolveGameChangersDir(IConfiguration configuration, IHostEnvironment environment)
        => Resolve(configuration["Ingest:GameChangersDir"] ?? Path.Combine("data", "game-changers"), environment);

    /// <summary>The curated lists directory (<c>data/curated/</c>). Relative
    /// paths resolve against the content root.</summary>
    public static string ResolveCuratedDir(IConfiguration configuration, IHostEnvironment environment)
        => Resolve(configuration["Ingest:CuratedDir"] ?? Path.Combine("data", "curated"), environment);

    private static string Resolve(string configured, IHostEnvironment environment)
        => Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);

    /// <summary>One spec file: the parsed spec and the document verbatim.</summary>
    public sealed record SpecFile(BracketSpec Spec, string Document);

    /// <summary>One Game Changers snapshot file.</summary>
    public sealed record SnapshotFile(DateOnly Date, string Source, IReadOnlyList<string> Cards);

    /// <summary>One curated barometer list file.</summary>
    public sealed record CuratedList(string Barometer, string Source, IReadOnlyList<string> Cards);

    /// <summary>
    /// The files the corpus-state gate hashes, keyed by their identity
    /// under <c>data/</c>. A missing directory lists nothing; the load then
    /// fails on it, and the gate sees the directory's first file as a change.
    /// </summary>
    public static IEnumerable<(string Key, string Path)> CuratedFiles(
        string specDir, string gameChangersDir, string curatedDir)
    {
        foreach (var path in DatedFiles(specDir))
        {
            yield return ($"bracket-spec/{Path.GetFileName(path)}", path);
        }

        foreach (var path in DatedFiles(gameChangersDir))
        {
            yield return ($"game-changers/{Path.GetFileName(path)}", path);
        }

        yield return ($"curated/{MassLandDenialFile}", Path.Combine(curatedDir, MassLandDenialFile));
        yield return ($"curated/{ChainedExtraTurnsFile}", Path.Combine(curatedDir, ChainedExtraTurnsFile));
    }

    public static IReadOnlyList<SpecFile> ReadSpecs(string directory, List<string> errors)
    {
        var specs = new List<SpecFile>();
        foreach (var (file, date, path) in EnumerateDated("bracket-spec", directory, "Ingest:BracketSpecDir", errors))
        {
            var json = File.ReadAllText(path);
            var spec = BracketSpec.Parse(file, json, errors);
            if (spec is null)
            {
                continue;
            }

            if (spec.Date != date)
            {
                errors.Add($"{file}: 'date' is {spec.Date:yyyy-MM-dd} but the file name says {date:yyyy-MM-dd}");
                continue;
            }

            specs.Add(new SpecFile(spec, json));
        }

        return specs;
    }

    public static IReadOnlyList<SnapshotFile> ReadSnapshots(string directory, List<string> errors)
    {
        var snapshots = new List<SnapshotFile>();
        foreach (var (file, date, path) in EnumerateDated("game-changers", directory, "Ingest:GameChangersDir", errors))
        {
            var read = ReadCardFile(file, path, SnapshotKeys, errors, (shape, root) =>
            {
                if (BracketSpec.ParseDate(shape, root, "$", "date") is { } declared && declared != date)
                {
                    shape.Fail("$.date", $"is {declared:yyyy-MM-dd} but the file name says {date:yyyy-MM-dd}");
                }
            });
            if (read is { } snapshot)
            {
                snapshots.Add(new SnapshotFile(date, snapshot.Source, snapshot.Cards));
            }
        }

        return snapshots;
    }

    /// <summary>Both curated lists, keyed to the barometer each one feeds.</summary>
    public static IReadOnlyList<CuratedList> ReadCuratedLists(string directory, List<string> errors)
    {
        var lists = new List<CuratedList>();
        foreach (var (barometer, file) in new[]
                 {
                     (BracketSpec.MassLandDenial, MassLandDenialFile),
                     (BracketSpec.ChainedExtraTurns, ChainedExtraTurnsFile),
                 })
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path))
            {
                errors.Add($"{file}: not found at {path} — check Ingest:CuratedDir");
                continue;
            }

            var read = ReadCardFile(file, path, CuratedKeys, errors,
                (shape, root) => shape.String(root, "$", "notes", required: false));
            if (read is { } list)
            {
                lists.Add(new CuratedList(barometer, list.Source, list.Cards));
            }
        }

        return lists;
    }

    /// <summary>One object file with <c>source</c> and <c>cards</c>; <paramref name="more"/>
    /// reads the keys that differ. Null when the file added an error.</summary>
    private static (string Source, List<string> Cards)? ReadCardFile(
        string file, string path, string[] keys, List<string> errors, Action<JsonShape, JsonElement> more)
    {
        var before = errors.Count;
        using var document = JsonShape.Parse(file, File.ReadAllText(path), errors);
        if (document is null)
        {
            return null;
        }

        var shape = new JsonShape(file, errors);
        var root = document.RootElement;
        if (!shape.IsObject(root, "$"))
        {
            return null;
        }

        shape.OnlyKeys(root, "$", keys, isRoot: true);
        var source = shape.String(root, "$", "source");
        var cards = ReadCards(shape, root);
        more(shape, root);
        return errors.Count == before && source is not null ? (source, cards) : null;
    }

    /// <summary>A <c>cards</c> array of unique, non-empty exact Oracle names.</summary>
    private static List<string> ReadCards(JsonShape shape, JsonElement root)
    {
        var names = shape.StringArray(root, "$", "cards");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!seen.Add(name))
            {
                shape.Fail("$.cards", $"duplicate entry '{name}'");
            }
        }

        return names;
    }

    private static IEnumerable<(string File, DateOnly Date, string Path)> EnumerateDated(
        string kind, string directory, string setting, List<string> errors)
    {
        if (!Directory.Exists(directory))
        {
            errors.Add($"{kind}: directory not found at {directory} — check {setting}");
            yield break;
        }

        foreach (var path in DatedFiles(directory))
        {
            var file = $"{kind}/{Path.GetFileName(path)}";
            var stem = Path.GetFileNameWithoutExtension(path);
            if (!DateOnly.TryParseExact(stem, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                errors.Add($"{file}: the file name must be a yyyy-MM-dd date");
                continue;
            }

            yield return (file, date, path);
        }
    }

    private static IEnumerable<string> DatedFiles(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal)
            : [];
}
