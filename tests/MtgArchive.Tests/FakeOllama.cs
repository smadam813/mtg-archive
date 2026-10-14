using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MtgArchive.Tests;

/// <summary>
/// An in-process Ollama for the embedding client: canned answers for the
/// three routes the service uses (<c>/api/tags</c>, <c>/api/pull</c>,
/// <c>/api/embed</c>), every request recorded, so no test needs the
/// container. Embeddings come back <see cref="Dimensions"/> wide; set
/// <see cref="EmbedError"/> to answer embed calls with Ollama's 400 error
/// shape instead, <see cref="MaxInputLength"/> to refuse a batch holding a
/// longer input the way <c>truncate: false</c> past the context length
/// does, and <see cref="FailNext"/> to answer that many requests with 503
/// first, as an Ollama that is still starting does.
/// </summary>
internal sealed class FakeOllama : HttpMessageHandler
{
    public List<(string Path, JsonElement Body)> Requests { get; } = [];

    public string[] LocalModels { get; set; } = [];

    public int Dimensions { get; set; } = 1024;

    public string? EmbedError { get; set; }

    public int? MaxInputLength { get; set; }

    public int FailNext { get; set; }

    public TaskCompletionSource<JsonElement> Pulled { get; } = new();

    public static HttpClient Client(FakeOllama fake) => new(fake) { BaseAddress = new Uri("http://fake-ollama/") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? default
            : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement;
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add((path, body));
        if (FailNext > 0)
        {
            FailNext--;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        switch (path)
        {
            case "/api/tags":
                return Json(new { models = LocalModels.Select(name => new { name }) });
            case "/api/pull":
                Pulled.TrySetResult(body);
                return Json(new { status = "success" });
            case "/api/embed" when EmbedError is not null:
                return Json(new { error = EmbedError }, HttpStatusCode.BadRequest);
            case "/api/embed" when MaxInputLength is { } max
                && body.GetProperty("input").EnumerateArray().Any(i => i.GetString()!.Length > max):
                return Json(new { error = "input length exceeds maximum context length" }, HttpStatusCode.BadRequest);
            case "/api/embed":
                var inputs = body.GetProperty("input").GetArrayLength();
                return Json(new
                {
                    embeddings = Enumerable.Range(1, inputs).Select(i => Enumerable.Repeat((float)i, Dimensions)),
                });
            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static HttpResponseMessage Json(object payload, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = JsonContent.Create(payload) };
}
