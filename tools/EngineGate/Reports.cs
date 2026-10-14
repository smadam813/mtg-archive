using System.Globalization;

namespace MtgArchive.Tools.EngineGate;

/// <summary>One timed /simulate request: one row of <c>eval/engine/throughput.md</c> (spec §6.5).
/// A paired (tier-3) request sets <see cref="DisruptedFloor"/>: its runs
/// count pairs, and it plays two games per pair.</summary>
internal sealed record ThroughputRow(
    DateTimeOffset At, string Engine, string Version, string Request, int Runs, int Horizon, double Seconds, int EngineErrors, string Floor, double? HostBusy, bool Gate,
    string? DisruptedFloor = null)
{
    /// <summary>The bar: the gate request finishes in under this many seconds.</summary>
    public const double LimitSeconds = 120;

    public const string Header = """
        # Engine throughput

        `EngineGate throughput` appends two rows per call (spec §6.5; see `README.md`). The gate row times the done-query 2 request as written. Its 10,000 runs at horizon 10 must finish in under 120 s through `/simulate` on this host. At most 1% of them can end in an engine error. The full-horizon row sets every goal leaf due at the horizon, so no run stops early. It is information, not the gate.

        A request with a disruption profile runs paired and appends one row. Its runs are pairs, and each pair plays two games, so the row gives pairs and games per second, the floor of each arm, and the 1% bar over games.

        | Date (UTC) | Engine | Request | Runs | Horizon | Seconds | Runs/s | Engine errors | Floor | Host busy | Verdict |
        |---|---|---|---|---|---|---|---|---|---|---|
        """;

    public bool Paired => DisruptedFloor is not null;

    /// <summary>The games the request played: two per pair.</summary>
    public int Games => Paired ? 2 * Runs : Runs;

    public double RunsPerSecond => Runs / Seconds;

    /// <summary>pass or fail for the gate row; info for the full-horizon row.</summary>
    public string Verdict => !Gate ? "info" : Seconds < LimitSeconds && EngineErrors * 100 <= Games ? "pass" : "fail";

    public string Markdown()
    {
        var busy = HostBusy is { } share ? string.Create(CultureInfo.InvariantCulture, $"{share * 100:0}%") : "n/a";
        var runs = Paired ? $"{Runs} pairs" : $"{Runs}";
        var rate = Paired
            ? string.Create(CultureInfo.InvariantCulture, $"{RunsPerSecond:0} pairs/s, {Games / Seconds:0} games/s")
            : string.Create(CultureInfo.InvariantCulture, $"{RunsPerSecond:0}");
        var floor = Paired ? $"baseline {Floor}; disrupted {DisruptedFloor}" : Floor;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"| {At:yyyy-MM-dd HH:mm} | {Engine} {Version} | {Request} | {runs} | {Horizon} | {Seconds:0.0} | {rate} | {EngineErrors} | {floor} | {busy} | {Verdict} |");
    }
}

/// <summary>One decklist's engine coverage: one row of <c>eval/engine/coverage.md</c>.</summary>
internal sealed record CoverageRow(
    DateTimeOffset At, string Engine, string Version, string Deck, int Cards, IReadOnlyList<string> Unimplemented, IReadOnlyList<string> Unprofiled)
{
    public const string Header = """
        # Engine coverage

        `EngineGate coverage` appends one row per decklist (roadmap carried-forward item 5; see `README.md`). Cards counts the distinct cards of the decklist, commanders included. The share is unprofiled cards over cards. The user decides before cutover whether the share is acceptable.

        | Date (UTC) | Engine | Deck | Cards | Unimplemented | Unprofiled | Unprofiled share | Unprofiled cards |
        |---|---|---|---|---|---|---|---|
        """;

    public double UnprofiledShare => Cards == 0 ? 0 : (double)Unprofiled.Count / Cards;

    public string Markdown() => string.Create(
        CultureInfo.InvariantCulture,
        $"| {At:yyyy-MM-dd HH:mm} | {Engine} {Version} | {Deck} | {Cards} | {Unimplemented.Count} | {Unprofiled.Count} | {UnprofiledShare * 100:0.0}% | {string.Join(", ", Unprofiled)} |");
}

internal static class Reports
{
    /// <summary>Appends one row; a missing file starts with the header.</summary>
    public static void Append(string path, string header, string row)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, header.ReplaceLineEndings("\n") + "\n");
        }

        File.AppendAllText(path, row + "\n");
    }
}
