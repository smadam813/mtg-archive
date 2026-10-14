using System.Numerics;

namespace MtgArchive.Simulation;

/// <summary>
/// The tier-1 closed-form core: the exact hypergeometric tail
/// P(≥ k successes) among cards seen. Exact and deterministic — no
/// sampling, no floor label, no confidence interval, no seed. No mulligan
/// adjustment of any kind: the London mulligan breaks closed-form, so
/// "no mulligans" is an assumptions-block line downstream, not a
/// computation here.
/// </summary>
public static class Hypergeometric
{
    /// <summary>Past any real list; bounds the BigInteger work per call.</summary>
    public const int MaxDeckSize = 10_000;

    /// <summary>
    /// P(at least <paramref name="k"/> of the <paramref name="successes"/>
    /// cards in a <paramref name="deckSize"/>-card deck appear among
    /// <paramref name="cardsSeen"/> cards seen. Seeing more cards than the
    /// deck holds means seeing the whole deck.
    /// </summary>
    public static double TailAtLeast(int k, int successes, int deckSize, int cardsSeen)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deckSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(deckSize, MaxDeckSize);
        ArgumentOutOfRangeException.ThrowIfNegative(successes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(successes, deckSize);
        ArgumentOutOfRangeException.ThrowIfNegative(cardsSeen);

        if (k <= 0)
        {
            return 1;
        }

        var seen = Math.Min(cardsSeen, deckSize);
        var reachable = Math.Min(successes, seen);
        if (k > reachable)
        {
            return 0;
        }

        // Terms below seen - (deckSize - successes) are zero: that many
        // successes are forced into view once the non-successes run out.
        var first = Math.Max(k, seen - (deckSize - successes));
        var successWays = Binomial(successes, first);
        var otherWays = Binomial(deckSize - successes, seen - first);
        BigInteger numerator = 0;
        for (var i = first; ; i++)
        {
            numerator += successWays * otherWays;
            if (i == reachable)
            {
                break;
            }

            // C(n, r±1) from C(n, r): one exact multiply-divide per step.
            successWays = successWays * (successes - i) / (i + 1);
            otherWays = otherWays * (seen - i) / (deckSize - successes - seen + i + 1);
        }

        return (double)numerator / (double)Binomial(deckSize, seen);
    }

    private static BigInteger Binomial(int n, int r)
    {
        r = Math.Min(r, n - r);
        BigInteger result = 1;
        for (var i = 1; i <= r; i++)
        {
            result = result * (n - r + i) / i;
        }

        return result;
    }
}
