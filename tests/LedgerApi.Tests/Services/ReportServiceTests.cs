using LedgerApi.Data;
using LedgerApi.Entities;
using LedgerApi.Services;
using LedgerApi.Tests.Fixtures;

namespace LedgerApi.Tests.Services;

[Collection("Postgres")]
public class ReportServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public ReportServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Account NewAccount(string number, AccountClass accountClass, string currency, long? glAccountId) => new()
    {
        AccountNumber = number,
        AccountName = number,
        AccountType = AccountType.Wallet,
        AccountClass = accountClass,
        CurrencyCode = currency,
        Status = AccountStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        GlAccountId = glAccountId,
    };

    private static void Post(LedgerDbContext ctx, Account debit, Account credit, decimal amount, string currency, string reference)
    {
        var transaction = new Transaction
        {
            Reference = reference,
            IdempotencyKey = Guid.NewGuid().ToString(),
            Status = TransactionStatus.Success,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.Transactions.Add(transaction);
        ctx.SaveChanges();

        ctx.LedgerEntries.AddRange(
            new LedgerEntry { TransactionId = transaction.Id, AccountId = debit.Id, EntryType = EntryType.Debit, Amount = amount, Currency = currency, CreatedAt = DateTimeOffset.UtcNow },
            new LedgerEntry { TransactionId = transaction.Id, AccountId = credit.Id, EntryType = EntryType.Credit, Amount = amount, Currency = currency, CreatedAt = DateTimeOffset.UtcNow });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task GetTrialBalanceAsync_FlatCase_SumsAccountsUnderTheSameGlAccount()
    {
        await using var ctx = _fixture.CreateContext();

        var cash = new GlAccount { Code = "1000", Name = "Cash and Cash Equivalents", AccountType = GlAccountType.Asset, CreatedAt = DateTimeOffset.UtcNow };
        var deposits = new GlAccount { Code = "2000", Name = "Customer Deposits", AccountType = GlAccountType.Liability, CreatedAt = DateTimeOffset.UtcNow };
        ctx.GlAccounts.AddRange(cash, deposits);
        await ctx.SaveChangesAsync();

        var funding = NewAccount("NGN100000001", AccountClass.System, "NGN", cash.Id);
        var customerA = NewAccount("5551111111", AccountClass.Customer, "NGN", deposits.Id);
        var customerB = NewAccount("5552222222", AccountClass.Customer, "NGN", deposits.Id);
        ctx.Accounts.AddRange(funding, customerA, customerB);
        await ctx.SaveChangesAsync();

        Post(ctx, funding, customerA, 100m, "NGN", "TXN-1"); // customerA: +100
        Post(ctx, funding, customerB, 50m, "NGN", "TXN-2");  // customerB: +50

        var report = await new ReportService(ctx).GetTrialBalanceAsync();

        var depositsEntry = Assert.Single(report.GlAccounts, g => g.Code == "2000");
        Assert.False(depositsEntry.IsHeader);
        var ngn = Assert.Single(depositsEntry.RolledUpBalances);
        Assert.Equal("NGN", ngn.CurrencyCode);
        Assert.Equal(150m, ngn.Balance);
        Assert.Equal(depositsEntry.OwnBalances, depositsEntry.RolledUpBalances);

        var cashEntry = Assert.Single(report.GlAccounts, g => g.Code == "1000");
        var cashNgn = Assert.Single(cashEntry.RolledUpBalances);
        Assert.Equal(-150m, cashNgn.Balance); // funding account paid both
    }

    [Fact]
    public async Task GetTrialBalanceAsync_HeaderChildCase_RollsUpThroughTheParent()
    {
        await using var ctx = _fixture.CreateContext();

        var header = new GlAccount { Code = "1000", Name = "Cash and Cash Equivalents", AccountType = GlAccountType.Asset, CreatedAt = DateTimeOffset.UtcNow };
        ctx.GlAccounts.Add(header);
        await ctx.SaveChangesAsync();

        var child = new GlAccount { Code = "1010", Name = "Petty Cash", AccountType = GlAccountType.Asset, ParentGlAccountId = header.Id, CreatedAt = DateTimeOffset.UtcNow };
        ctx.GlAccounts.Add(child);
        await ctx.SaveChangesAsync();

        var headerAccount = NewAccount("NGN100000001", AccountClass.System, "NGN", header.Id);
        var childAccount = NewAccount("NGN100000002", AccountClass.System, "NGN", child.Id);
        var customer = NewAccount("5551111111", AccountClass.Customer, "NGN", null);
        ctx.Accounts.AddRange(headerAccount, childAccount, customer);
        await ctx.SaveChangesAsync();

        Post(ctx, headerAccount, customer, 100m, "NGN", "TXN-1"); // header's own account pays 100
        Post(ctx, childAccount, customer, 30m, "NGN", "TXN-2");   // child's own account pays 30

        var report = await new ReportService(ctx).GetTrialBalanceAsync();

        var headerEntry = Assert.Single(report.GlAccounts, g => g.Code == "1000");
        Assert.True(headerEntry.IsHeader);
        var headerOwn = Assert.Single(headerEntry.OwnBalances);
        Assert.Equal(-100m, headerOwn.Balance);
        var headerRolledUp = Assert.Single(headerEntry.RolledUpBalances);
        Assert.Equal(-130m, headerRolledUp.Balance); // own (-100) + child's (-30)
        Assert.NotEqual(headerEntry.OwnBalances, headerEntry.RolledUpBalances);

        var childEntry = Assert.Single(report.GlAccounts, g => g.Code == "1010");
        Assert.False(childEntry.IsHeader);
        Assert.Equal(childEntry.OwnBalances, childEntry.RolledUpBalances);
        Assert.Equal(-30m, Assert.Single(childEntry.OwnBalances).Balance);
    }

    [Fact]
    public async Task GetTrialBalanceAsync_ExcludesAccountsWithNoGlAccount()
    {
        await using var ctx = _fixture.CreateContext();

        var cash = new GlAccount { Code = "1000", Name = "Cash and Cash Equivalents", AccountType = GlAccountType.Asset, CreatedAt = DateTimeOffset.UtcNow };
        ctx.GlAccounts.Add(cash);
        await ctx.SaveChangesAsync();

        var categorized = NewAccount("NGN100000001", AccountClass.System, "NGN", cash.Id);
        var uncategorized = NewAccount("5551111111", AccountClass.Customer, "NGN", null);
        ctx.Accounts.AddRange(categorized, uncategorized);
        await ctx.SaveChangesAsync();

        Post(ctx, categorized, uncategorized, 75m, "NGN", "TXN-1");

        var report = await new ReportService(ctx).GetTrialBalanceAsync();

        // Only the one real GL account shows up; the uncategorized account's leg is not double-counted
        // anywhere, not an error - decision #48 excludes it by design.
        var entry = Assert.Single(report.GlAccounts);
        Assert.Equal("1000", entry.Code);
        Assert.Equal(-75m, Assert.Single(entry.RolledUpBalances).Balance);
    }

    [Fact]
    public async Task GetTrialBalanceAsync_KeepsDifferentCurrenciesSeparate()
    {
        await using var ctx = _fixture.CreateContext();

        var cash = new GlAccount { Code = "1000", Name = "Cash and Cash Equivalents", AccountType = GlAccountType.Asset, CreatedAt = DateTimeOffset.UtcNow };
        ctx.GlAccounts.Add(cash);
        await ctx.SaveChangesAsync();

        var ngnFunding = NewAccount("NGN100000001", AccountClass.System, "NGN", cash.Id);
        var usdFunding = NewAccount("USD100000001", AccountClass.System, "USD", cash.Id);
        var ngnCustomer = NewAccount("5551111111", AccountClass.Customer, "NGN", null);
        var usdCustomer = NewAccount("5552222222", AccountClass.Customer, "USD", null);
        ctx.Accounts.AddRange(ngnFunding, usdFunding, ngnCustomer, usdCustomer);
        await ctx.SaveChangesAsync();

        Post(ctx, ngnFunding, ngnCustomer, 100m, "NGN", "TXN-1");
        Post(ctx, usdFunding, usdCustomer, 40m, "USD", "TXN-2");

        var report = await new ReportService(ctx).GetTrialBalanceAsync();

        var entry = Assert.Single(report.GlAccounts);
        Assert.Equal(2, entry.RolledUpBalances.Count);
        Assert.Equal(-100m, entry.RolledUpBalances.Single(b => b.CurrencyCode == "NGN").Balance);
        Assert.Equal(-40m, entry.RolledUpBalances.Single(b => b.CurrencyCode == "USD").Balance);
    }
}
