using LedgerApi.Configuration;
using LedgerApi.Entities;

namespace LedgerApi.Tests.Configuration;

public class FeeIncomeOptionsValidatorTests
{
    private static FeeIncomeOptions AllCurrencies() => new()
    {
        Accounts = SupportedCurrencies.DecimalPlaces.Keys.ToDictionary(c => c, c => $"{c}200000001"),
    };

    [Fact]
    public void Passes_WhenEverySupportedCurrencyHasAFeeIncomeAccount()
    {
        Assert.True(new FeeIncomeOptionsValidator().Validate(null, AllCurrencies()).Succeeded);
    }

    [Fact]
    public void Fails_NamingTheCurrenciesWithoutAFeeIncomeAccount()
    {
        var options = AllCurrencies();
        options.Accounts.Remove("XOF");
        options.Accounts["JPY"] = " ";

        var result = new FeeIncomeOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("XOF", result.FailureMessage);
        Assert.Contains("JPY", result.FailureMessage);
        Assert.DoesNotContain("NGN", result.FailureMessage);
    }
}
