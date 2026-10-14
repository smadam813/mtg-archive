using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace MtgArchive.Tests;

/// <summary>
/// Tests that change process-wide state, such as the current directory.
/// xunit runs this collection alone, after the parallel collections, so no
/// other test sees the change.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessStateCollection
{
    public const string Name = "process-state";
}

/// <summary>
/// WebApplicationFactory reads its app manifest from the current
/// directory. Without it, it falls back to a content root that does not
/// exist, and host startup throws. <see cref="ArchiveApp"/> pins the content
/// root, so the current directory does not matter.
/// </summary>
[Collection(ProcessStateCollection.Name)]
public class ArchiveAppTests
{
    [Fact]
    public async Task The_host_starts_on_the_service_project_when_the_current_directory_is_elsewhere()
    {
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(Path.GetTempPath());
        try
        {
            using var app = new ArchiveApp();
            using var client = app.CreateClient();

            var response = await client.GetAsync("/healthz");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var contentRoot = app.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
            Assert.True(File.Exists(Path.Combine(contentRoot, "MtgArchive.csproj")), contentRoot);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }
}
