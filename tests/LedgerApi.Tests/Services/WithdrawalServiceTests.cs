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
public class WithdrawalServiceTests : IAsyncLifetime
{
    private const string FundingNumber = "NGN100000001";
    private const string CustomerNumber = "1000000001";

    private readonly PostgresFixture _fixture;

    public WithdrawalServiceTests(PostgresFixture fixture) => _fixture = fixture;

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

    private async Task SeedAccountsAsync(string customerCurrency = "NGN")
    {
        await using var ctx = _fixture.CreateContext();
        ctx.Accounts.Add(NewAccount(FundingNumber, AccountClass.System));
        ctx.Accounts.Add(NewAccount(CustomerNumber, AccountClass.Customer, customerCurrency));
        await ctx.SaveChangesAsync();
    }

    private async Task SeedCustomerBalanceAsync(decimal amount)
    {
        await using var ctx = _fixture.CreateContext();
        var customer = await ctx.Accounts.SingleAsync(a => a.AccountNumber == CustomerNumber);

        var transaction = new Transaction
        {
            Reference = "TXN-SEED-001",
            IdempotencyKey = Guid.NewGuid().ToString(),
            Status = TransactionStatus.Success,
            CreatedAt = DateTimeOffset.UtcNow
        };
        ctx.Transactions.Add(transaction);
        await ctx.SaveChangesAsync();

        ctx.LedgerEntries.Add(new LedgerEntry
        {
            TransactionId = transaction.Id,
            AccountId = customer.Id,
            EntryType = EntryType.Credit,
            Amount = amount,
            Currency = customer.CurrencyCode,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private static WithdrawalService CreateService(LedgerDbContext ctx, Dictionary<string, string>? funding = null)
    {
        var options = Options.Create(new FundingOptions
        {
            Accounts = funding ?? new Dictionary<string, string> { ["NGN"] = FundingNumber }
        });
        return new WithdrawalService(new TransferService(ctx), ctx, options);
    }

    private static async Task<decimal> BalanceAsync(LedgerDbContext ctx, string accountNumber) =>
        await ctx.LedgerEntries
            .Where(e => e.Account.AccountNumber == accountNumber)
            .SumAsync(e => e.EntryType == EntryType.Credit ? e.Amount : -e.Amount);

    [Fact]
    public async Task WithdrawAsync_DebitsCustomerAndCreditsFundingAccount_OnHappyPath()
    {
        await SeedAccountsAsync();
        await SeedCustomerBalanceAsync(500m);
        await using var ctx = _fixture.CreateContext();

        var response = await CreateService(ctx).WithdrawAsync(
            new WithdrawalRequest(CustomerNumber, 200m, "Cash out", Guid.NewGuid().ToString()));

        Assert.Equal("Success", response.Status);

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(300m, await BalanceAsync(verifyCtx, CustomerNumber));
        Assert.Equal(200m, await BalanceAsync(verifyCtx, FundingNumber));
    }

    [Fact]
    public async Task WithdrawAsync_FailsAndLeavesBalancesUntouched_WhenBalanceIsInsufficient()
    {
        await SeedAccountsAsync();
        await SeedCustomerBalanceAsync(100m);
        await using var ctx = _fixture.CreateContext();

        var response = await CreateService(ctx).WithdrawAsync(
            new WithdrawalRequest(CustomerNumber, 500m, null, Guid.NewGuid().ToString()));

        Assert.Equal("Failed", response.Status);

        await using var verifyCtx = _fixture.CreateContext();
        var transaction = await verifyCtx.Transactions.SingleAsync(t => t.Reference == response.Reference);
        Assert.Equal("Insufficient Account Balance", transaction.FailureReason);
        Assert.Equal(100m, await BalanceAsync(verifyCtx, CustomerNumber));
        Assert.Equal(0m, await BalanceAsync(verifyCtx, FundingNumber));
    }

    [Fact]
    public async Task WithdrawAsync_DebitsOnce_WhenIdempotencyKeyIsReplayed()
    {
        await SeedAccountsAsync();
        await SeedCustomerBalanceAsync(500m);
        var request = new WithdrawalRequest(CustomerNumber, 200m, "Cash out", Guid.NewGuid().ToString());

        await using var ctx1 = _fixture.CreateContext();
        var first = await CreateService(ctx1).WithdrawAsync(request);

        await using var ctx2 = _fixture.CreateContext();
        var second = await CreateService(ctx2).WithdrawAsync(request);

        Assert.Equal(first.Reference, second.Reference);
        Assert.Equal(first.Status, second.Status);

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(300m, await BalanceAsync(verifyCtx, CustomerNumber));
    }

    [Fact]
    public async Task WithdrawAsync_Throws_WhenCustomerAccountNotFound()
    {
        await using var ctx = _fixture.CreateContext();

        await Assert.ThrowsAsync<AccountNotFoundException>(() =>
            CreateService(ctx).WithdrawAsync(
                new WithdrawalRequest("0000000000", 200m, null, Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task WithdrawAsync_Throws_WhenTargetIsASystemAccount()
    {
        await SeedAccountsAsync();
        await using var ctx = _fixture.CreateContext();

        await Assert.ThrowsAsync<AccountNotFoundException>(() =>
            CreateService(ctx).WithdrawAsync(
                new WithdrawalRequest(FundingNumber, 200m, null, Guid.NewGuid().ToString())));

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(0, await verifyCtx.Transactions.CountAsync());
    }

    [Fact]
    public async Task WithdrawAsync_Throws_WhenNoFundingAccountConfiguredForCurrency()
    {
        await SeedAccountsAsync(customerCurrency: "USD");
        await using var ctx = _fixture.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(ctx).WithdrawAsync(
                new WithdrawalRequest(CustomerNumber, 200m, null, Guid.NewGuid().ToString())));
    }
}
