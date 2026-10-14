using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MtgArchive.Data;

namespace MtgArchive.Ingest;

/// <summary>
/// Reads the local popularity exports (GLOSSARY.md "Popularity
/// export", ADR 0012). The layout is one directory per format under
/// <c>data/popularity/</c>, named by the format's wire name, holding exactly
/// one Untapped.gg card-data CSV named <c>&lt;window-start&gt;_&lt;window-end&gt;.csv</c>
/// (<c>yyyy-MM-dd</c> dates). Only Brawl and Competitive Brawl have an
/// export. No directory means no export, and the EDHREC proxy orders that
/// format. A malformed layout or file is a hard ingest failure, never a
/// warning: every problem goes to the caller's error list.
/// </summary>
public static class PopularityExports
{
    /// <summary>The formats that read a popularity export.</summary>
    public static readonly Format[] Formats = [Format.Brawl, Format.CompetitiveBrawl];

    /// <summary>Untapped writes an en dash for a blank value.</summary>
    private const string Blank = "–";

    private const string NameColumn = "name";
    private const string PopularityColumn = "Popularity";
    private const string InMainDeckColumn = "In Main Deck";
    private const string TotalGamesColumn = "Total Games";

    private static readonly string[] RequiredColumns = [NameColumn, PopularityColumn, InMainDeckColumn, TotalGamesColumn];

    /// <summary>Relative paths resolve against the content root, so the dev
    /// loop finds the directory wherever the host starts.</summary>
    public static string ResolveDir(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Ingest:PopularityDir"] ?? Path.Combine("data", "popularity");
        return Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
    }

    /// <summary>
    /// The files the corpus-state gate hashes, keyed by their identity under
    /// <c>data/</c> (<c>popularity/brawl/2026-09-29_2026-10-04.csv</c>). The
    /// list holds every file in a format directory, so a second file reads as
    /// a change and the load then fails on it.
    /// </summary>
    public static IEnumerable<(string Key, string Path)> Files(string directory)
    {
        foreach (var format in Formats)
        {
            var name = ArchiveDbContext.WireName(format);
            foreach (var path in FormatFiles(Path.Combine(directory, name)))
            {
                yield return ($"popularity/{name}/{Path.GetFileName(path)}", path);
            }
        }
    }

    /// <summary>Parses the corpus-state key of an export back into its
    /// format and window. Null for a key that is not an export.</summary>
    public static PopularityExportStamp? ParseKey(string key, string sha256)
    {
        var parts = key.Split('/');
        var format = parts.Length == 3 && parts[0] == "popularity"
            ? Formats.Cast<Format?>().FirstOrDefault(f => ArchiveDbContext.WireName(f!.Value) == parts[1])
            : null;
        if (format is null || ParseWindow(parts[2]) is not { } window)
        {
            return null;
        }

        return new PopularityExportStamp(format.Value, window.Start, window.End, sha256);
    }

    /// <summary>Reads every format's export. A format with no directory is
    /// absent from the result.</summary>
    public static IReadOnlyList<PopularityExport> Read(string directory, List<string> errors)
    {
        var exports = new List<PopularityExport>();
        foreach (var format in Formats)
        {
            var name = ArchiveDbContext.WireName(format);
            var formatDir = Path.Combine(directory, name);
            if (!Directory.Exists(formatDir))
            {
                continue;
            }

            var files = FormatFiles(formatDir).ToList();
            if (files.Count != 1)
            {
                errors.Add($"popularity/{name}: the directory must hold exactly one export, found {files.Count}");
                continue;
            }

            var path = files[0];
            var file = $"popularity/{name}/{Path.GetFileName(path)}";
            if (ParseWindow(Path.GetFileName(path)) is not { } window)
            {
                errors.Add($"{file}: the file name must be <window-start>_<window-end>.csv with yyyy-MM-dd dates, start not after end");
                continue;
            }

            var bytes = File.ReadAllBytes(path);
            var rows = ParseRows(file, bytes, errors);
            if (rows is not null)
            {
                exports.Add(new PopularityExport(format, window.Start, window.End, Convert.ToHexString(SHA256.HashData(bytes)), rows));
            }
        }

        return exports;
    }

    private static IEnumerable<string> FormatFiles(string formatDir)
        => Directory.Exists(formatDir)
            ? Directory.EnumerateFiles(formatDir).Order(StringComparer.Ordinal)
            : [];

    private static (DateOnly Start, DateOnly End)? ParseWindow(string fileName)
    {
        if (!fileName.EndsWith(".csv", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = fileName[..^4].Split('_');
        return parts.Length == 2
            && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            && DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            && start <= end
                ? (start, end)
                : null;
    }

    /// <summary>The rows with stats, each with its seat. A row with no
    /// stats (every value blank) is not a row of the export. Null when the
    /// file has errors.</summary>
    private static List<PopularityExportRow>? ParseRows(string file, byte[] bytes, List<string> errors)
    {
        var errorCount = errors.Count;
        var records = Csv.Parse(new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿'));
        if (records.Count == 0)
        {
            errors.Add($"{file}: the file is empty");
            return null;
        }

        var header = records[0];
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Count; i++)
        {
            columns.TryAdd(header[i], i);
        }

        foreach (var missing in RequiredColumns.Where(c => !columns.ContainsKey(c)))
        {
            errors.Add($"{file}: the header has no '{missing}' column");
        }

        if (errors.Count > errorCount)
        {
            return null;
        }

        var rows = new List<PopularityExportRow>();
        for (var line = 1; line < records.Count; line++)
        {
            var record = records[line];
            if (record.Count != header.Count)
            {
                errors.Add($"{file}: record {line + 1} has {record.Count} values, the header has {header.Count}");
                continue;
            }

            var hasStats = record.Where((_, i) => i != columns[NameColumn] && header[i] != "id").Any(v => !IsBlank(v));
            if (!hasStats)
            {
                continue;
            }

            var name = record[columns[NameColumn]];
            var popularity = record[columns[PopularityColumn]];
            var inMainDeck = record[columns[InMainDeckColumn]];
            var totalGames = record[columns[TotalGamesColumn]];
            // The seat is in the values, never in the file order.
            Role? seat = !IsBlank(inMainDeck) ? Role.Main : !IsBlank(popularity) ? Role.Commander : null;
            if (seat is null)
            {
                errors.Add($"{file}: record {line + 1} ('{name}') has stats but neither an In Main Deck nor a Popularity value");
                continue;
            }

            var share = ParsePercent(popularity);
            var games = ParseCount(totalGames);
            if ((!IsBlank(popularity) && share is null) || (!IsBlank(totalGames) && games is null))
            {
                errors.Add($"{file}: record {line + 1} ('{name}') has a malformed Popularity or Total Games value");
                continue;
            }

            rows.Add(new PopularityExportRow(name, seat.Value, share, games));
        }

        return errors.Count > errorCount ? null : rows;
    }

    private static bool IsBlank(string value) => value.Length == 0 || value == Blank;

    private static decimal? ParsePercent(string value)
        => value.EndsWith('%')
            && decimal.TryParse(value[..^1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var share)
                ? share
                : null;

    private static int? ParseCount(string value)
        => int.TryParse(value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var count) ? count : null;

    /// <summary>A minimal RFC 4180 reader: comma-separated fields, double
    /// quotes around a field, a doubled quote inside one, CRLF or LF
    /// records.</summary>
    private static class Csv
    {
        public static List<List<string>> Parse(string text)
        {
            var records = new List<List<string>>();
            var record = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            var any = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        field.Append(c);
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        quoted = true;
                        any = true;
                        break;
                    case ',':
                        record.Add(field.ToString());
                        field.Clear();
                        any = true;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        if (any || field.Length > 0)
                        {
                            record.Add(field.ToString());
                            records.Add(record);
                        }

                        record = [];
                        field.Clear();
                        any = false;
                        break;
                    default:
                        field.Append(c);
                        any = true;
                        break;
                }
            }

            if (any || field.Length > 0)
            {
                record.Add(field.ToString());
                records.Add(record);
            }

            return records;
        }
    }
}

/// <summary>One format's export: its window, the SHA-256 of the file, and
/// the rows with stats.</summary>
public sealed record PopularityExport(
    Format Format, DateOnly WindowStart, DateOnly WindowEnd, string Sha256, IReadOnlyList<PopularityExportRow> Rows);

/// <summary>One row with stats. <paramref name="Share"/> is the
/// <c>Popularity</c> value in percent.</summary>
public sealed record PopularityExportRow(string Name, Role Seat, decimal? Share, int? TotalGames);

/// <summary>An export as <c>/corpus-state</c> lists it: the format, the
/// window, and the file hash the corpus-state gate stored.</summary>
public sealed record PopularityExportStamp(Format Format, DateOnly WindowStart, DateOnly WindowEnd, string FileSha256);
