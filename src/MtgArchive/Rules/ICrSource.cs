namespace MtgArchive.Rules;

/// <summary>
/// Where the current Comprehensive Rules text comes from. The production
/// implementation scrapes the WotC rules landing page
/// (<see cref="WotcCrClient"/>); tests inject fixture-backed fakes.
/// </summary>
public interface ICrSource
{
    Task<CrFetch> FetchCurrentAsync(CancellationToken cancellationToken);
}

/// <summary>The raw downloaded CR text and the TXT link it came from.</summary>
public sealed record CrFetch(string RawText, string SourceUrl);
