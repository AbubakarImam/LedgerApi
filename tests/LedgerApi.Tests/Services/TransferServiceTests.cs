using LedgerApi.Contracts.Requests;
using LedgerApi.Entities;
using LedgerApi.Services;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace LedgerApi.Tests.Services;

[Collection("Postgres")]
public class TransferServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public TransferServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TransferAsync_MovesFunds_OnHappyPath()
    {
        await using var ctx = _fixture.CreateContext();

        var sourceAccount = new Account
        {
            AccountNumber = "NGN-TEST-SYS-01",
            AccountName = "Test System Account",
            AccountType = AccountType.Wallet,
            AccountClass = AccountClass.System,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var destinationAccount = new Account
        {
            AccountNumber = "1000000001",
            AccountName = "Test Customer Account",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        ctx.Accounts.AddRange(sourceAccount,  destinationAccount);
        await ctx.SaveChangesAsync();

        var service = new TransferService(ctx);
        var request = new TransferRequest(
            DebitAccountNumber: sourceAccount.AccountNumber,
            CreditAccountNumber: destinationAccount.AccountNumber,
            Amount: 100m,
            Narration: "Happy path test",
            IdempotencyKey: Guid.NewGuid().ToString());

        var response = await service.TransferAsync(request);

        Assert.Equal("Success", response.Status);
        Assert.Null(response.FailureReason);

        await using var verifyCtx = _fixture.CreateContext();
        var entries = await verifyCtx.LedgerEntries.ToListAsync();
        Assert.Equal(2, entries.Count);

        var debitEntry = entries.Single(e => e.AccountId == sourceAccount.Id);
        Assert.Equal(EntryType.Debit,  debitEntry.EntryType);
        Assert.Equal(100m, debitEntry.Amount);

        var creditEntry = entries.Single(e => e.AccountId == destinationAccount.Id);
        Assert.Equal(EntryType.Credit, creditEntry.EntryType);
        Assert.Equal(100m, creditEntry.Amount);

        var sourceBalance = entries.Where(e => e.AccountId == sourceAccount.Id)
            .Sum(e => e.EntryType == EntryType.Credit ? e.Amount : -e.Amount);
        var destinationBalance = entries.Where(e => e.AccountId == destinationAccount.Id)
            .Sum(e => e.EntryType == EntryType.Credit ? e.Amount : -e.Amount);

        Assert.Equal(-100m,  sourceBalance);
        Assert.Equal(100m, destinationBalance);


    }

    [Fact]
    public async Task TransferAsync_MovesFunds_OnInsufficientBalancePath()
    {
        await using var ctx = _fixture.CreateContext();

        var sourceAccount = new Account
        {
            AccountNumber = "1000000002",
            AccountName = "Test Customer Account2",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var destinationAccount = new Account
        {
            AccountNumber = "1000000001",
            AccountName = "Test Customer Account",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        ctx.Accounts.AddRange(sourceAccount, destinationAccount);
        await ctx.SaveChangesAsync();

        var service = new TransferService(ctx);
        var request = new TransferRequest(
            DebitAccountNumber: sourceAccount.AccountNumber,
            CreditAccountNumber: destinationAccount.AccountNumber,
            Amount: 100m,
            Narration: "Insufficient balance test",
            IdempotencyKey: Guid.NewGuid().ToString());

        var response = await service.TransferAsync(request);
        Assert.Equal("Failed", response.Status);
        Assert.Equal("Insufficient Account Balance", response.FailureReason);

        await using var verifyCtx = _fixture.CreateContext();
        var transaction = await verifyCtx.Transactions.SingleAsync(t => t.Reference == response.Reference);
        Assert.Equal("Insufficient Account Balance", transaction.FailureReason);

        var entries = await verifyCtx.LedgerEntries.ToListAsync();
        Assert.Empty(entries);

        
    }

    [Fact]
    public async Task TransferAsync_ReturnsSameResult_WhenIdempotencyKeyIsReplayed()
    {
        await using var ctx = _fixture.CreateContext();


        var sourceAccount = new Account
        {
            AccountNumber = "NGN-TEST-SYS-02",
            AccountName = "Test System Account",
            AccountType = AccountType.Wallet,
            AccountClass = AccountClass.System,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var destinationAccount = new Account
        {
            AccountNumber = "1000000003",
            AccountName = "Test Customer Account",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        ctx.Accounts.AddRange(sourceAccount,  destinationAccount);
        await ctx.SaveChangesAsync();

        var idempotencyKey = Guid.NewGuid().ToString();
        var request = new TransferRequest(
            DebitAccountNumber: sourceAccount.AccountNumber,
            CreditAccountNumber: destinationAccount.AccountNumber,
            Amount: 100m,
            Narration: "Idempotency replay test",
            IdempotencyKey: idempotencyKey);

        await using var ctx1 = _fixture.CreateContext();
        var firstResponse = await new TransferService(ctx1).TransferAsync(request);

        await using var ctx2 = _fixture.CreateContext();
        var secondResponse = await new TransferService(ctx2).TransferAsync(request);

        Assert.Equal(firstResponse.Reference,  secondResponse.Reference);
        Assert.Equal(firstResponse.Status,  secondResponse.Status);

        await using var verifyCtx = _fixture.CreateContext();
        var entries = await verifyCtx.LedgerEntries.ToListAsync();
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task TransferAsync_MovesFunds_OnCurrencyMismatchPath()
    {
        await using var ctx = _fixture.CreateContext();

        var sourceAccount = new Account
        {
            AccountNumber = "1000000004",
            AccountName = "Test Customer Account4",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var destinationAccount = new Account
        {
            AccountNumber = "1000000005",
            AccountName = "Test Customer Account5",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "USD",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        ctx.Accounts.AddRange(sourceAccount, destinationAccount);
        await ctx.SaveChangesAsync();

        var service = new TransferService(ctx);
        var request = new TransferRequest(
            DebitAccountNumber: sourceAccount.AccountNumber,
            CreditAccountNumber: destinationAccount.AccountNumber,
            Amount: 100m,
            Narration: "Currency Mismatch test",
            IdempotencyKey: Guid.NewGuid().ToString());

        var response = await service.TransferAsync(request);
        Assert.Equal("Failed", response.Status);

        await using var verifyCtx = _fixture.CreateContext();
        var transaction = await verifyCtx.Transactions.SingleAsync(t => t.Reference == response.Reference);
        Assert.Equal("Conflicting currency type", transaction.FailureReason);

        var entries = await verifyCtx.LedgerEntries.ToListAsync();
        Assert.Empty(entries);


    }

    [Fact]
    public async Task TransferAsync_MovesFunds_OnSourceAccountNotFoundPath()
    {
        await using var ctx = _fixture.CreateContext();

        var sourceAccount = new Account
        {
            AccountNumber = "NGN-TEST-SYS-03",
            AccountName = "Test System Account3",
            AccountType = AccountType.Wallet,
            AccountClass = AccountClass.System,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var destinationAccount = new Account
        {
            AccountNumber = "1000000005",
            AccountName = "Test Customer Account5",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        ctx.Accounts.AddRange(sourceAccount, destinationAccount);
        await ctx.SaveChangesAsync();

        var service = new TransferService(ctx);
        var request = new TransferRequest(
            DebitAccountNumber: "0000000000",
            CreditAccountNumber: destinationAccount.AccountNumber,
            Amount: 100m,
            Narration: "Source Account NotFound test",
            IdempotencyKey: Guid.NewGuid().ToString());

        var response = await service.TransferAsync(request);
       Assert.Equal("Failed", response.Status);

        await using var verifyCtx = _fixture.CreateContext();
        var transaction = await verifyCtx.Transactions.SingleAsync(t => t.Reference == response.Reference);
        Assert.Equal("Source account not found", transaction.FailureReason);

        var entries = await verifyCtx.LedgerEntries.ToListAsync();
        Assert.Empty(entries);


    }

    [Fact]
    public async Task TransferAsync_MovesFunds_OnDestinationAccountNotFoundPath()
    {
        await using var ctx = _fixture.CreateContext();

        var sourceAccount = new Account
        {
            AccountNumber = "NGN-TEST-SYS-03",
            AccountName = "Test System Account3",
            AccountType = AccountType.Wallet,
            AccountClass = AccountClass.System,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var destinationAccount = new Account
        {
            AccountNumber = "1000000005",
            AccountName = "Test Customer Account5",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        ctx.Accounts.AddRange(sourceAccount, destinationAccount);
        await ctx.SaveChangesAsync();

        var service = new TransferService(ctx);
        var request = new TransferRequest(
            DebitAccountNumber: sourceAccount.AccountNumber,
            CreditAccountNumber: "",
            Amount: 100m,
            Narration: "Destination account not found test",
            IdempotencyKey: Guid.NewGuid().ToString());

        var response = await service.TransferAsync(request);
        Assert.Equal("Failed", response.Status);

        await using var verifyCtx = _fixture.CreateContext();
        var transaction = await verifyCtx.Transactions.SingleAsync(t => t.Reference == response.Reference);
        Assert.Equal("Destination account not found", transaction.FailureReason);

        var entries = await verifyCtx.LedgerEntries.ToListAsync();
        Assert.Empty(entries);


    }

    [Fact]
    public async Task TransferAsync_MovesFunds_OnSourceAccountNotActivePath()
    {
        await using var ctx = _fixture.CreateContext();

        var sourceAccount = new Account
        {
            AccountNumber = "NGN-TEST-SYS-03",
            AccountName = "Test System Account3",
            AccountType = AccountType.Wallet,
            AccountClass = AccountClass.System,
            CurrencyCode = "NGN",
            Status = AccountStatus.Frozen,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var destinationAccount = new Account
        {
            AccountNumber = "1000000005",
            AccountName = "Test Customer Account5",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        ctx.Accounts.AddRange(sourceAccount, destinationAccount);
        await ctx.SaveChangesAsync();

        var service = new TransferService(ctx);
        var request = new TransferRequest(
            DebitAccountNumber: sourceAccount.AccountNumber,
            CreditAccountNumber: destinationAccount.AccountNumber,
            Amount: 100m,
            Narration: "Source account not active test",
            IdempotencyKey: Guid.NewGuid().ToString());

        var response = await service.TransferAsync(request);
        Assert.Equal("Failed", response.Status);

        await using var verifyCtx = _fixture.CreateContext();
        var transaction = await verifyCtx.Transactions.SingleAsync(t => t.Reference == response.Reference);
        Assert.Equal("Source account not active", transaction.FailureReason);

        var entries = await verifyCtx.LedgerEntries.ToListAsync();
        Assert.Empty(entries);


    }

    [Fact]
    public async Task TransferAsync_MovesFunds_OnDestinationAccountBlockedPath()
    {
        await using var ctx = _fixture.CreateContext();

        var sourceAccount = new Account
        {
            AccountNumber = "NGN-TEST-SYS-03",
            AccountName = "Test System Account3",
            AccountType = AccountType.Wallet,
            AccountClass = AccountClass.System,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var destinationAccount = new Account
        {
            AccountNumber = "1000000005",
            AccountName = "Test Customer Account5",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Blocked,
            CreatedAt = DateTimeOffset.UtcNow
        };

        ctx.Accounts.AddRange(sourceAccount, destinationAccount);
        await ctx.SaveChangesAsync();

        var service = new TransferService(ctx);
        var request = new TransferRequest(
            DebitAccountNumber: sourceAccount.AccountNumber,
            CreditAccountNumber: destinationAccount.AccountNumber,
            Amount: 100m,
            Narration: "Destination account blocked test",
            IdempotencyKey: Guid.NewGuid().ToString());

        var response = await service.TransferAsync(request);
        Assert.Equal("Failed", response.Status);

        await using var verifyCtx = _fixture.CreateContext();
        var transaction = await verifyCtx.Transactions.SingleAsync(t => t.Reference == response.Reference);
        Assert.Equal("Destination account is blocked", transaction.FailureReason);

        var entries = await verifyCtx.LedgerEntries.ToListAsync();
        Assert.Empty(entries);


    }

    [Fact]
    public async Task TransferAsync_MovesFunds_OnAmountMustBePositivePath()
    {
        await using var ctx = _fixture.CreateContext();

        var sourceAccount = new Account
        {
            AccountNumber = "NGN-TEST-SYS-03",
            AccountName = "Test System Account3",
            AccountType = AccountType.Wallet,
            AccountClass = AccountClass.System,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var destinationAccount = new Account
        {
            AccountNumber = "1000000005",
            AccountName = "Test Customer Account5",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        ctx.Accounts.AddRange(sourceAccount, destinationAccount);
        await ctx.SaveChangesAsync();

        var service = new TransferService(ctx);
        var request = new TransferRequest(
            DebitAccountNumber: sourceAccount.AccountNumber,
            CreditAccountNumber: destinationAccount.AccountNumber,
            Amount: -100m,
            Narration: "Amount must be positive test",
            IdempotencyKey: Guid.NewGuid().ToString());

        var response = await service.TransferAsync(request);
        Assert.Equal("Failed", response.Status);

        await using var verifyCtx = _fixture.CreateContext();
        var transaction = await verifyCtx.Transactions.SingleAsync(t => t.Reference == response.Reference);
        Assert.Equal("Amount must be positive", transaction.FailureReason);

        var entries = await verifyCtx.LedgerEntries.ToListAsync();
        Assert.Empty(entries);


    }

    [Fact]
    public async Task TransferAsync_MovesFunds_OnSystemAccountOverdraftViolationPath()
    {
        await using var ctx = _fixture.CreateContext();

        var sourceAccount = new Account
        {
            AccountNumber = "NGN-TEST-SYS-03",
            AccountName = "Test System Account3",
            AccountType = AccountType.Wallet,
            AccountClass = AccountClass.System,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var destinationAccount = new Account
        {
            AccountNumber = "1000000005",
            AccountName = "Test Customer Account5",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        ctx.Accounts.AddRange(sourceAccount, destinationAccount);
        await ctx.SaveChangesAsync();

        var service = new TransferService(ctx);
        var request = new TransferRequest(
            DebitAccountNumber: sourceAccount.AccountNumber,
            CreditAccountNumber: destinationAccount.AccountNumber,
            Amount: 2_000_000_000_000m,
            Narration: "Insufficient Account Balance",
            IdempotencyKey: Guid.NewGuid().ToString());

        var response = await service.TransferAsync(request);
        Assert.Equal("Failed", response.Status);

        await using var verifyCtx = _fixture.CreateContext();
        var transaction = await verifyCtx.Transactions.SingleAsync(t => t.Reference == response.Reference);
        Assert.Equal("Insufficient Account Balance", transaction.FailureReason);

        var entries = await verifyCtx.LedgerEntries.ToListAsync();
        Assert.Empty(entries);


    }

    [Fact]
    public async Task TransferAsync_ReturnsStoredFailure_OnIdempotentRetryOfFailedTransfer()
    {
        await using var ctx = _fixture.CreateContext();

        var sourceAccount = new Account
        {
            AccountNumber = "1000000011",
            AccountName = "Empty Customer Account",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var destinationAccount = new Account
        {
            AccountNumber = "1000000012",
            AccountName = "Test Customer Account",
            AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer,
            CurrencyCode = "NGN",
            Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        ctx.Accounts.AddRange(sourceAccount, destinationAccount);
        await ctx.SaveChangesAsync();

        var request = new TransferRequest(
            DebitAccountNumber: sourceAccount.AccountNumber,
            CreditAccountNumber: destinationAccount.AccountNumber,
            Amount: 100m,
            Narration: "Failed replay test",
            IdempotencyKey: Guid.NewGuid().ToString());

        await using var ctx1 = _fixture.CreateContext();
        var firstResponse = await new TransferService(ctx1).TransferAsync(request);

        await using var ctx2 = _fixture.CreateContext();
        var retryResponse = await new TransferService(ctx2).TransferAsync(request);

        Assert.Equal("Failed", retryResponse.Status);
        Assert.Equal(firstResponse.Reference, retryResponse.Reference);
        Assert.Equal("Insufficient Account Balance", retryResponse.FailureReason);

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(1, await verifyCtx.Transactions.CountAsync());
    }
}
