namespace MtgArchive.Embedding;

/// <summary>
/// Startup model provisioning. The service is the single authority on which
/// model tag must exist in the stock <c>ollama/ollama</c> container, so a
/// model switch is configuration (<c>Embedding:ModelTag</c>), never a
/// compose or image change. Runs in the background — a first pull is
/// hundreds of MB — and retries a failure (Ollama still starting, a dropped
/// pull) with backoff until the tag is present. Never fatal: the rest of
/// the service works without embeddings, and embedding calls fail loudly on
/// their own until then.
/// </summary>
public sealed class EmbeddingModelProvisioner(OllamaEmbedder embedder, ILogger<EmbeddingModelProvisioner> logger)
    : BackgroundService
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tag = embedder.Model.Tag;
        var delay = TimeSpan.FromSeconds(1);
        while (true)
        {
            try
            {
                if (await embedder.EnsureModelAsync(stoppingToken))
                {
                    logger.LogInformation("Pulled embedding model {ModelTag}", tag);
                }
                else
                {
                    logger.LogInformation("Embedding model {ModelTag} is present", tag);
                }

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Provisioning embedding model {ModelTag} failed; retrying in {Delay}", tag, delay);
            }

            await Task.Delay(delay, stoppingToken);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
        }
    }
}
