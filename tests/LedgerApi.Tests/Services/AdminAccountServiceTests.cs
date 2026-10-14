using LedgerApi.Contracts.Requests;
using LedgerApi.Entities;
using LedgerApi.Exceptions;
using LedgerApi.Services;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace LedgerApi.Tests.Services;

[Collection("Postgres")]
public class AdminAccountServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public AdminAccountServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AdminAccountAsync_CreateAccount_OnHappyPath()
    {
        await using var ctx = _fixture.CreateContext();

        var service = new AdminAccountService(ctx, TestAudit.For(ctx), TestCaller.Default);
        var request = new CreateAccountRequest(
            AccountName: "Test Account",
             AccountType: "Wallet",
            CurrencyCode: "NGN",
            IdempotencyKey: Guid.NewGuid().ToString());

        var response = await service.CreateSystemAccountAsync(request);
        Assert.Equal("System",  response.AccountClass);
        Assert.Equal("Active",  response.Status);
        Assert.Matches(@"^NGN\d{9}$", response.AccountNumber);

        await using var verifyCtx = _fixture.CreateContext();
        var accounts = await verifyCtx.Accounts.ToListAsync();
        var account = Assert.Single(accounts);
        Assert.Equal(response.AccountNumber, account.AccountNumber);
        Assert.Equal(AccountClass.System, account.AccountClass);
    }

    [Fact]
    public async Task AdminAccountAsync_RetriedWithTheSameKeyAndBody_ReturnsTheSameAccount_AndCreatesOnlyOne()
    {
        var request = new CreateAccountRequest("Test Account", "Wallet", "NGN", "order-42");

        await using var ctx1 = _fixture.CreateContext();
        var first = await new AdminAccountService(ctx1, TestAudit.For(ctx1), TestCaller.Default).CreateSystemAccountAsync(request);

        await using var ctx2 = _fixture.CreateContext();
        var retry = await new AdminAccountService(ctx2, TestAudit.For(ctx2), TestCaller.Default).CreateSystemAccountAsync(request);

        Assert.Equal(first.AccountNumber, retry.AccountNumber);

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(1, await verifyCtx.Accounts.CountAsync());
    }

    [Fact]
    public async Task AdminAccountAsync_RetryWithDifferentAccountTypeCasing_IsTheSameRequest()
    {
        await using var ctx1 = _fixture.CreateContext();
        var first = await new AdminAccountService(ctx1, TestAudit.For(ctx1), TestCaller.Default)
            .CreateSystemAccountAsync(new CreateAccountRequest("Test Account", "Wallet", "NGN", "order-42"));

        await using var ctx2 = _fixture.CreateContext();
        var retry = await new AdminAccountService(ctx2, TestAudit.For(ctx2), TestCaller.Default)
            .CreateSystemAccountAsync(new CreateAccountRequest("Test Account", "WALLET", "NGN", "order-42"));

        Assert.Equal(first.AccountNumber, retry.AccountNumber);
        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(1, await verifyCtx.Accounts.CountAsync());
    }

    [Fact]
    public async Task AdminAccountAsync_ReusedKeyForADifferentRequest_Throws409_AndCreatesOnlyOne()
    {
        await using var ctx1 = _fixture.CreateContext();
        await new AdminAccountService(ctx1, TestAudit.For(ctx1), TestCaller.Default)
            .CreateSystemAccountAsync(new CreateAccountRequest("Test Account", "Wallet", "NGN", "order-42"));

        await using var ctx2 = _fixture.CreateContext();
        await Assert.ThrowsAsync<DuplicateIdempotencyKeyException>(() =>
            new AdminAccountService(ctx2, TestAudit.For(ctx2), TestCaller.Default)
                .CreateSystemAccountAsync(new CreateAccountRequest("Different Account", "Wallet", "NGN", "order-42")));

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(1, await verifyCtx.Accounts.CountAsync());
    }
}
