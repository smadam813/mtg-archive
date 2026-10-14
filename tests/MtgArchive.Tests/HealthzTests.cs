using System.Net;

namespace MtgArchive.Tests;

public class HealthzTests
{
    [Fact]
    public async Task Healthz_answers_200_for_the_compose_healthcheck()
    {
        using var app = new ArchiveApp();
        using var client = app.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
