using LedgerApi.Configuration;
using LedgerApi.Entities;

namespace LedgerApi.Tests.Configuration;

public class FundingOptionsValidatorTests
{
    private static FundingOptions AllCurrencies() => new()
    {
        Accounts = SupportedCurrencies.DecimalPlaces.Keys.ToDictionary(c => c, c => $"{c}100000001"),
    };

    [Fact]
    public void Passes_WhenEverySupportedCurrencyHasAFundingAccount()
    {
        Assert.True(new FundingOptionsValidator().Validate(null, AllCurrencies()).Succeeded);
    }

    [Fact]
    public void Fails_NamingTheCurrenciesWithoutAFundingAccount()
    {
        var options = AllCurrencies();
        options.Accounts.Remove("XOF");
        options.Accounts["JPY"] = " ";

        var result = new FundingOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("XOF", result.FailureMessage);
        Assert.Contains("JPY", result.FailureMessage);
        Assert.DoesNotContain("NGN", result.FailureMessage);
    }
}
