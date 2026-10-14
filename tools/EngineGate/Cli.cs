using MtgArchive.Tools.EngineGate.Diagnosis;
using MtgArchive.Tools.EngineGate.Parity;

namespace MtgArchive.Tools.EngineGate;

/// <summary>
/// The engine gates of the XMage migration (spec §6), run on the host
/// against a running service. Phase 2 ships <c>throughput</c> (spec §6.5)
/// and <c>coverage</c> (roadmap carried-forward item 5); phase 3 adds
/// parity, determinism and leak, and phase 4 makes parity the engine
/// regression gate on XMage.
/// </summary>
public static class Cli
{
    public const string Usage = """
        usage:
          EngineGate throughput [--url <service>] [--request <file>] [--out <file>] [--max-busy <percent>] [--sample-seconds <s>] [--force]
          EngineGate coverage [--url <service>] [--out <file>] <request-file> [<request-file>...]
          EngineGate parity-baseline [--cases <file>] [--image <tag> | --endpoint <host:port>] [--threads <n>] [--runs <n>] [--only <case>]
          EngineGate parity [--cases <file>] [--image <tag> | --endpoint <host:port>] [--threads <n>] [--runs <n>] [--only <case>] [--cap-seeds <n>]
          EngineGate determinism [--cases <file>] [--image <tag> | --endpoint <host:port> --endpoint <host:port>] [--threads <n>] [--seeds <n>] [--only <deck|row>]
          EngineGate leak [--cases <file>] [--image <tag> | --endpoint <host:port> --endpoint <host:port>] [--threads <n>] [--seeds <n>] [--only <deck|row>]
          EngineGate trace --case <name> [--mulligan <policy>] [--runs <n>] [--label <name>] [--compare <label>] [--switches "<XMAGE_SWITCHES>"] [--cases <file>] [--image <tag> | --endpoint <host:port>] [--threads <n>]

        throughput samples host CPU (5 s by default) and refuses a host busier than --max-busy (15%) unless --force.
        It then posts the request (default eval/acceptance/done-query-2/rerun-request.json) to /simulate twice: as
        written, which is the gate (under 120 s), and with every goal leaf due at the turn horizon, so no run stops
        early. Each request appends a row to --out (default eval/engine/throughput.md). A request with a disruption
        profile (such as eval/engine/tier-3-request.json) runs paired: one post, one row, runs counted as pairs.
        coverage posts each request file with one run and appends its engine coverage to --out
        (default eval/engine/coverage.md).
        --url defaults to http://127.0.0.1:8080.

        The engine regression gate (see eval/parity/README.md) reads --cases (default eval/parity/cases.json) and keeps
        its decks, baseline and reports beside it. parity-baseline runs the serving XMage (pass --image mtg-archive-xmage,
        the image compose builds) and writes baseline.json; it exits 8 and writes nothing when more than 1% of a
        comparison's games end in an engine error.
        parity runs a candidate sidecar and writes report.md (not with --only); it exits 6 when a comparison fails.
        parity also reads accepted.json beside the cases file: a comparison it names, failing on both seeds in the named
        direction (lower or higher), is accepted, not failed.
        determinism and leak play --seeds (1000) seeds per deck on two
        sidecar processes and append a row per deck, then one per disrupted case under its own profile; they exit 7
        when a digest differs. --only <deck|row> plays one deck or one row and prints it without appending to the report. A gate starts its sidecars from
        --image (default mtg-archive-xmage:ci, --threads 16 game threads) unless --endpoint names running ones.
        trace plays one case on XMage with every log kept, and the analysis, and writes diagnosis/<label>.json (first made for the Obeka turn-4 diagnosis, now for any case).
        Run from the repository root; the default paths are relative to it.
        """;

    /// <param name="handler">The HTTP handler; tests pass a stand-in service.</param>
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, HttpMessageHandler? handler = null)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            stdout.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        if (args[0] == "trace")
        {
            return await TraceCommand.RunAsync(args[1..], stdout, stderr);
        }

        if (args[0] is "parity-baseline" or "parity" or "determinism" or "leak")
        {
            var gateErrors = new List<string>();
            var gate = ParityOptions.Parse(args[1..], gateErrors);
            if (gateErrors.Count > 0)
            {
                gateErrors.ForEach(stderr.WriteLine);
                stderr.WriteLine(Usage);
                return 1;
            }

            return args[0] switch
            {
                "parity-baseline" => await ParityCommands.BaselineAsync(gate, stdout, stderr),
                "parity" => await ParityCommands.ParityAsync(gate, stdout, stderr),
                "determinism" => await DigestCommands.RunAsync(DigestCheck.Determinism, gate, stdout, stderr),
                _ => await DigestCommands.RunAsync(DigestCheck.Leak, gate, stdout, stderr),
            };
        }

        if (args[0] is not ("throughput" or "coverage"))
        {
            stderr.WriteLine($"unknown command '{args[0]}'");
            stderr.WriteLine(Usage);
            return 1;
        }

        var errors = new List<string>();
        var options = GateOptions.Parse(args[1..], errors);
        if (errors.Count > 0)
        {
            errors.ForEach(stderr.WriteLine);
            stderr.WriteLine(Usage);
            return 1;
        }

        using var http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null)
        {
            BaseAddress = new Uri(options.Url),
            Timeout = TimeSpan.FromMinutes(15),
        };
        return args[0] == "throughput"
            ? await Commands.ThroughputAsync(http, options, stdout, stderr)
            : await Commands.CoverageAsync(http, options, stdout, stderr);
    }
}
