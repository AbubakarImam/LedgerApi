using Testcontainers.PostgreSql;
using LedgerApi.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using LedgerApi.Tests.Fixtures;

namespace LedgerApi.Tests.Fixtures;

public class PostgresFixture : IAsyncLifetime
{

    private readonly PostgreSqlContainer _container = new
    PostgreSqlBuilder("postgres:16-alpine")
    .Build();

    // database: another database in the same container, for tests that need their own schema state
    // (e.g. migration tests). Null means the shared, fully migrated test database.
    public LedgerDbContext CreateContext(string? database = null)
    {
        var connectionString = database is null
            ? _container.GetConnectionString()
            : new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;
        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new LedgerDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
           "TRUNCATE TABLE accounts, audit_logs, transactions, ledger_entries, reversals RESTART IDENTITY CASCADE");
    }
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }