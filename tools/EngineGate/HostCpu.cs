using System.Globalization;
using System.Runtime.InteropServices;

namespace MtgArchive.Tools.EngineGate;

/// <summary>
/// The host's CPU busy share over an interval, over all cores, 0 to 1. The
/// user streams and games on this host, and that slows runs by 15-25%, so
/// the throughput gate samples first (spec §6.5). Windows reads
/// GetSystemTimes; Linux reads /proc/stat; any other platform has no reader.
/// </summary>
internal static partial class HostCpu
{
    /// <summary>Cumulative idle time and total time, in any one unit.</summary>
    internal readonly record struct Times(ulong Idle, ulong Total);

    public static async Task<double?> SampleAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        if (Read() is not { } before)
        {
            return null;
        }

        await Task.Delay(interval, cancellationToken);
        return Read() is { } after ? Busy(before, after) : null;
    }

    public static double Busy(Times before, Times after)
    {
        var total = after.Total - before.Total;
        return total == 0 ? 0 : 1 - (double)(after.Idle - before.Idle) / total;
    }

    /// <summary>The first line of /proc/stat: "cpu user nice system idle
    /// iowait irq softirq steal ...". Idle counts idle and iowait.</summary>
    public static Times? ParseProcStat(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 6 || parts[0] != "cpu")
        {
            return null;
        }

        var values = parts.Skip(1).Take(8).Select(p => ulong.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        return new Times(values[3] + values[4], values.Aggregate(0UL, (sum, v) => sum + v));
    }

    private static Times? Read()
    {
        if (OperatingSystem.IsWindows())
        {
            // Kernel time includes idle time, so the total is kernel plus user.
            return GetSystemTimes(out var idle, out var kernel, out var user) ? new Times(idle, kernel + user) : null;
        }

        return OperatingSystem.IsLinux() && File.Exists("/proc/stat") ? ParseProcStat(File.ReadLines("/proc/stat").First()) : null;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
