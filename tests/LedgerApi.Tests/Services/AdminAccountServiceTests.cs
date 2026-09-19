using LedgerApi.Contracts.Requests;
using LedgerApi.Entities;
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

        var service = new AdminAccountService(ctx);
        var request = new CreateAccountRequest(
            AccountName: "Test Account",
             AccountType: "Wallet",
            CurrencyCode: "NGN");

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
}
