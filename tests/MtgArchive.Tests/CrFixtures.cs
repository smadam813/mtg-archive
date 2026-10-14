using System.Text;
using MtgArchive.Rules;

namespace MtgArchive.Tests;

/// <summary>
/// The CR excerpt fixture — a synthetic file mirroring the published CR TXT
/// structure (UTF-8 BOM, CRLF, header, TOC, rules body with the subrule
/// anomalies, glossary, credits) — and a fixture-backed <see cref="ICrSource"/>.
/// </summary>
internal static class CrFixtures
{
    /// <summary>Raw bytes decoded without BOM handling, so the BOM reaches
    /// <see cref="CrChunker.Normalize"/> exactly as HTTP text would.</summary>
    public static string RawExcerpt()
        => Encoding.UTF8.GetString(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "cr", "cr-excerpt.txt")));

    /// <summary>The excerpt republished under a later effective date, with
    /// rule 101.1 renumbered to 101.2 and the Abandon entry renamed — the
    /// version change the full-replace tests key on.</summary>
    public static string RawExcerptNextVersion()
        => RawExcerpt()
            .Replace("August 7, 2026", "November 6, 2026")
            .Replace("101.1.", "101.2.")
            .Replace("Abandon", "Abscond");
}

internal sealed class FakeCrSource : ICrSource
{
    public string RawText { get; set; } = CrFixtures.RawExcerpt();

    public string SourceUrl { get; set; } = "https://media.example.test/MagicCompRules 20260807.txt";

    public Task<CrFetch> FetchCurrentAsync(CancellationToken cancellationToken)
        => Task.FromResult(new CrFetch(RawText, SourceUrl));
}
