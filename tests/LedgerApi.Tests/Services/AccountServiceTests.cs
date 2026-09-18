using LedgerApi.Contracts.Requests;
using LedgerApi.Entities;
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

        var service = new AccountService(ctx);
        var request = new CreateAccountRequest(
            AccountName: "Test Account",
            AccountType: "Wallet",
            CurrencyCode: "NGN"
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

        var service = new AccountService(ctx);
        var request = "NGN-TEST-SYS-01";

        var response = await service.GetAccountAsync(request);

        Assert.Equal(seedAccount.AccountNumber, response.AccountNumber);

    }

    [Fact]
    public async Task AccountAsync_GetAccount_NotFoundPath()
    {
        await using var ctx = _fixture.CreateContext();


        var service = new AccountService(ctx);

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

        var service = new AccountService(ctx);
        var response = await service.GetBalanceAsync("NGN-TEST-SYS-01");
        Assert.Equal(200m, response.Balance);

    }

    [Fact]
    public async Task AccountAsync_GetBalanceAsync_NotFoundPath()
    {
        await using var ctx = _fixture.CreateContext();


        var service = new AccountService(ctx);

        var response = await service.GetBalanceAsync("0000000000");

        Assert.Null(response);

    }
}
