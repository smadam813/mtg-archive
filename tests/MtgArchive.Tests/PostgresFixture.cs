using Microsoft.EntityFrameworkCore;
using MtgArchive.Data;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MtgArchive.Tests;

/// <summary>
/// The suite's one pgvector/pgvector:pg18 container (xunit collection
/// fixture). The container's default database is migrated once and shared;
/// tests that need to migrate from zero create fresh databases on demand.
/// Requires a running Docker daemon.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        // The suite creates a database (and so a connection pool) per test,
        // and idle pooled connections outlive their test by minutes — the
        // 100-connection default runs out mid-suite.
        .WithCommand("-c", "max_connections=500")
        .Build();

    public static ArchiveDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ArchiveDbContext>()
            .UseNpgsql(connectionString, o => o.UseVector())
            .Options;
        return new ArchiveDbContext(options);
    }

    /// <summary>The shared migrated database, for hosts that serve it over HTTP.</summary>
    public string CorpusConnectionString => _postgres.GetConnectionString();

    /// <summary>A context on the shared migrated database.</summary>
    public ArchiveDbContext CreateCorpusContext() => CreateContext(CorpusConnectionString);

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(string name)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {new NpgsqlCommandBuilder().QuoteIdentifier(name)}", conn);
        await cmd.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateCorpusContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
