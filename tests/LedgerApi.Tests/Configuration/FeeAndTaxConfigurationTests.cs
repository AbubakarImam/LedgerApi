using LedgerApi.Configuration;
using LedgerApi.Entities;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LedgerApi.Tests.Configuration;

// Checks the real appsettings.json against the real migrations (decision #46), the same way
// FundingConfigurationTests checks the funding accounts (decision #25): every configured
// fee-income/tax-payable account must exist as an Active System account in the currency it is
// configured for, and every supported currency must have exactly one of each, at the
// <CCY>200000001 / <CCY>300000001 numbers the task specifies.
[Collection("Api")]
public class FeeAndTaxConfigurationTests(ApiFixture fixture)
{
    [Fact]
    public async Task EveryConfiguredFeeIncomeAccount_IsSeededByTheMigrations()
    {
        var feeIncome = fixture.Factory.Services.GetRequiredService<IOptions<FeeIncomeOptions>>().Value;
        await using var ctx = fixture.CreateContext();

        var problems = new List<string>();
        foreach (var (currency, accountNumber) in feeIncome.Accounts)
        {
            var account = await ctx.Accounts.SingleOrDefaultAsync(a => a.AccountNumber == accountNumber);
            if (account is null)
                problems.Add($"{currency}: {accountNumber} is not seeded");
            else if (account.CurrencyCode != currency || account.AccountClass != AccountClass.System)
                problems.Add($"{currency}: {accountNumber} is {account.AccountClass} in {account.CurrencyCode}");
        }

        Assert.Empty(problems);
    }

    [Fact]
    public async Task EveryConfiguredTaxPayableAccount_IsSeededByTheMigrations()
    {
        var taxPayable = fixture.Factory.Services.GetRequiredService<IOptions<TaxPayableOptions>>().Value;
        await using var ctx = fixture.CreateContext();

        var problems = new List<string>();
        foreach (var (currency, accountNumber) in taxPayable.Accounts)
        {
            var account = await ctx.Accounts.SingleOrDefaultAsync(a => a.AccountNumber == accountNumber);
            if (account is null)
                problems.Add($"{currency}: {accountNumber} is not seeded");
            else if (account.CurrencyCode != currency || account.AccountClass != AccountClass.System)
                problems.Add($"{currency}: {accountNumber} is {account.AccountClass} in {account.CurrencyCode}");
        }

        Assert.Empty(problems);
    }

    [Fact]
    public async Task EverySupportedCurrency_HasExactlyOneActiveFeeIncomeAndTaxPayableAccount()
    {
        await using var ctx = fixture.CreateContext();

        var problems = new List<string>();
        foreach (var currency in SupportedCurrencies.DecimalPlaces.Keys)
        {
            var feeAccount = await ctx.Accounts.SingleOrDefaultAsync(a => a.AccountNumber == $"{currency}200000001");
            if (feeAccount is null || feeAccount.AccountClass != AccountClass.System || feeAccount.Status != AccountStatus.Active || feeAccount.CurrencyCode != currency)
                problems.Add($"{currency}: fee-income account {currency}200000001 missing or wrong");

            var taxAccount = await ctx.Accounts.SingleOrDefaultAsync(a => a.AccountNumber == $"{currency}300000001");
            if (taxAccount is null || taxAccount.AccountClass != AccountClass.System || taxAccount.Status != AccountStatus.Active || taxAccount.CurrencyCode != currency)
                problems.Add($"{currency}: tax-payable account {currency}300000001 missing or wrong");
        }

        Assert.Empty(problems);
    }
}
