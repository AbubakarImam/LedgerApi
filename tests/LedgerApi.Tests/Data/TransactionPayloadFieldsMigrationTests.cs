using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LedgerApi.Tests.Data;

// Runs the real AddTransactionPayloadFields migration (decisions #49-#52) over pre-existing data on its
// own database: four new nullable columns on both transactions and transaction_chains, no backfill —
// unlike decision #48's chart-of-accounts migration, nothing here has a meaningful value for a
// pre-existing row.
[Collection("Postgres")]
public class TransactionPayloadFieldsMigrationTests(PostgresFixture fixture)
{
    private const string MigrationBefore = "BackfillAccountGlAccountIds"; // the migration immediately before this one

    [Fact]
    public async Task Migration_AddsFourNullableColumns_ToBothTables_WithoutTouchingExistingRows()
    {
        await using var ctx = fixture.CreateContext(database: "transaction_payload_fields_migration");
        await ctx.Database.EnsureDeletedAsync();
        var migrator = ctx.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);

        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transactions (reference, idempotency_key, initiated_by, status, created_at)
            VALUES ('TXN-PRE-MIGRATION', 'pre-1', 'payment-api', 'Success', now());
            """);
        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transaction_chains (reference, idempotency_key, initiated_by, status, created_at)
            VALUES ('CHN-PRE-MIGRATION', 'pre-chain-1', 'payment-api', 'Success', now());
            """);

        await migrator.MigrateAsync();

        async Task<bool> IsNullableAsync(string table, string column) => await ctx.Database.SqlQuery<string>($"""
            SELECT is_nullable AS "Value" FROM information_schema.columns
            WHERE table_name = {table} AND column_name = {column}
            """).SingleAsync() == "YES";

        foreach (var column in new[] { "value_date", "branch_code", "channel", "decision_id" })
        {
            Assert.True(await IsNullableAsync("transactions", column), $"transactions.{column} should be nullable");
            Assert.True(await IsNullableAsync("transaction_chains", column), $"transaction_chains.{column} should be nullable");
        }

        // value_date is DATE, not a timestamp (decision #49) — every source checked treats this as a
        // calendar-day concept, not a time-of-day one.
        var valueDateType = await ctx.Database.SqlQuery<string>($"""
            SELECT data_type AS "Value" FROM information_schema.columns
            WHERE table_name = 'transactions' AND column_name = 'value_date'
            """).SingleAsync();
        Assert.Equal("date", valueDateType);
        var chainValueDateType = await ctx.Database.SqlQuery<string>($"""
            SELECT data_type AS "Value" FROM information_schema.columns
            WHERE table_name = 'transaction_chains' AND column_name = 'value_date'
            """).SingleAsync();
        Assert.Equal("date", chainValueDateType);

        // Pre-existing rows survive the migration untouched: every new column is null, not backfilled.
        var transaction = await ctx.Transactions.AsNoTracking().SingleAsync(t => t.Reference == "TXN-PRE-MIGRATION");
        Assert.Null(transaction.ValueDate);
        Assert.Null(transaction.BranchCode);
        Assert.Null(transaction.Channel);
        Assert.Null(transaction.DecisionId);

        var chain = await ctx.TransactionChains.AsNoTracking().SingleAsync(c => c.Reference == "CHN-PRE-MIGRATION");
        Assert.Null(chain.ValueDate);
        Assert.Null(chain.BranchCode);
        Assert.Null(chain.Channel);
        Assert.Null(chain.DecisionId);

        // A row written after the migration can populate all four columns, on both tables.
        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transactions
                (reference, idempotency_key, initiated_by, status, created_at, value_date, branch_code, channel, decision_id)
            VALUES
                ('TXN-POST-MIGRATION', 'post-1', 'payment-api', 'Success', now(), CURRENT_DATE, 'BR-01', 'OpsConsole', 'decision-123');
            """);
        var newTransaction = await ctx.Transactions.AsNoTracking().SingleAsync(t => t.Reference == "TXN-POST-MIGRATION");
        Assert.NotNull(newTransaction.ValueDate);
        Assert.Equal("BR-01", newTransaction.BranchCode);
        Assert.Equal("OpsConsole", newTransaction.Channel);
        Assert.Equal("decision-123", newTransaction.DecisionId);

        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transaction_chains
                (reference, idempotency_key, initiated_by, status, created_at, value_date, branch_code, channel, decision_id)
            VALUES
                ('CHN-POST-MIGRATION', 'post-chain-1', 'payment-api', 'Success', now(), CURRENT_DATE, 'BR-01', 'OpsConsole', 'decision-123');
            """);
        var newChain = await ctx.TransactionChains.AsNoTracking().SingleAsync(c => c.Reference == "CHN-POST-MIGRATION");
        Assert.NotNull(newChain.ValueDate);
        Assert.Equal("BR-01", newChain.BranchCode);
        Assert.Equal("OpsConsole", newChain.Channel);
        Assert.Equal("decision-123", newChain.DecisionId);
    }
}
