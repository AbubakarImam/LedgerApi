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

    // Runs the real AddChainKind migration (decision #53) over a pre-existing chain row, the same shape as
    // every chain created before this decision shipped.
    [Fact]
    public async Task Migration_BackfillsExistingChains_ToPayment()
    {
        await using var ctx = fixture.CreateContext(database: "chain_kind_migration");
        await ctx.Database.EnsureDeletedAsync();
        var migrator = ctx.GetService<IMigrator>();
        await migrator.MigrateAsync("AddTransactionPayloadFields"); // the migration right before AddChainKind

        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transaction_chains (reference, idempotency_key, status, created_at)
            VALUES ('CHN-OLD-1', 'old-chain-1', 'Success', now());
            """);

        await migrator.MigrateAsync();

        var old = await ctx.TransactionChains.AsNoTracking().SingleAsync(c => c.Reference == "CHN-OLD-1");
        Assert.Equal(ChainKind.Payment, old.ChainKind); // NOT NULL DEFAULT 'Payment' backfilled by ADD COLUMN itself

        // A Disbursement/Collection chain can be written after the migration, and the enum's own closed
        // set is enforced at the database too.
        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transaction_chains (reference, idempotency_key, status, chain_kind, created_at)
            VALUES ('CHN-NEW', 'new-chain-1', 'Pending', 'Disbursement', now());
            """);
        var badKind = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transaction_chains (reference, idempotency_key, status, chain_kind, created_at)
            VALUES ('CHN-BAD-KIND', 'bad-kind-1', 'Pending', 'Payroll', now());
            """));
        Assert.Equal("ck_transaction_chains_chain_kind", badKind.ConstraintName);

        // The widened purpose check also admits the two new leg purposes.
        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transactions (reference, idempotency_key, status, created_at, chain_id, sequence, purpose)
            SELECT 'TXN-DISBURSEMENT-LEG', 'disb-leg-1', 'Pending', now(), id, 1, 'Disbursement'
            FROM transaction_chains WHERE reference = 'CHN-NEW';
            """);
    }
}
