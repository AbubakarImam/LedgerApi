using LedgerApi.Auditing;
using LedgerApi.Contracts.Requests;
using LedgerApi.Entities;
using LedgerApi.Exceptions;
using LedgerApi.Services;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace LedgerApi.Tests.Services;

[Collection("Postgres")]
public class AccountServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public AccountServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AccountAsync_CreateAccount_OnHappyPath()
    {
        await using var ctx = _fixture.CreateContext();

        var service = new AccountService(ctx, TestAudit.For(ctx), TestCaller.Default);
        var request = new CreateAccountRequest(
            AccountName: "Test Account",
            AccountType: "Wallet",
            CurrencyCode: "NGN",
            IdempotencyKey: Guid.NewGuid().ToString()
            );

        var response = await service.CreateAccountAsync(request);

        Assert.Equal(request.AccountName, response.AccountName);

        await using var verifyCtx = _fixture.CreateContext();
        var accounts = await verifyCtx.Accounts.ToListAsync();
        Assert.Single(accounts);
    }

    [Fact]
    public async Task AccountAsync_GetAccount_HappyPath()
    {
        await using var ctx = _fixture.CreateContext();

        var seedAccount = new Account
        {
            AccountNumber = "NGN-TEST-SYS-01",
            AccountName = "Test System Account",
            AccountType = AccountType.Wallet,
            AccountClass = AccountClass.System,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        ctx.Accounts.Add(seedAccount);
        await ctx.SaveChangesAsync();

        var service = new AccountService(ctx, TestAudit.For(ctx), TestCaller.Default);
        var request = "NGN-TEST-SYS-01";

        var response = await service.GetAccountAsync(request);

        Assert.Equal(seedAccount.AccountNumber, response.AccountNumber);

    }

    [Fact]
    public async Task AccountAsync_GetAccount_NotFoundPath()
    {
        await using var ctx = _fixture.CreateContext();


        var service = new AccountService(ctx, TestAudit.For(ctx), TestCaller.Default);

        var response = await service.GetAccountAsync("0000000000");

        Assert.Null(response);

    }

    [Fact]
    public async Task AccountAsync_GetBalanceAsync_HappyPath()
    {
        await using var ctx = _fixture.CreateContext();

        var seedAccount = new Account
        {
            AccountNumber = "NGN-TEST-SYS-01",
            AccountName = "Test System Account",
            AccountType = AccountType.Wallet,
            AccountClass = AccountClass.System,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var seedTransaction = new Transaction
        {
            Reference = "TXN-TEST-001",
            IdempotencyKey = Guid.NewGuid().ToString(),
            Status = TransactionStatus.Success,
            CreatedAt = DateTimeOffset.UtcNow
        };
        ctx.Accounts.Add(seedAccount);
        ctx.Transactions.Add(seedTransaction);
        await ctx.SaveChangesAsync();

        ctx.LedgerEntries.Add(new LedgerEntry
        {
            TransactionId = seedTransaction.Id,
            AccountId = seedAccount.Id,
            EntryType = EntryType.Credit,
            Amount = 250m,
            Currency = seedAccount.CurrencyCode,
            CreatedAt = DateTimeOffset.UtcNow
        });

        ctx.LedgerEntries.Add(new LedgerEntry
        {
            TransactionId = seedTransaction.Id,
            AccountId = seedAccount.Id,
            EntryType = EntryType.Debit,
            Amount = 50m,
            Currency = seedAccount.CurrencyCode,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await ctx.SaveChangesAsync();

        var service = new AccountService(ctx, TestAudit.For(ctx), TestCaller.Default);
        var response = await service.GetBalanceAsync("NGN-TEST-SYS-01");
        Assert.Equal(200m, response.Balance);

    }

    [Fact]
    public async Task AccountAsync_GetBalanceAsync_NotFoundPath()
    {
        await using var ctx = _fixture.CreateContext();


        var service = new AccountService(ctx, TestAudit.For(ctx), TestCaller.Default);

        var response = await service.GetBalanceAsync("0000000000");

        Assert.Null(response);

    }

    [Fact]
    public async Task CreateAccountAsync_RecordsAuditRow()
    {
        await using var ctx = _fixture.CreateContext();

        var account = await new AccountService(ctx, TestAudit.For(ctx), TestCaller.Default)
            .CreateAccountAsync(new CreateAccountRequest("Audit Customer", "Savings", "NGN", Guid.NewGuid().ToString()));

        await using var verifyCtx = _fixture.CreateContext();
        var audit = await verifyCtx.AuditLogs.SingleAsync();
        Assert.Equal(AuditActions.CustomerAccountCreated, audit.Action);
        Assert.Equal(nameof(Account), audit.EntityType);
        Assert.Equal(account.AccountNumber, audit.EntityId);
        Assert.Equal("NGN", audit.Currency);
    }

    // Guards the orchestrator-retry scenario: a timed-out create, retried with the same idempotency key,
    // must return the account it already created instead of a second one.
    [Fact]
    public async Task CreateAccountAsync_RetriedWithTheSameKeyAndBody_ReturnsTheSameAccount_AndCreatesOnlyOne()
    {
        var request = new CreateAccountRequest("Retry Customer", "Savings", "NGN", "order-42");

        await using var ctx1 = _fixture.CreateContext();
        var first = await new AccountService(ctx1, TestAudit.For(ctx1), TestCaller.Default).CreateAccountAsync(request);

        await using var ctx2 = _fixture.CreateContext();
        var retry = await new AccountService(ctx2, TestAudit.For(ctx2), TestCaller.Default).CreateAccountAsync(request);

        Assert.Equal(first.AccountNumber, retry.AccountNumber);

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(1, await verifyCtx.Accounts.CountAsync());
    }

    [Fact]
    public async Task CreateAccountAsync_RetryWithDifferentAccountTypeCasing_IsTheSameRequest()
    {
        await using var ctx1 = _fixture.CreateContext();
        var first = await new AccountService(ctx1, TestAudit.For(ctx1), TestCaller.Default)
            .CreateAccountAsync(new CreateAccountRequest("Retry Customer", "Savings", "NGN", "order-42"));

        await using var ctx2 = _fixture.CreateContext();
        var retry = await new AccountService(ctx2, TestAudit.For(ctx2), TestCaller.Default)
            .CreateAccountAsync(new CreateAccountRequest("Retry Customer", "savings", "NGN", "order-42"));

        Assert.Equal(first.AccountNumber, retry.AccountNumber);
        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(1, await verifyCtx.Accounts.CountAsync());
    }

    [Theory]
    [InlineData("Other Name", "Savings", "NGN")]
    [InlineData("Retry Customer", "Wallet", "NGN")]
    [InlineData("Retry Customer", "Savings", "USD")]
    public async Task CreateAccountAsync_ReusedKeyForADifferentRequest_Throws409_AndCreatesOnlyOne(
        string accountName, string accountType, string currencyCode)
    {
        await using var ctx1 = _fixture.CreateContext();
        await new AccountService(ctx1, TestAudit.For(ctx1), TestCaller.Default)
            .CreateAccountAsync(new CreateAccountRequest("Retry Customer", "Savings", "NGN", "order-42"));

        await using var ctx2 = _fixture.CreateContext();
        await Assert.ThrowsAsync<DuplicateIdempotencyKeyException>(() =>
            new AccountService(ctx2, TestAudit.For(ctx2), TestCaller.Default)
                .CreateAccountAsync(new CreateAccountRequest(accountName, accountType, currencyCode, "order-42")));

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(1, await verifyCtx.Accounts.CountAsync());
    }

    [Fact]
    public async Task CreateAccountAsync_SameKeyFromTwoClients_CreatesTwoSeparateAccounts()
    {
        var request = new CreateAccountRequest("Retry Customer", "Savings", "NGN", "order-42");

        await using var ctx1 = _fixture.CreateContext();
        var fromA = await new AccountService(ctx1, TestAudit.For(ctx1), new TestCaller("client-a")).CreateAccountAsync(request);

        await using var ctx2 = _fixture.CreateContext();
        var fromB = await new AccountService(ctx2, TestAudit.For(ctx2), new TestCaller("client-b")).CreateAccountAsync(request);

        Assert.NotEqual(fromA.AccountNumber, fromB.AccountNumber);
    }
}
