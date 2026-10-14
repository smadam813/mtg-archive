using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MtgArchive.Tests;

/// <summary>
/// EF Core SQL logging: with no category override, EF Core logs
/// the full text of every SQL command at Information level, flooding ingest
/// logs. Asserts the shipped config lowers that category below Information.
/// </summary>
public class LoggingConfigTests
{
    [Fact]
    public void Ef_core_command_logging_is_quieted_below_information()
    {
        using var app = new ArchiveApp();
        var config = app.Services.GetRequiredService<IConfiguration>();

        var raw = config["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"];
        var parsed = Enum.TryParse<LogLevel>(raw, ignoreCase: true, out var level);

        Assert.True(parsed, $"expected a valid LogLevel, got '{raw}'");
        Assert.True(level >= LogLevel.Warning, $"expected at least Warning, got '{level}'");
    }
}
