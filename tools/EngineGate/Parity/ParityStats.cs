using MtgArchive.Simulation.Engine;

namespace MtgArchive.Tools.EngineGate.Parity;

/// <summary>
/// The difference of two floors, candidate minus baseline, with its 95% interval
/// (spec §6.1). The interval is Newcombe's hybrid score interval (method
/// 10), built from the two Wilson intervals that the envelope already
/// reports; it suits floors near 0 or 1 and unequal run counts.
/// </summary>
internal readonly record struct Difference(double Estimate, double Lower, double Upper)
{
    public bool ContainsZero => Lower <= 0 && 0 <= Upper;

    public static Difference Newcombe(int successes1, int runs1, int successes2, int runs2)
    {
        var p1 = (double)successes1 / runs1;
        var p2 = (double)successes2 / runs2;
        var w1 = ConfidenceInterval.Wilson(successes1, runs1);
        var w2 = ConfidenceInterval.Wilson(successes2, runs2);
        var estimate = p1 - p2;
        return new Difference(
            estimate,
            estimate - Math.Sqrt(Square(p1 - w1.Lower) + Square(w2.Upper - p2)),
            estimate + Math.Sqrt(Square(w1.Upper - p1) + Square(p2 - w2.Lower)));
    }

    private static double Square(double x) => x * x;
}

/// <summary>The verdict of one comparison (spec §6.1).</summary>
internal static class Verdicts
{
    public const string Pass = "pass";

    /// <summary>Failed at the base seed, passed at the fresh seed: with about
    /// 18 comparisons, one false fail at 95% is likely.</summary>
    public const string PassFresh = "pass (fresh seed)";

    public const string Fail = "fail";

    /// <summary>More than 1% of the candidate's games at the base seed, or at the
    /// fresh seed, ended in an engine error: the floor is no evidence either way.</summary>
    public const string Errors = "engine errors";

    /// <summary>Failed on both seeds, in the direction an entry of accepted.json names (phase 3E).</summary>
    public const string Accepted = "accepted";

    /// <param name="first">The difference at the base seed.</param>
    /// <param name="fresh">The difference at the fresh seed; required when <paramref name="first"/> excludes 0.</param>
    /// <param name="freshEngineErrors">The candidate's engine errors at the fresh seed; 0 when it did not play.</param>
    public static string Of(Difference first, Difference? fresh, int engineErrors, int runs, int freshEngineErrors = 0, AcceptedDifference? accepted = null)
    {
        if (engineErrors * 100 > runs || freshEngineErrors * 100 > runs)
        {
            return Errors;
        }

        if (first.ContainsZero)
        {
            return Pass;
        }

        if (fresh is not { } again)
        {
            throw new ArgumentException("A comparison that fails at the base seed needs its fresh-seed difference.", nameof(fresh));
        }

        if (again.ContainsZero)
        {
            return PassFresh;
        }

        return accepted is not null && accepted.Matches(first) && accepted.Matches(again) ? Accepted : Fail;
    }
}
