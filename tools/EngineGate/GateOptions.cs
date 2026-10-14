using System.Globalization;

namespace MtgArchive.Tools.EngineGate;

/// <summary>The command line of both commands; <see cref="Out"/> is null
/// when the command's own default applies.</summary>
internal sealed record GateOptions(string Url, string Request, string? Out, double MaxBusy, bool Force, double SampleSeconds, IReadOnlyList<string> Files)
{
    public const string DefaultUrl = "http://127.0.0.1:8080";

    public const string DefaultRequest = "eval/acceptance/done-query-2/rerun-request.json";

    public static GateOptions Parse(IReadOnlyList<string> args, List<string> errors)
    {
        var url = DefaultUrl;
        var request = DefaultRequest;
        string? output = null;
        var maxBusy = 15.0;
        var sample = 5.0;
        var force = false;
        var files = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--url":
                    url = Value(args, ref i, errors);
                    break;
                case "--request":
                    request = Value(args, ref i, errors);
                    break;
                case "--out":
                    output = Value(args, ref i, errors);
                    break;
                case "--max-busy":
                    maxBusy = Number(Value(args, ref i, errors), errors);
                    break;
                case "--sample-seconds":
                    sample = Number(Value(args, ref i, errors), errors);
                    break;
                case "--force":
                    force = true;
                    break;
                case var option when option.StartsWith("--", StringComparison.Ordinal):
                    errors.Add($"unknown option '{option}'");
                    break;
                default:
                    files.Add(args[i]);
                    break;
            }
        }

        return new GateOptions(url, request, output, maxBusy, force, sample, files);
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

    private static double Number(string text, List<string> errors)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        errors.Add($"'{text}' is not a number");
        return 0;
    }
}
