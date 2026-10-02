using LedgerApi.Entities;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LedgerApi.Tests.Data;

// The C# enums keep bad values out of normal code paths; these tests prove the database refuses them
// too, through raw SQL that bypasses EF entirely (DESIGN.md Section 3: "the database physically refuses").
[Collection("Postgres")]
public class CheckConstraintTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public CheckConstraintTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(long AccountId, long TransactionId)> SeedAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var account = new Account
        {
            AccountNumber = "1000000001", AccountName = "Constraint Test", AccountType = AccountType.Wallet,
            AccountClass = AccountClass.Customer, CurrencyCode = "NGN", Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var transaction = new Transaction
        {
            Reference = "TXN-CONSTRAINT", IdempotencyKey = Guid.NewGuid().ToString(),
            Status = TransactionStatus.Success, CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.AddRange(account, transaction);
        await ctx.SaveChangesAsync();
        return (account.Id, transaction.Id);
    }

    [Fact]
    public async Task LedgerEntry_WithUnknownEntryType_IsRejectedByTheDatabase()
    {
        var (accountId, transactionId) = await SeedAsync();
        await using var ctx = _fixture.CreateContext();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ledger_entries (transaction_id, account_id, entry_type, amount, currency, created_at)
            VALUES ({transactionId}, {accountId}, 'Sideways', 10, 'NGN', now())
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("ck_ledger_entries_entry_type", ex.ConstraintName);
    }

    [Fact]
    public async Task Transaction_WithUnknownStatus_IsRejectedByTheDatabase()
    {
        await using var ctx = _fixture.CreateContext();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (reference, idempotency_key, status, created_at)
            VALUES ('TXN-BADSTATUS', {Guid.NewGuid().ToString()}, 'Lost', now())
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("ck_transactions_status", ex.ConstraintName);
    }

    [Fact]
    public async Task ValidEntryTypesAndStatuses_AreAccepted()
    {
        var (accountId, transactionId) = await SeedAsync();
        await using var ctx = _fixture.CreateContext();

        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ledger_entries (transaction_id, account_id, entry_type, amount, currency, created_at)
            VALUES ({transactionId}, {accountId}, 'Debit', 10, 'NGN', now()),
                   ({transactionId}, {accountId}, 'Credit', 10, 'NGN', now())
            """);

        Assert.Equal(2, await ctx.LedgerEntries.CountAsync());
    }
}
