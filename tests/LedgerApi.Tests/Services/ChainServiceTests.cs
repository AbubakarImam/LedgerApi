using LedgerApi.Auditing;
using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Entities;
using LedgerApi.Exceptions;
using LedgerApi.Services;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace LedgerApi.Tests.Services;

// Chains of legs (decision #45), against a real Postgres.
[Collection("Postgres")]
public class ChainServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public ChainServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // Customers A, B and C; system accounts: funding, fees and tax; plus a USD customer and USD system account.
    private const string A = "1000000101", B = "1000000102", C = "1000000103";
    private const string Funding = "NGN-CHAIN-FUND", Fees = "NGN-CHAIN-FEES", Tax = "NGN-CHAIN-TAX";
    private const string UsdCustomer = "1000000201", UsdSystem = "USD-CHAIN-SYS";

    private async Task SeedAsync()
    {
        await using var ctx = _fixture.CreateContext();
        ctx.Accounts.AddRange(
            Account(A, AccountClass.Customer), Account(B, AccountClass.Customer), Account(C, AccountClass.Customer),
            Account(Funding, AccountClass.System), Account(Fees, AccountClass.System), Account(Tax, AccountClass.System),
            Account(UsdCustomer, AccountClass.Customer, "USD"), Account(UsdSystem, AccountClass.System, "USD"));
        await ctx.SaveChangesAsync();
    }

    private static Account Account(string number, AccountClass accountClass, string currency = "NGN") => new()
    {
        AccountNumber = number, AccountName = "Chain " + number, AccountType = AccountType.Wallet,
        AccountClass = accountClass, CurrencyCode = currency, Status = AccountStatus.Active, CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task FundAsync(string account, decimal amount, string from = Funding)
    {
        await using var ctx = _fixture.CreateContext();
        var response = await new TransferService(ctx, TestAudit.For(ctx), TestCaller.Default)
            .TransferAsync(new TransferRequest(from, account, amount, "fund", Guid.NewGuid().ToString()));
        Assert.Equal("Success", response.Status);
    }

    private async Task<ChainResponse> PostAsync(string key, params ChainLegRequest[] legs)
    {
        await using var ctx = _fixture.CreateContext();
        return await new ChainService(ctx, TestAudit.For(ctx), TestCaller.Default)
            .PostAsync(new ChainRequest(key, "Rent", legs));
    }

    private static ChainLegRequest Leg(string debit, string credit, decimal amount, string purpose = "Principal") =>
        new(debit, credit, amount, purpose);

    private async Task<decimal> BalanceAsync(string accountNumber)
    {
        await using var ctx = _fixture.CreateContext();
        return await ctx.LedgerEntries.Where(e => e.Account.AccountNumber == accountNumber)
            .SumAsync(e => e.EntryType == EntryType.Credit ? e.Amount : -e.Amount);
    }

    private async Task<int> ChainEntryCountAsync()
    {
        await using var ctx = _fixture.CreateContext();
        return await ctx.LedgerEntries.CountAsync(e => e.Transaction.ChainId != null);
    }

    [Fact]
    public async Task OneLegChain_PostsLikeAPlainTransfer()
    {
        await SeedAsync();
        await FundAsync(A, 1000m);

        var chain = await PostAsync("one-leg", Leg(A, B, 100m));

        Assert.Equal("Success", chain.Status);
        var leg = Assert.Single(chain.Legs);
        Assert.Equal((1, "Principal", A, B, 100.00m), (leg.Sequence, leg.Purpose, leg.DebitAccountNumber, leg.CreditAccountNumber, leg.Amount));
        Assert.Equal(900m, await BalanceAsync(A));
        Assert.Equal(100m, await BalanceAsync(B));

        await using var ctx = _fixture.CreateContext();
        var stored = await ctx.Transactions.Include(t => t.Entries).Include(t => t.Chain).SingleAsync(t => t.Reference == leg.Reference);
        Assert.Equal(2, stored.Entries.Count); // one debit, one credit, like a transfer
        Assert.Equal($"chain:{stored.ChainId}:1", stored.IdempotencyKey);
        Assert.Equal(chain.Reference, stored.Chain!.Reference);
    }

    [Fact]
    public async Task ThreeLegs_PostSixEntries_WithCorrectBalances_AndAuditRows()
    {
        await SeedAsync();
        await FundAsync(A, 1000m);

        var chain = await PostAsync("three-legs", Leg(A, B, 500m), Leg(A, Fees, 10m, "Fee"), Leg(A, Tax, 2m, "Tax"));

        Assert.Equal("Success", chain.Status);
        Assert.Equal(new[] { "Principal", "Fee", "Tax" }, chain.Legs.Select(l => l.Purpose));
        Assert.Equal(6, await ChainEntryCountAsync());
        Assert.Equal(488m, await BalanceAsync(A));
        Assert.Equal(500m, await BalanceAsync(B));
        Assert.Equal(10m, await BalanceAsync(Fees));
        Assert.Equal(2m, await BalanceAsync(Tax));

        await using var ctx = _fixture.CreateContext();
        var legReferences = chain.Legs.Select(l => l.Reference).ToList();
        Assert.Equal(3, await ctx.AuditLogs.CountAsync(a => a.Action == AuditActions.Transfer && legReferences.Contains(a.EntityId)));
        Assert.Equal(1, await ctx.AuditLogs.CountAsync(a => a.Action == AuditActions.ChainPosted && a.EntityId == chain.Reference));
    }

    [Fact]
    public async Task RejectedMiddleLeg_LeavesNothingPosted_StoresTheFailedChain_AndARetryReturnsIt()
    {
        await SeedAsync();
        await FundAsync(A, 1000m);
        var legs = new[] { Leg(A, B, 500m), Leg(A, "NGN-DOES-NOT-EXIST", 10m, "Fee"), Leg(A, Tax, 2m, "Tax") };

        var failed = await PostAsync("middle-fails", legs);

        Assert.Equal("Failed", failed.Status);
        Assert.Equal(2, failed.FailedSequence);
        Assert.Equal("Destination account not found", failed.FailureReason);
        Assert.Empty(failed.Legs);
        Assert.Equal(0, await ChainEntryCountAsync());
        Assert.Equal(1000m, await BalanceAsync(A));
        Assert.Equal(0m, await BalanceAsync(B));

        var retry = await PostAsync("middle-fails", legs);

        Assert.Equal(failed.Reference, retry.Reference);
        Assert.Equal("Failed", retry.Status);
        await using var ctx = _fixture.CreateContext();
        Assert.Equal(0, await ctx.Transactions.CountAsync(t => t.ChainId != null));
        Assert.Equal(1, await ctx.AuditLogs.CountAsync(a => a.Action == AuditActions.ChainPosted && a.EntityId == failed.Reference && a.Status == "Failed"));
    }

    [Fact]
    public async Task PayerWhoAffordsThePrincipalButNotPrincipalPlusFeeAndTax_Fails_NothingPosted()
    {
        await SeedAsync();
        await FundAsync(A, 100m);

        var chain = await PostAsync("cannot-afford", Leg(A, B, 100m), Leg(A, Fees, 5m, "Fee"), Leg(A, Tax, 1m, "Tax"));

        Assert.Equal("Failed", chain.Status);
        Assert.Equal("Insufficient Account Balance", chain.FailureReason);
        Assert.Equal(2, chain.FailedSequence); // the leg where the running total first exceeds the balance
        Assert.Equal(100m, await BalanceAsync(A));
        Assert.Equal(0, await ChainEntryCountAsync());
    }

    [Fact]
    public async Task MoneyReceivedEarlierInTheChain_DoesNotCount_TowardsWhatAPayerCanAfford()
    {
        // Agreed rule (decision #45): B must already afford what it pays; the 100 it receives in leg 1 does not count.
        await SeedAsync();
        await FundAsync(A, 1000m);

        var chain = await PostAsync("no-incoming-credit", Leg(A, B, 100m), Leg(B, Fees, 5m, "Fee"));

        Assert.Equal("Failed", chain.Status);
        Assert.Equal(2, chain.FailedSequence);
        Assert.Equal("Insufficient Account Balance", chain.FailureReason);
    }

    [Fact]
    public async Task SameKey_WithAChangedFee_Returns409()
    {
        await SeedAsync();
        await FundAsync(A, 1000m);
        await PostAsync("changed-fee", Leg(A, B, 100m), Leg(A, Fees, 5m, "Fee"));

        await Assert.ThrowsAsync<DuplicateIdempotencyKeyException>(() =>
            PostAsync("changed-fee", Leg(A, B, 100m), Leg(A, Fees, 6m, "Fee")));

        Assert.Equal(895m, await BalanceAsync(A));
    }

    [Fact]
    public async Task SameKey_WithTheSameLegs_ReplaysTheChain_AndPostsOnce()
    {
        await SeedAsync();
        await FundAsync(A, 1000m);

        var first = await PostAsync("replay", Leg(A, B, 100m), Leg(A, Fees, 5m, "Fee"));
        var retry = await PostAsync("replay", Leg(A, B, 100.00m), Leg(A, Fees, 5m, "Fee"));

        Assert.Equal(first.Reference, retry.Reference);
        Assert.Equal(first.Legs.Select(l => l.Reference), retry.Legs.Select(l => l.Reference));
        Assert.Equal(895m, await BalanceAsync(A));
    }

    [Fact]
    public async Task FeeLeg_ThatCreditsACustomer_FailsTheChain()
    {
        await SeedAsync();
        await FundAsync(A, 1000m);

        var chain = await PostAsync("fee-to-customer", Leg(A, B, 100m), Leg(A, C, 5m, "Fee"));

        Assert.Equal("Failed", chain.Status);
        Assert.Equal(2, chain.FailedSequence);
        Assert.Equal("Fee legs must credit a system account", chain.FailureReason);
        Assert.Equal(1000m, await BalanceAsync(A));
    }

    [Fact]
    public async Task LegsInDifferentCurrencies_FailTheChain()
    {
        await SeedAsync();
        await FundAsync(A, 1000m);
        await FundAsync(UsdCustomer, 50m, from: UsdSystem);

        var chain = await PostAsync("mixed-currency", Leg(A, B, 100m), Leg(UsdCustomer, UsdSystem, 5m, "Fee"));

        Assert.Equal("Failed", chain.Status);
        Assert.Equal(2, chain.FailedSequence);
        Assert.Equal("All legs in a chain must use the same currency", chain.FailureReason);
        Assert.Equal(1000m, await BalanceAsync(A));
    }

    [Fact]
    public async Task OppositeDirectionChainsInParallel_OverTheSameAccounts_DoNotDeadlock()
    {
        // X: A pays B, B pays the fee. Y: B pays A, A pays the fee. Both lock A and B; without one shared lock
        // order they would deadlock (Postgres would abort one with 40P01).
        await SeedAsync();
        await FundAsync(A, 10_000m);
        await FundAsync(B, 10_000m);

        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() => i % 2 == 0
            ? PostAsync($"x-{i}", Leg(A, B, 10m), Leg(B, Fees, 1m, "Fee"))
            : PostAsync($"y-{i}", Leg(B, A, 10m), Leg(A, Fees, 1m, "Fee"))));

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal("Success", r.Status));
        Assert.Equal(10_000m - 10m, await BalanceAsync(A)); // 10 x (receive 10) + 10 x (pay 10 + fee 1)
        Assert.Equal(10_000m - 10m, await BalanceAsync(B));
        Assert.Equal(20m, await BalanceAsync(Fees));
    }

    [Fact]
    public async Task TwoConcurrentFailingRequests_WithOneKey_StoreOneFailedChain_AndBothSeeIt()
    {
        // The race reviewed for decision #45: the failed chain is stored in a second save, after the rollback.
        await SeedAsync();
        var legs = new[] { Leg(A, B, 100m) }; // A has no money: the chain fails

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => PostAsync("race", legs))));

        Assert.All(results, r => Assert.Equal("Failed", r.Status));
        Assert.Equal(results[0].Reference, results[1].Reference);
        await using var ctx = _fixture.CreateContext();
        Assert.Equal(1, await ctx.TransactionChains.CountAsync());
    }

    [Fact]
    public async Task Get_ReturnsTheChainWithItsLegs_OrNullWhenUnknown()
    {
        await SeedAsync();
        await FundAsync(A, 1000m);
        var posted = await PostAsync("get", Leg(A, B, 100m), Leg(A, Fees, 5m, "Fee"));

        await using var ctx = _fixture.CreateContext();
        var service = new ChainService(ctx, TestAudit.For(ctx), TestCaller.Default);
        var chain = await service.GetAsync(posted.Reference);

        Assert.NotNull(chain);
        Assert.Equal(posted.Legs.Select(l => (l.Sequence, l.Reference, l.Amount)), chain!.Legs.Select(l => (l.Sequence, l.Reference, l.Amount)));
        Assert.Null(await service.GetAsync("CHN-DOESNOTEXIST"));
    }

    // ---- Reversal ----

    private async Task<ChainResponse> ReverseAsync(string reference, string? reason = null)
    {
        await using var ctx = _fixture.CreateContext();
        return await new ChainService(ctx, TestAudit.For(ctx), TestCaller.Default).ReverseAsync(reference, reason);
    }

    private async Task<ChainResponse> PostThreeLegChainAsync()
    {
        await SeedAsync();
        await FundAsync(A, 1000m);
        var chain = await PostAsync("to-reverse", Leg(A, B, 500m), Leg(A, Fees, 10m, "Fee"), Leg(A, Tax, 2m, "Tax"));
        Assert.Equal("Success", chain.Status);
        return chain;
    }

    [Fact]
    public async Task ChainReversal_RestoresAllBalances_InTaxFeePrincipalOrder()
    {
        var chain = await PostThreeLegChainAsync();

        var reversal = await ReverseAsync(chain.Reference, "Customer dispute");

        Assert.Equal("Success", reversal.Status);
        Assert.Equal(chain.Reference, reversal.ReversesChainReference);
        Assert.Equal(new[] { (1, "Tax"), (2, "Fee"), (3, "Principal") }, reversal.Legs.Select(l => (l.Sequence, l.Purpose)));
        Assert.Equal((B, A, 500.00m), (reversal.Legs[2].DebitAccountNumber, reversal.Legs[2].CreditAccountNumber, reversal.Legs[2].Amount));
        Assert.Equal(1000m, await BalanceAsync(A));
        Assert.Equal(0m, await BalanceAsync(B));
        Assert.Equal(0m, await BalanceAsync(Fees));
        Assert.Equal(0m, await BalanceAsync(Tax));

        await using var ctx = _fixture.CreateContext();
        Assert.Equal(3, await ctx.Reversals.CountAsync()); // one reversals row per leg: each leg is reversed at most once
        var original = await new ChainService(ctx, TestAudit.For(ctx), TestCaller.Default).GetAsync(chain.Reference);
        Assert.Equal(reversal.Reference, original!.ReversedByChainReference);
    }

    [Fact]
    public async Task ChainReversal_ASecondTime_IsRefused()
    {
        var chain = await PostThreeLegChainAsync();
        await ReverseAsync(chain.Reference);

        // The chain subtype (its own 409 title); still an AlreadyReversedException for callers that catch that.
        await Assert.ThrowsAsync<ChainAlreadyReversedException>(() => ReverseAsync(chain.Reference));

        Assert.Equal(1000m, await BalanceAsync(A));
    }

    [Fact]
    public async Task ChainReversal_WhenOneLegCannotBeReversed_RollsBackTheWholeReversal()
    {
        // B spent the principal, so its reversing leg (the last one) fails after Tax and Fee were reversed.
        var chain = await PostThreeLegChainAsync();
        await FundAsync(C, 0.01m); // C exists with money; B pays C most of what it received
        await using (var ctx = _fixture.CreateContext())
        {
            var response = await new TransferService(ctx, TestAudit.For(ctx), TestCaller.Default)
                .TransferAsync(new TransferRequest(B, C, 400m, "spend", Guid.NewGuid().ToString()));
            Assert.Equal("Success", response.Status);
        }

        await Assert.ThrowsAsync<InsufficientFundsException>(() => ReverseAsync(chain.Reference));

        Assert.Equal(488m, await BalanceAsync(A));
        Assert.Equal(10m, await BalanceAsync(Fees));
        Assert.Equal(2m, await BalanceAsync(Tax));
        await using var verify = _fixture.CreateContext();
        Assert.Equal(0, await verify.Reversals.CountAsync());
        Assert.Equal(1, await verify.TransactionChains.CountAsync()); // no reversal chain stored
    }

    [Fact]
    public async Task ChainReversal_MayDebitAFrozenAccount()
    {
        // Decision #38: money can be clawed back from a frozen account.
        var chain = await PostThreeLegChainAsync();
        await using (var ctx = _fixture.CreateContext())
        {
            await ctx.Accounts.Where(a => a.AccountNumber == B)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, AccountStatus.Frozen));
        }

        var reversal = await ReverseAsync(chain.Reference);

        Assert.Equal("Success", reversal.Status);
        Assert.Equal(0m, await BalanceAsync(B));
    }

    [Fact]
    public async Task ReversingAFailedChain_OrAReversal_IsRefused()
    {
        var chain = await PostThreeLegChainAsync();
        var reversal = await ReverseAsync(chain.Reference);
        var failed = await PostAsync("failed", Leg(C, B, 100m)); // C has no money

        await Assert.ThrowsAsync<ChainNotReversibleException>(() => ReverseAsync(failed.Reference));
        await Assert.ThrowsAsync<ChainNotReversibleException>(() => ReverseAsync(reversal.Reference));
        await Assert.ThrowsAsync<ChainNotFoundException>(() => ReverseAsync("CHN-DOESNOTEXIST"));
    }

    [Fact]
    public async Task ASingleLeg_ReversedThroughTheOldEndpoint_Is422_PointingAtTheChain()
    {
        var chain = await PostThreeLegChainAsync();

        await using var ctx = _fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<TransactionNotReversibleException>(() =>
            new ReversalService(ctx, TestAudit.For(ctx), TestCaller.Default)
                .ReverseAsync(new ReversalRequest(chain.Legs[1].Reference, null)));

        Assert.Contains($"is part of chain '{chain.Reference}'; reverse the chain", ex.Message);
        Assert.Equal(488m, await BalanceAsync(A));
    }
}
