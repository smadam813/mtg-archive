using System.Net;
using MtgArchive.Rules;

namespace MtgArchive.Tests;

/// <summary>
/// The WotC landing-page scrape: TXT-link extraction, the
/// MagicCompRules preference over other hosted .txt documents, the
/// literal-space href the published page carries, and the failure modes —
/// against canned landing-page HTML, the way the chunker tests use a
/// canned CR excerpt.
/// </summary>
public sealed class WotcCrClientTests
{
    private const string CrUrl = "https://media.wizards.com/2026/downloads/MagicCompRules 20260819.txt";
    private const string CrText = "Magic: The Gathering Comprehensive Rules\n";

    [Fact]
    public async Task Fetches_the_single_txt_link_escaping_its_literal_space()
    {
        var handler = new CannedHandler(Landing($"<a href=\"{CrUrl}\">TXT</a>"), CrText);
        var client = new WotcCrClient(new HttpClient(handler));

        var fetch = await client.FetchCurrentAsync(CancellationToken.None);

        Assert.Equal(CrText, fetch.RawText);
        Assert.Equal("https://media.wizards.com/2026/downloads/MagicCompRules%2020260819.txt",
            fetch.SourceUrl);
        Assert.Equal(fetch.SourceUrl, handler.Requested[^1]);
    }

    [Fact]
    public async Task A_relative_txt_href_resolves_against_the_landing_page()
    {
        var handler = new CannedHandler(Landing("<a href=\"/downloads/MagicCompRules.txt\">TXT</a>"), CrText);
        var client = new WotcCrClient(new HttpClient(handler));

        var fetch = await client.FetchCurrentAsync(CancellationToken.None);

        Assert.Equal("https://magic.wizards.com/downloads/MagicCompRules.txt", fetch.SourceUrl);
    }

    // The page has hosted tournament rules and FAQs as .txt beside the CR;
    // only a MagicCompRules link should win, not an error.
    [Fact]
    public async Task Other_txt_documents_beside_the_cr_do_not_make_the_page_ambiguous()
    {
        var handler = new CannedHandler(Landing(
            "<a href=\"https://media.wizards.com/MagicTournamentRules.txt\">MTR</a>"
            + $"<a href=\"{CrUrl}\">CR</a>"), CrText);
        var client = new WotcCrClient(new HttpClient(handler));

        var fetch = await client.FetchCurrentAsync(CancellationToken.None);

        Assert.Contains("MagicCompRules", fetch.SourceUrl);
    }

    [Fact]
    public async Task A_page_without_a_txt_link_throws()
    {
        var handler = new CannedHandler(Landing("<a href=\"/rules.pdf\">PDF</a>"), CrText);
        var client = new WotcCrClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.FetchCurrentAsync(CancellationToken.None));
        Assert.Contains("no .txt link", ex.Message);
    }

    [Fact]
    public async Task Two_comprehensive_rules_links_stay_an_error()
    {
        var handler = new CannedHandler(Landing(
            "<a href=\"https://media.wizards.com/a/MagicCompRules 1.txt\">A</a>"
            + "<a href=\"https://media.wizards.com/b/MagicCompRules 2.txt\">B</a>"), CrText);
        var client = new WotcCrClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.FetchCurrentAsync(CancellationToken.None));
        Assert.Contains("2 candidate .txt links", ex.Message);
    }

    private static string Landing(string anchors)
        => $"<html><body><h1>Rules</h1>{anchors}</body></html>";

    /// <summary>Serves the canned landing page for the landing URL and the
    /// canned CR text for everything else, recording each request URL.</summary>
    private sealed class CannedHandler(string landingHtml, string crText) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!.AbsoluteUri);
            var body = request.RequestUri.AbsoluteUri == WotcCrClient.LandingPage ? landingHtml : crText;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }
}
