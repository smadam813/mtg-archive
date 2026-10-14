using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace MtgArchive.Tools.EngineGate;

internal static class Commands
{
    public static async Task<int> ThroughputAsync(HttpClient http, GateOptions options, TextWriter stdout, TextWriter stderr)
    {
        double? busy = null;
        if (options.SampleSeconds > 0)
        {
            busy = await HostCpu.SampleAsync(TimeSpan.FromSeconds(options.SampleSeconds), CancellationToken.None);
            stdout.WriteLine(busy is { } share ? $"host CPU busy {share * 100:0}% over {options.SampleSeconds} s" : "host CPU: no reader on this platform");
            if (busy * 100 > options.MaxBusy && !options.Force)
            {
                stderr.WriteLine($"The host is busy ({busy * 100:0}% > {options.MaxBusy}%). Close games, streams and other heavy work, or pass --force.");
                return 2;
            }
        }

        var request = JsonNode.Parse(await File.ReadAllTextAsync(options.Request))!.AsObject();
        var name = Path.GetFileName(options.Request);
        var output = options.Out ?? "eval/engine/throughput.md";
        var failed = false;

        // A paired request's disrupted arm never stops early, so it gets no full-horizon row.
        var posts = request["disruption_profile"] is null
            ? new[] { (request, name, true), (Requests.FullHorizon(request), $"{name} (full horizon)", false) }
            : [(request, name, true)];
        foreach (var (body, label, gate) in posts)
        {
            var clock = Stopwatch.StartNew();
            var (envelope, exit) = await PostAsync(http, body, stderr);
            var seconds = clock.Elapsed.TotalSeconds;
            if (envelope is null)
            {
                return exit;
            }

            var row = new ThroughputRow(
                DateTimeOffset.UtcNow,
                (string)envelope["engine"]!["name"]!,
                (string)envelope["engine"]!["version"]!,
                label,
                (int)envelope["runs"]!,
                (int)envelope["turn_horizon"]!,
                seconds,
                (int)envelope["engine_errors"]!,
                (string)envelope["probability"]!["label"]!,
                busy,
                gate,
                (string?)envelope["disruption"]?["disrupted"]?["probability"]?["label"]);
            Reports.Append(output, ThroughputRow.Header, row.Markdown());
            stdout.WriteLine(row.Markdown());
            failed |= row.Verdict == "fail";
        }

        return failed ? 5 : 0;
    }

    public static async Task<int> CoverageAsync(HttpClient http, GateOptions options, TextWriter stdout, TextWriter stderr)
    {
        if (options.Files.Count == 0)
        {
            stderr.WriteLine("coverage needs at least one request file.");
            stderr.WriteLine(Cli.Usage);
            return 1;
        }

        var output = options.Out ?? "eval/engine/coverage.md";
        foreach (var file in options.Files)
        {
            var request = JsonNode.Parse(await File.ReadAllTextAsync(file))!.AsObject();
            request["runs"] = 1;
            request.Remove("disruption_profile");
            request.Remove("disruption_mode");
            var (envelope, exit) = await PostAsync(http, request, stderr);
            if (envelope is null)
            {
                return exit;
            }

            var coverage = envelope["engine_coverage"]!;
            var row = new CoverageRow(
                DateTimeOffset.UtcNow,
                (string)envelope["engine"]!["name"]!,
                (string)envelope["engine"]!["version"]!,
                Path.GetFileName(file),
                Requests.CardCount(request),
                [.. coverage["unimplemented"]!.AsArray().Select(n => (string)n!)],
                [.. coverage["unprofiled"]!.AsArray().Select(n => (string)n!)]);
            Reports.Append(output, CoverageRow.Header, row.Markdown());
            stdout.WriteLine(row.Markdown());
        }

        return 0;
    }

    /// <summary>The envelope of one /simulate request, or null and the exit
    /// code: 4 when the request failed, 3 when the service runs another
    /// engine than xmage.</summary>
    private static async Task<(JsonNode? Envelope, int Exit)> PostAsync(HttpClient http, JsonObject body, TextWriter stderr)
    {
        using var response = await http.PostAsync("/simulate", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            stderr.WriteLine($"/simulate answered {(int)response.StatusCode}: {text}");
            return (null, 4);
        }

        var envelope = JsonNode.Parse(text)!;
        var engine = (string?)envelope["engine"]?["name"];
        if (engine != "xmage")
        {
            stderr.WriteLine($"The service runs {engine ?? "an engine with no name"}, not xmage: it is an old build. Rebuild it with docker compose -p mtg-archive -f compose.yaml -f compose.gpu.yml up -d --build xmage service (eval/engine/README.md).");
            return (null, 3);
        }

        return (envelope, 0);
    }
}
