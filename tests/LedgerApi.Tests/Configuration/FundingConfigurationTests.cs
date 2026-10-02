using LedgerApi.Configuration;
using LedgerApi.Entities;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LedgerApi.Tests.Configuration;

// Checks the real appsettings.json against the real migrations: every configured funding account
// must exist as an active System account in the currency it is configured for.
[Collection("Api")]
public class FundingConfigurationTests(ApiFixture fixture)
{
    [Fact]
    public async Task EveryConfiguredFundingAccount_IsSeededByTheMigrations()
    {
        var funding = fixture.Factory.Services.GetRequiredService<IOptions<FundingOptions>>().Value;
        await using var ctx = fixture.CreateContext();

        var problems = new List<string>();
        foreach (var (currency, accountNumber) in funding.Accounts)
        {
            var account = await ctx.Accounts.SingleOrDefaultAsync(a => a.AccountNumber == accountNumber);
            if (account is null)
                problems.Add($"{currency}: {accountNumber} is not seeded");
            else if (account.CurrencyCode != currency || account.AccountClass != AccountClass.System)
                problems.Add($"{currency}: {accountNumber} is {account.AccountClass} in {account.CurrencyCode}");
        }

        Assert.Empty(problems);
    }
}
