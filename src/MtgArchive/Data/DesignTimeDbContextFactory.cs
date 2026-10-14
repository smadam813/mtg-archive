using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MtgArchive.Data;

/// <summary>
/// `dotnet ef migrations add` uses this factory. It never connects — the
/// tool generates migrations offline and the service applies them at startup.
/// Reads the connection string from appsettings.Development.json so the dev
/// database has one source of truth.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ArchiveDbContext>
{
    public ArchiveDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json"))
            .Build();
        var options = new DbContextOptionsBuilder<ArchiveDbContext>()
            .UseNpgsql(configuration.GetConnectionString("Archive"), o => o.UseVector())
            .Options;
        return new ArchiveDbContext(options);
    }
}
