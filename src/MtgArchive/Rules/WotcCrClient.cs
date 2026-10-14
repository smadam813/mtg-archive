using System.Text.RegularExpressions;

namespace MtgArchive.Rules;

/// <summary>
/// Fetches the current Comprehensive Rules from WotC: scrapes the rules
/// landing page for the TXT download link (the one stable pointer — the
/// media URL itself changes per release), then downloads the file. The
/// published href carries a literal space, which <see cref="Uri"/> escapes.
/// </summary>
public sealed partial class WotcCrClient(HttpClient http) : ICrSource
{
    public const string LandingPage = "https://magic.wizards.com/en/rules";

    public static void Configure(HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MtgArchive/0.1 (+https://github.com/smadam813/mtg-archive)");
        client.Timeout = TimeSpan.FromMinutes(5);
    }

    [GeneratedRegex("href=\"(?<url>[^\"]*\\.txt)\"", RegexOptions.IgnoreCase)]
    private static partial Regex TxtLink();

    public async Task<CrFetch> FetchCurrentAsync(CancellationToken cancellationToken)
    {
        var landing = await http.GetStringAsync(LandingPage, cancellationToken);
        var links = TxtLink().Matches(landing)
            .Select(m => m.Groups["url"].Value)
            .Distinct()
            .ToList();

        // The page has hosted other .txt documents (tournament rules, FAQs)
        // beside the CR, so links named MagicCompRules win when any exist;
        // the strict error stays for genuine ambiguity.
        var candidates = links
            .Where(l => l.Contains("MagicCompRules", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = links;
        }

        if (candidates.Count != 1)
        {
            throw new InvalidOperationException(candidates.Count == 0
                ? $"The rules landing page ({LandingPage}) has no .txt link."
                : $"The rules landing page ({LandingPage}) has {candidates.Count} candidate .txt links; expected exactly one: {string.Join(", ", candidates)}.");
        }

        var url = new Uri(new Uri(LandingPage), candidates[0]);
        var text = await http.GetStringAsync(url, cancellationToken);
        return new CrFetch(text, url.AbsoluteUri);
    }
}
