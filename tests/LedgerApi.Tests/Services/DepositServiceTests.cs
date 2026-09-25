using LedgerApi.Configuration;
using LedgerApi.Contracts.Requests;
using LedgerApi.Data;
using LedgerApi.Entities;
using LedgerApi.Exceptions;
using LedgerApi.Services;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LedgerApi.Tests.Services;

[Collection("Postgres")]
public class DepositServiceTests : IAsyncLifetime
{
    private const string FundingNumber = "NGN100000001";
    private const string CustomerNumber = "1000000001";

    private readonly PostgresFixture _fixture;

    public DepositServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Account NewAccount(string number, AccountClass accountClass, string currency = "NGN") => new()
    {
        AccountNumber = number,
        AccountName = $"Test {number}",
        AccountType = AccountType.Wallet,
        AccountClass = accountClass,
        CurrencyCode = currency,
        Status = AccountStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private async Task SeedAccountsAsync(string customerCurrency = "NGN", bool seedFunding = true)
    {
        await using var ctx = _fixture.CreateContext();
        if (seedFunding)
            ctx.Accounts.Add(NewAccount(FundingNumber, AccountClass.System));
        ctx.Accounts.Add(NewAccount(CustomerNumber, AccountClass.Customer, customerCurrency));
        await ctx.SaveChangesAsync();
    }

    private static DepositService CreateService(LedgerDbContext ctx, Dictionary<string, string>? funding = null)
    {
        var options = Options.Create(new FundingOptions
        {
            Accounts = funding ?? new Dictionary<string, string> { ["NGN"] = FundingNumber }
        });
        return new DepositService(new TransferService(ctx), ctx, options);
    }

    private static async Task<decimal> BalanceAsync(LedgerDbContext ctx, string accountNumber) =>
        await ctx.LedgerEntries
            .Where(e => e.Account.AccountNumber == accountNumber)
            .SumAsync(e => e.EntryType == EntryType.Credit ? e.Amount : -e.Amount);

    [Fact]
    public async Task DepositAsync_CreditsCustomerFromFundingAccount_OnHappyPath()
    {
        await SeedAccountsAsync();
        await using var ctx = _fixture.CreateContext();

        var response = await CreateService(ctx).DepositAsync(
            new DepositRequest(CustomerNumber, 500m, "Top up", Guid.NewGuid().ToString()));

        Assert.Equal("Success", response.Status);

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(2, await verifyCtx.LedgerEntries.CountAsync());
        Assert.Equal(500m, await BalanceAsync(verifyCtx, CustomerNumber));
        Assert.Equal(-500m, await BalanceAsync(verifyCtx, FundingNumber));
    }

    [Fact]
    public async Task DepositAsync_FundsOnce_WhenIdempotencyKeyIsReplayed()
    {
        await SeedAccountsAsync();
        var request = new DepositRequest(CustomerNumber, 500m, "Top up", Guid.NewGuid().ToString());

        await using var ctx1 = _fixture.CreateContext();
        var first = await CreateService(ctx1).DepositAsync(request);

        await using var ctx2 = _fixture.CreateContext();
        var second = await CreateService(ctx2).DepositAsync(request);

        Assert.Equal(first.Reference, second.Reference);
        Assert.Equal(first.Status, second.Status);

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(2, await verifyCtx.LedgerEntries.CountAsync());
        Assert.Equal(500m, await BalanceAsync(verifyCtx, CustomerNumber));
    }

    [Fact]
    public async Task DepositAsync_Throws_WhenCustomerAccountNotFound()
    {
        await using var ctx = _fixture.CreateContext();

        await Assert.ThrowsAsync<AccountNotFoundException>(() =>
            CreateService(ctx).DepositAsync(
                new DepositRequest("0000000000", 500m, null, Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task DepositAsync_Throws_WhenNoFundingAccountConfiguredForCurrency()
    {
        await SeedAccountsAsync(customerCurrency: "USD");
        await using var ctx = _fixture.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(ctx).DepositAsync(
                new DepositRequest(CustomerNumber, 500m, null, Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task DepositAsync_FailsTransfer_WhenConfiguredFundingAccountDoesNotExist()
    {
        await SeedAccountsAsync(seedFunding: false);
        await using var ctx = _fixture.CreateContext();

        var response = await CreateService(ctx).DepositAsync(
            new DepositRequest(CustomerNumber, 500m, null, Guid.NewGuid().ToString()));

        Assert.Equal("Failed", response.Status);

        await using var verifyCtx = _fixture.CreateContext();
        var transaction = await verifyCtx.Transactions.SingleAsync(t => t.Reference == response.Reference);
        Assert.Equal("Source account not found", transaction.FailureReason);
        Assert.Empty(await verifyCtx.LedgerEntries.ToListAsync());
    }
}
