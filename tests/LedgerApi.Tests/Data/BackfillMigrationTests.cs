using LedgerApi.Contracts.Requests;
using LedgerApi.Data;
using LedgerApi.Services;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LedgerApi.Tests.Data;

// Runs the real BackfillTransactionInitiatedBy migration (decision #44) on its own database: migrate to the
// step before it, insert pre-#42 data, then apply it.
[Collection("Postgres")]
public class BackfillMigrationTests(PostgresFixture fixture)
{
    private const string MigrationBeforeBackfill = "AddIdempotencyToAccounts";

    private static async Task<IMigrator> MigrateToBeforeBackfillAsync(LedgerDbContext ctx)
    {
        await ctx.Database.EnsureDeletedAsync();
        var migrator = ctx.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeBackfill);
        return migrator;
    }

    [Fact]
    public async Task Backfill_SetsTheClientFromTheAuditTrail_SoAnAuthenticatedRetryIsReplayed()
    {
        await using var ctx = fixture.CreateContext(database: "backfill_replay");
        var migrator = await MigrateToBeforeBackfillAsync(ctx);

        // An old-style transaction (no client, no hash) and the audit row naming the client that sent it.
        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transactions (reference, idempotency_key, status, created_at)
            VALUES ('TXN-OLD', 'old-key', 'Success', now());
            INSERT INTO audit_logs (actor_id, action, entity_type, entity_id, created_at)
            VALUES ('payment-api', 'Transfer', 'Transaction', 'TXN-OLD', now());
            """);

        await migrator.MigrateAsync();

        var row = await ctx.Transactions.AsNoTracking().SingleAsync(t => t.Reference == "TXN-OLD");
        Assert.Equal("payment-api", row.InitiatedBy);

        // The production scenario: the same client retries the old key and gets the original back.
        var response = await new TransferService(ctx, TestAudit.For(ctx), new TestCaller("payment-api"))
            .TransferAsync(new TransferRequest("1", "2", 10m, "retry", "old-key"));
        Assert.Equal("TXN-OLD", response.Reference);
    }

    [Fact]
    public async Task Backfill_SkipsARow_WhenThatClientAlreadyHasADuplicateForTheKey()
    {
        await using var ctx = fixture.CreateContext(database: "backfill_duplicate");
        var migrator = await MigrateToBeforeBackfillAsync(ctx);

        // The bug already struck: the client's retry created TXN-NEW with the same key as TXN-OLD.
        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transactions (reference, idempotency_key, status, created_at)
            VALUES ('TXN-OLD', 'dup-key', 'Success', now());
            INSERT INTO transactions (reference, idempotency_key, initiated_by, status, created_at)
            VALUES ('TXN-NEW', 'dup-key', 'payment-api', 'Success', now());
            INSERT INTO audit_logs (actor_id, action, entity_type, entity_id, created_at)
            VALUES ('payment-api', 'Transfer', 'Transaction', 'TXN-OLD', now());
            """);

        await migrator.MigrateAsync(); // must not fail on the unique index

        var old = await ctx.Transactions.AsNoTracking().SingleAsync(t => t.Reference == "TXN-OLD");
        Assert.Null(old.InitiatedBy);
    }

    [Fact]
    public async Task Backfill_LeavesARowAlone_WhenTheAuditTrailHasNoClient()
    {
        await using var ctx = fixture.CreateContext(database: "backfill_no_actor");
        var migrator = await MigrateToBeforeBackfillAsync(ctx);

        // From before API keys existed: the audit row has no actor.
        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO transactions (reference, idempotency_key, status, created_at)
            VALUES ('TXN-PREAUTH', 'preauth-key', 'Success', now());
            INSERT INTO audit_logs (action, entity_type, entity_id, created_at)
            VALUES ('Transfer', 'Transaction', 'TXN-PREAUTH', now());
            """);

        await migrator.MigrateAsync();

        var row = await ctx.Transactions.AsNoTracking().SingleAsync(t => t.Reference == "TXN-PREAUTH");
        Assert.Null(row.InitiatedBy);
    }
}
