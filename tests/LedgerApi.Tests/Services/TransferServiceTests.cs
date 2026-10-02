using LedgerApi.Auditing;
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

        var service = new TransferService(ctx, TestAudit.For(ctx));
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

        var service = new TransferService(ctx, TestAudit.For(ctx));
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
        var firstResponse = await new TransferService(ctx1, TestAudit.For(ctx1)).TransferAsync(request);

        await using var ctx2 = _fixture.CreateContext();
        var secondResponse = await new TransferService(ctx2, TestAudit.For(ctx2)).TransferAsync(request);

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

        var service = new TransferService(ctx, TestAudit.For(ctx));
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

        var service = new TransferService(ctx, TestAudit.For(ctx));
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

        var service = new TransferService(ctx, TestAudit.For(ctx));
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

        var service = new TransferService(ctx, TestAudit.For(ctx));
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

        var service = new TransferService(ctx, TestAudit.For(ctx));
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

        var service = new TransferService(ctx, TestAudit.For(ctx));
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

        var service = new TransferService(ctx, TestAudit.For(ctx));
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
        var firstResponse = await new TransferService(ctx1, TestAudit.For(ctx1)).TransferAsync(request);

        await using var ctx2 = _fixture.CreateContext();
        var retryResponse = await new TransferService(ctx2, TestAudit.For(ctx2)).TransferAsync(request);

        Assert.Equal("Failed", retryResponse.Status);
        Assert.Equal(firstResponse.Reference, retryResponse.Reference);
        Assert.Equal("Insufficient Account Balance", retryResponse.FailureReason);

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(1, await verifyCtx.Transactions.CountAsync());
    }

    private async Task<(Account System, Account Customer)> SeedAuditAccountsAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var system = new Account
        {
            AccountNumber = "NGN-AUDIT-SYS", AccountName = "Audit System", AccountType = AccountType.Wallet,
            AccountClass = AccountClass.System, CurrencyCode = "NGN", Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var customer = new Account
        {
            AccountNumber = "1000000050", AccountName = "Audit Customer", AccountType = AccountType.Savings,
            AccountClass = AccountClass.Customer, CurrencyCode = "NGN", Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.Accounts.AddRange(system, customer);
        await ctx.SaveChangesAsync();
        return (system, customer);
    }

    [Fact]
    public async Task TransferAsync_RecordsSuccessAuditRow_WithTheTransfer()
    {
        var (system, customer) = await SeedAuditAccountsAsync();
        await using var ctx = _fixture.CreateContext();

        var response = await new TransferService(ctx, TestAudit.For(ctx)).TransferAsync(
            new TransferRequest(system.AccountNumber, customer.AccountNumber, 250m, "Audit test", Guid.NewGuid().ToString()));

        await using var verifyCtx = _fixture.CreateContext();
        var audit = await verifyCtx.AuditLogs.SingleAsync();
        Assert.Equal(AuditActions.Transfer, audit.Action);
        Assert.Equal(nameof(Transaction), audit.EntityType);
        Assert.Equal(response.Reference, audit.EntityId);
        Assert.Equal(system.AccountNumber, audit.DebitAccountNumber);
        Assert.Equal(customer.AccountNumber, audit.CreditAccountNumber);
        Assert.Equal(250m, audit.Amount);
        Assert.Equal("NGN", audit.Currency);
        Assert.Equal("Success", audit.Status);
    }

    [Fact]
    public async Task TransferAsync_RecordsFailedAuditRow_ForACommittedBusinessFailure()
    {
        var (system, customer) = await SeedAuditAccountsAsync();
        await using var ctx = _fixture.CreateContext();

        // Customer has no funds, so this commits as Failed.
        var response = await new TransferService(ctx, TestAudit.For(ctx)).TransferAsync(
            new TransferRequest(customer.AccountNumber, system.AccountNumber, 250m, "Audit test", Guid.NewGuid().ToString()));

        await using var verifyCtx = _fixture.CreateContext();
        var audit = await verifyCtx.AuditLogs.SingleAsync();
        Assert.Equal(response.Reference, audit.EntityId);
        Assert.Equal("Failed", audit.Status);
        Assert.Equal(250m, audit.Amount);
    }

    [Fact]
    public async Task TransferAsync_DoesNotAudit_AnIdempotentReplay()
    {
        var (system, customer) = await SeedAuditAccountsAsync();
        var request = new TransferRequest(system.AccountNumber, customer.AccountNumber, 250m, "Audit test", Guid.NewGuid().ToString());

        await using (var ctx1 = _fixture.CreateContext())
            await new TransferService(ctx1, TestAudit.For(ctx1)).TransferAsync(request);
        await using (var ctx2 = _fixture.CreateContext())
            await new TransferService(ctx2, TestAudit.For(ctx2)).TransferAsync(request);

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(1, await verifyCtx.AuditLogs.CountAsync());
    }

    private async Task<(Account System, Account Customer)> SeedCurrencyAccountsAsync(string currency)
    {
        await using var ctx = _fixture.CreateContext();
        var system = new Account
        {
            AccountNumber = $"{currency}-PRECISION-SYS", AccountName = "Precision System", AccountType = AccountType.Wallet,
            AccountClass = AccountClass.System, CurrencyCode = currency, Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var customer = new Account
        {
            AccountNumber = "1000000070", AccountName = "Precision Customer", AccountType = AccountType.Wallet,
            AccountClass = AccountClass.Customer, CurrencyCode = currency, Status = AccountStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.Accounts.AddRange(system, customer);
        await ctx.SaveChangesAsync();
        return (system, customer);
    }

    [Theory]
    [InlineData("XOF", "1500.50")] // CFA franc has no decimal places
    [InlineData("JPY", "0.5")]
    [InlineData("NGN", "10.005")]  // 3 decimals: would otherwise be silently rounded to 10.01 by decimal(18,2)
    public async Task TransferAsync_Fails_WhenAmountHasMoreDecimalPlacesThanTheCurrencyAllows(string currency, string amount)
    {
        var (system, customer) = await SeedCurrencyAccountsAsync(currency);
        await using var ctx = _fixture.CreateContext();

        var response = await new TransferService(ctx, TestAudit.For(ctx)).TransferAsync(new TransferRequest(
            system.AccountNumber, customer.AccountNumber, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture),
            "Precision test", Guid.NewGuid().ToString()));

        Assert.Equal("Failed", response.Status);
        Assert.StartsWith($"Amount has more decimal places than {currency} allows", response.FailureReason);

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Empty(await verifyCtx.LedgerEntries.ToListAsync());
    }

    [Theory]
    [InlineData("XOF", "1500")]
    [InlineData("JPY", "1500.00")] // trailing zeros are still a whole number
    [InlineData("NGN", "10.05")]
    public async Task TransferAsync_Succeeds_WhenAmountFitsTheCurrency(string currency, string amount)
    {
        var (system, customer) = await SeedCurrencyAccountsAsync(currency);
        await using var ctx = _fixture.CreateContext();

        var response = await new TransferService(ctx, TestAudit.For(ctx)).TransferAsync(new TransferRequest(
            system.AccountNumber, customer.AccountNumber, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture),
            "Precision test", Guid.NewGuid().ToString()));

        Assert.Equal("Success", response.Status);
    }
}
