using LedgerApi.Contracts.Requests;
using LedgerApi.Entities;
using LedgerApi.Exceptions;
using LedgerApi.Services;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace LedgerApi.Tests.Services;

[Collection("Postgres")]
public class ReversalServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public ReversalServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Account System, Account Customer, Account Other)> SeedAccountsAsync()
    {
        await using var ctx = _fixture.CreateContext();

        var system = NewAccount("NGN100000009", AccountClass.System);
        var customer = NewAccount("1000000001", AccountClass.Customer);
        var other = NewAccount("1000000002", AccountClass.Customer);

        ctx.Accounts.AddRange(system, customer, other);
        await ctx.SaveChangesAsync();
        return (system, customer, other);
    }

    private static Account NewAccount(string accountNumber, AccountClass accountClass) => new()
    {
        AccountNumber = accountNumber,
        AccountName = "Test " + accountNumber,
        AccountType = AccountType.Wallet,
        AccountClass = accountClass,
        CurrencyCode = "NGN",
        Status = AccountStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<string> TransferAsync(string debit, string credit, decimal amount)
    {
        await using var ctx = _fixture.CreateContext();
        var response = await new TransferService(ctx).TransferAsync(
            new TransferRequest(debit, credit, amount, "Seed transfer", Guid.NewGuid().ToString()));
        return response.Reference;
    }

    private async Task<decimal> BalanceAsync(long accountId)
    {
        await using var ctx = _fixture.CreateContext();
        return await ctx.LedgerEntries.Where(e => e.AccountId == accountId)
            .SumAsync(e => e.EntryType == EntryType.Credit ? e.Amount : -e.Amount);
    }

    private async Task<Contracts.Responses.TransactionResponse> ReverseAsync(string reference, string? reason = null)
    {
        await using var ctx = _fixture.CreateContext();
        return await new ReversalService(ctx).ReverseAsync(new ReversalRequest(reference, reason));
    }

    [Fact]
    public async Task ReverseAsync_WritesOppositeEntries_AndRestoresBalances()
    {
        var (system, customer, _) = await SeedAccountsAsync();
        var originalReference = await TransferAsync(system.AccountNumber, customer.AccountNumber, 100m);

        var response = await ReverseAsync(originalReference, "Customer dispute");

        Assert.Equal("Success", response.Status);
        Assert.NotEqual(originalReference, response.Reference);
        Assert.Equal("Customer dispute", response.Narration);
        Assert.Equal(0m, await BalanceAsync(system.Id));
        Assert.Equal(0m, await BalanceAsync(customer.Id));

        await using var verifyCtx = _fixture.CreateContext();
        var reversal = await verifyCtx.Reversals
            .Include(r => r.OriginalTransaction)
            .Include(r => r.ReversalTransaction).ThenInclude(t => t.Entries)
            .SingleAsync();
        Assert.Equal(originalReference, reversal.OriginalTransaction.Reference);
        Assert.Equal(response.Reference, reversal.ReversalTransaction.Reference);

        var entries = reversal.ReversalTransaction.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal(EntryType.Credit, entries.Single(e => e.AccountId == system.Id).EntryType);
        Assert.Equal(EntryType.Debit, entries.Single(e => e.AccountId == customer.Id).EntryType);

        // The original transaction is untouched.
        Assert.Equal(TransactionStatus.Success, reversal.OriginalTransaction.Status);
    }

    [Fact]
    public async Task ReverseAsync_DefaultsNarration_WhenNoReasonGiven()
    {
        var (system, customer, _) = await SeedAccountsAsync();
        var originalReference = await TransferAsync(system.AccountNumber, customer.AccountNumber, 100m);

        var response = await ReverseAsync(originalReference);

        Assert.Equal($"Reversal of {originalReference}", response.Narration);
    }

    [Fact]
    public async Task ReverseAsync_Throws_WhenTransactionDoesNotExist()
    {
        await Assert.ThrowsAsync<TransactionNotFoundException>(() => ReverseAsync("TXN-DOESNOTEXIST"));
    }

    [Fact]
    public async Task ReverseAsync_Throws_WhenOriginalFailed()
    {
        var (_, customer, other) = await SeedAccountsAsync();
        // Customer has no funds, so this commits as a Failed transaction.
        var failedReference = await TransferAsync(customer.AccountNumber, other.AccountNumber, 50m);

        await Assert.ThrowsAsync<TransactionNotReversibleException>(() => ReverseAsync(failedReference));
    }

    [Fact]
    public async Task ReverseAsync_Throws_WhenAlreadyReversed_AndWritesNothing()
    {
        var (system, customer, _) = await SeedAccountsAsync();
        var originalReference = await TransferAsync(system.AccountNumber, customer.AccountNumber, 100m);
        await ReverseAsync(originalReference);

        await Assert.ThrowsAsync<AlreadyReversedException>(() => ReverseAsync(originalReference));

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(1, await verifyCtx.Reversals.CountAsync());
        Assert.Equal(2, await verifyCtx.Transactions.CountAsync());
        Assert.Equal(4, await verifyCtx.LedgerEntries.CountAsync());
    }

    [Fact]
    public async Task ReverseAsync_Throws_WhenReversingAReversal()
    {
        var (system, customer, _) = await SeedAccountsAsync();
        var originalReference = await TransferAsync(system.AccountNumber, customer.AccountNumber, 100m);
        var reversal = await ReverseAsync(originalReference);

        await Assert.ThrowsAsync<TransactionNotReversibleException>(() => ReverseAsync(reversal.Reference));
    }

    [Fact]
    public async Task ReverseAsync_Throws_WhenCreditedAccountHasSpentTheFunds_AndWritesNothing()
    {
        var (system, customer, other) = await SeedAccountsAsync();
        var originalReference = await TransferAsync(system.AccountNumber, customer.AccountNumber, 100m);
        await TransferAsync(customer.AccountNumber, other.AccountNumber, 80m);

        await Assert.ThrowsAsync<InsufficientFundsException>(() => ReverseAsync(originalReference));

        Assert.Equal(20m, await BalanceAsync(customer.Id));
        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(0, await verifyCtx.Reversals.CountAsync());
        Assert.Equal(2, await verifyCtx.Transactions.CountAsync());
    }

    [Fact]
    public async Task ReverseAsync_Throws_WhenAccountToDebitIsFrozen()
    {
        var (system, customer, _) = await SeedAccountsAsync();
        var originalReference = await TransferAsync(system.AccountNumber, customer.AccountNumber, 100m);

        await using (var ctx = _fixture.CreateContext())
        {
            await ctx.Accounts.Where(a => a.Id == customer.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AccountStatus.Frozen));
        }

        await Assert.ThrowsAsync<InvalidAccountStatusException>(() => ReverseAsync(originalReference));
    }

    [Fact]
    public async Task ReverseAsync_Throws_WhenAccountToCreditIsBlocked()
    {
        var (system, customer, _) = await SeedAccountsAsync();
        var originalReference = await TransferAsync(system.AccountNumber, customer.AccountNumber, 100m);

        // The reversal credits the system account back, so blocking it must stop the reversal.
        await using (var ctx = _fixture.CreateContext())
        {
            await ctx.Accounts.Where(a => a.Id == system.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AccountStatus.Blocked));
        }

        await Assert.ThrowsAsync<InvalidAccountStatusException>(() => ReverseAsync(originalReference));

        Assert.Equal(100m, await BalanceAsync(customer.Id));
    }

    [Fact]
    public async Task ReverseAsync_AllowsSystemAccountToGoNegative_AboveTheFloor()
    {
        var (system, customer, _) = await SeedAccountsAsync();
        await TransferAsync(system.AccountNumber, customer.AccountNumber, 100m);          // system -100
        var paybackReference = await TransferAsync(customer.AccountNumber, system.AccountNumber, 50m); // system -50

        // Reversing the payback debits the system account from -50 to -100.
        // A customer account would be rejected here; a system account only has to stay above the floor.
        var response = await ReverseAsync(paybackReference);

        Assert.Equal("Success", response.Status);
        Assert.Equal(-100m, await BalanceAsync(system.Id));
        Assert.Equal(100m, await BalanceAsync(customer.Id));
    }

    [Fact]
    public async Task ReverseAsync_Throws_WhenSystemAccountWouldGoBelowTheFloor()
    {
        var (system, customer, other) = await SeedAccountsAsync();
        const decimal floor = -1_000_000_000_000m;

        await TransferAsync(system.AccountNumber, customer.AccountNumber, -floor);                     // system at the floor
        var paybackReference = await TransferAsync(customer.AccountNumber, system.AccountNumber, 100m); // floor + 100
        await TransferAsync(system.AccountNumber, other.AccountNumber, 50m);                           // floor + 50

        // Reversing the payback would take the system account to floor - 50.
        await Assert.ThrowsAsync<InsufficientFundsException>(() => ReverseAsync(paybackReference));

        Assert.Equal(floor + 50m, await BalanceAsync(system.Id));
    }

    [Fact]
    public async Task ReverseAsync_ConcurrentReversals_OnlyOneSucceeds()
    {
        var (system, customer, _) = await SeedAccountsAsync();
        var originalReference = await TransferAsync(system.AccountNumber, customer.AccountNumber, 100m);

        var attempts = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await ReverseAsync(originalReference);
                    return "ok";
                }
                catch (AlreadyReversedException)
                {
                    return "already";
                }
            }));

        var results = await Task.WhenAll(attempts);

        Assert.Single(results, r => r == "ok");
        Assert.Equal(4, results.Count(r => r == "already"));
        Assert.Equal(0m, await BalanceAsync(customer.Id));
    }
}
