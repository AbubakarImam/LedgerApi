using LedgerApi.Data;
using LedgerApi.Entities;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace LedgerApi.Tests.Data;

// Runs the real AddTransactionChains migration (decision #45) over pre-existing data on its own database.
[Collection("Postgres")]
public class ChainMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Migration_LeavesExistingTransactionsValid_OutsideAnyChain()
    {
        await using var ctx = fixture.CreateContext(database: "chain_migration");
        await ctx.Database.EnsureDeletedAsync();
        var migrator = ctx.GetService<IMigrator>();
        await migrator.MigrateAsync("BackfillTransactionInitiatedBy"); // the migration before chains

        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transactions (reference, idempotency_key, initiated_by, status, created_at)
            VALUES ('TXN-OLD-1', 'old-1', 'payment-api', 'Success', now()),
                   ('TXN-OLD-2', 'old-2', 'payment-api', 'Failed', now());
            """);

        await migrator.MigrateAsync();

        var old = await ctx.Transactions.AsNoTracking().Where(t => t.Reference.StartsWith("TXN-OLD")).ToListAsync();
        Assert.Equal(2, old.Count);
        Assert.All(old, t =>
        {
            Assert.Null(t.ChainId);
            Assert.Null(t.Sequence);
            Assert.Null(t.Purpose);
        });

        // A plain transaction can still be written after the migration.
        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transactions (reference, idempotency_key, initiated_by, status, created_at)
            VALUES ('TXN-NEW', 'new-1', 'payment-api', 'Pending', now());
            """);

        // A half-filled chain leg is refused by the database.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transactions (reference, idempotency_key, status, created_at, sequence)
            VALUES ('TXN-HALF', 'half-1', 'Pending', now(), 1);
            """));
        Assert.Equal("ck_transactions_chain_leg", ex.ConstraintName);
    }
}
