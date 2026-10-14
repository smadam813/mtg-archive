using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MtgArchive.Tests;

/// <summary>
/// Applies the EF migrations from zero to fresh databases in the suite's
/// shared pgvector/pgvector:pg18 container.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrationSmokeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_on_a_fresh_database_enable_the_vector_extension()
    {
        var connectionString = await postgres.CreateDatabaseAsync("migrate_fresh");
        await using var db = PostgresFixture.CreateContext(connectionString);

        await db.Database.MigrateAsync();

        Assert.True(await VectorExtensionPresent(connectionString));
    }

    [Fact]
    public async Task Service_startup_applies_migrations_automatically()
    {
        var connectionString = await postgres.CreateDatabaseAsync("startup_probe");
        using var app = new ArchiveApp(autoMigrate: true, connectionString);
        using var client = app.CreateClient();

        var response = await client.GetAsync("/healthz");

        response.EnsureSuccessStatusCode();
        Assert.True(await VectorExtensionPresent(connectionString));
    }

    private static async Task<bool> VectorExtensionPresent(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM pg_extension WHERE extname = 'vector'", conn);
        return (long)(await cmd.ExecuteScalarAsync())! == 1;
    }
}
