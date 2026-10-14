using System.IO.Compression;
using System.Text.Json;

namespace MtgArchive.Ingest;

/// <summary>
/// Downloads Scryfall bulk data: resolves the file's current
/// <c>jsonl_download_uri</c> from the /bulk-data catalog, then streams the
/// gzipped JSONL to disk decompressed. Scryfall's terms require an accurate
/// User-Agent and an Accept header (<see cref="Configure"/>).
/// </summary>
public sealed class ScryfallBulkClient(HttpClient http) : IScryfallBulkSource
{
    public static void Configure(HttpClient client)
    {
        client.BaseAddress = new Uri("https://api.scryfall.com/");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MtgArchive/0.1 (+https://github.com/smadam813/mtg-archive)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        // Bulk files are tens of MB; the 100-second default is too tight for
        // slow links.
        client.Timeout = TimeSpan.FromMinutes(15);
    }

    private static readonly Dictionary<ScryfallBulkType, (string CatalogType, string FileName)> Files = new()
    {
        [ScryfallBulkType.OracleCards] = ("oracle_cards", "oracle-cards.jsonl"),
        [ScryfallBulkType.Rulings] = ("rulings", "rulings.jsonl"),
        [ScryfallBulkType.OracleTags] = ("oracle_tags", "oracle-tags.jsonl"),
    };

    // The catalog is read once per client instance: the fetch phase's three
    // concurrent downloads share it, and the corpus-state gate's own
    // instance reads it once for the freshness probe. A few KB, so the
    // shared read takes no cancellation token.
    private readonly Lazy<Task<JsonElement>> _catalog = new(() => FetchCatalogAsync(http));

    public async Task<string> DownloadAsync(ScryfallBulkType type, string workDir, CancellationToken cancellationToken)
    {
        var (catalogType, fileName) = Files[type];
        var downloadUri = await ResolveDownloadUriAsync(catalogType);
        var path = Path.Combine(workDir, fileName);

        using var response = await http.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(path);
        if (downloadUri.AbsolutePath.EndsWith(".gz", StringComparison.Ordinal))
        {
            // leaveOpen: the body's own using disposes it exactly once.
            await using var decompressed = new GZipStream(body, CompressionMode.Decompress, leaveOpen: true);
            await decompressed.CopyToAsync(file, cancellationToken);
        }
        else
        {
            await body.CopyToAsync(file, cancellationToken);
        }

        return path;
    }

    public async Task<DateTimeOffset> LatestUpdatedAtAsync(CancellationToken cancellationToken)
    {
        var consumed = Files.Values.Select(f => f.CatalogType).ToHashSet();
        DateTimeOffset? latest = null;
        foreach (var entry in (await _catalog.Value).EnumerateArray())
        {
            if (consumed.Contains(entry.GetProperty("type").GetString()!))
            {
                var updatedAt = entry.GetProperty("updated_at").GetDateTimeOffset();
                if (latest is null || updatedAt > latest)
                {
                    latest = updatedAt;
                }
            }
        }

        return latest
            ?? throw new InvalidOperationException("Scryfall bulk-data catalog lists none of the consumed bulk types.");
    }

    public async Task<DateTimeOffset> UpdatedAtAsync(ScryfallBulkType type, CancellationToken cancellationToken)
        => (await CatalogEntryAsync(Files[type].CatalogType)).GetProperty("updated_at").GetDateTimeOffset();

    /// <summary>The catalog's <c>data</c> array, cloned so the document can
    /// be disposed.</summary>
    private static async Task<JsonElement> FetchCatalogAsync(HttpClient http)
    {
        await using var catalogStream = await http.GetStreamAsync("bulk-data");
        using var catalog = await JsonDocument.ParseAsync(catalogStream);
        return catalog.RootElement.GetProperty("data").Clone();
    }

    private async Task<Uri> ResolveDownloadUriAsync(string catalogType)
        => new((await CatalogEntryAsync(catalogType)).GetProperty("jsonl_download_uri").GetString()!);

    private async Task<JsonElement> CatalogEntryAsync(string catalogType)
    {
        foreach (var entry in (await _catalog.Value).EnumerateArray())
        {
            if (entry.GetProperty("type").GetString() == catalogType)
            {
                return entry;
            }
        }

        throw new InvalidOperationException($"Scryfall bulk-data catalog has no '{catalogType}' entry.");
    }
}
