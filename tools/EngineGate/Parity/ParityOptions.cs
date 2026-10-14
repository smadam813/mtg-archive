using System.Globalization;
using System.Net;
using System.Net.Sockets;
using MtgArchive.Simulation.Xmage;

namespace MtgArchive.Tools.EngineGate.Parity;

/// <summary>
/// The command line of the gate commands. The decks, the baseline and the
/// reports live beside the cases file (<see cref="Root"/>).
/// <see cref="Endpoints"/>, when given, name running sidecars in place of
/// new containers: the first for every command, the second for the second
/// process of determinism and leak.
/// </summary>
internal sealed record ParityOptions(
    string Cases,
    string Image,
    IReadOnlyList<string> Endpoints,
    int Threads,
    int Seeds,
    int CapSeeds,
    int? Runs,
    string? Only)
{
    public const string DefaultCases = "eval/parity/cases.json";

    public const string DefaultImage = "mtg-archive-xmage:ci";

    public string Root => Path.GetDirectoryName(Path.GetFullPath(Cases))!;

    public string DeckPath(string deck) => Path.Combine(Root, "decks", deck + ".json");

    public string BaselinePath => Path.Combine(Root, "baseline.json");

    public string ReportPath => Path.Combine(Root, "report.md");

    /// <summary>The accepted differences (phase 3E), beside the cases file.</summary>
    public string AcceptedPath => Path.Combine(Root, "accepted.json");

    public static ParityOptions Parse(IReadOnlyList<string> args, List<string> errors)
    {
        var cases = DefaultCases;
        var image = DefaultImage;
        var endpoints = new List<string>();
        var threads = 16;
        var seeds = 1000;
        var capSeeds = 1000;
        int? runs = null;
        string? only = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--cases":
                    cases = Value(args, ref i, errors);
                    break;
                case "--image":
                    image = Value(args, ref i, errors);
                    break;
                case "--endpoint":
                    endpoints.Add(Value(args, ref i, errors));
                    break;
                case "--threads":
                    threads = Count(Value(args, ref i, errors), 1, errors);
                    break;
                case "--seeds":
                    seeds = Count(Value(args, ref i, errors), 1, errors);
                    break;
                case "--cap-seeds":
                    capSeeds = Count(Value(args, ref i, errors), 0, errors);
                    break;
                case "--runs":
                    runs = Count(Value(args, ref i, errors), 1, errors);
                    break;
                case "--only":
                    only = Value(args, ref i, errors);
                    break;
                default:
                    errors.Add($"unknown option '{args[i]}'");
                    break;
            }
        }

        foreach (var endpoint in endpoints.Where(e => e.Length > 0))
        {
            try
            {
                EndpointOptions(endpoint);
            }
            catch (ArgumentException ex)
            {
                errors.Add($"--endpoint: {ex.Message}");
            }
        }

        if (endpoints.Count > 2)
        {
            errors.Add("--endpoint: at most two, one per sidecar process");
        }
        else if (endpoints.Count == 2 && errors.Count == 0 && SameProcess(EndpointOptions(endpoints[0]), EndpointOptions(endpoints[1])))
        {
            errors.Add("--endpoint: the two endpoints name one process; determinism and leak need two");
        }

        return new ParityOptions(cases, image, endpoints, threads, seeds, capSeeds, runs, only);
    }

    /// <summary>A running sidecar at "host:port": a connect waits 5 s, a read 60 s.</summary>
    public static XmageOptions EndpointOptions(string endpoint)
    {
        var colon = endpoint.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(endpoint[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            throw new ArgumentException($"'{endpoint}' is not host:port.");
        }

        return new XmageOptions(endpoint[..colon], port, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// True when two endpoints reach one listener: the same port on hosts
    /// that share an address (localhost and 127.0.0.1). A host that does not
    /// resolve counts as its own name.
    /// </summary>
    internal static bool SameProcess(XmageOptions a, XmageOptions b)
    {
        if (a.Port != b.Port)
        {
            return false;
        }

        return Addresses(a.Host).Overlaps(Addresses(b.Host));

        static HashSet<string> Addresses(string host)
        {
            HashSet<string> set = [host.ToLowerInvariant()];
            try
            {
                foreach (var address in Dns.GetHostAddresses(host))
                {
                    set.Add((address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString());
                }
            }
            catch (SocketException)
            {
            }

            // localhost may resolve to ::1 alone, or to 127.0.0.1 alone: both are this host.
            if (set.Any(s => IPAddress.TryParse(s, out var ip) && IPAddress.IsLoopback(ip)))
            {
                set.Add("loopback");
            }

            return set;
        }
    }

    private static string Value(IReadOnlyList<string> args, ref int i, List<string> errors)
    {
        if (i + 1 < args.Count)
        {
            return args[++i];
        }

        errors.Add($"'{args[i]}' needs a value");
        return string.Empty;
    }

    private static int Count(string text, int minimum, List<string> errors)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= minimum)
        {
            return value;
        }

        errors.Add($"'{text}' is not a whole number of at least {minimum}");
        return minimum;
    }
}
