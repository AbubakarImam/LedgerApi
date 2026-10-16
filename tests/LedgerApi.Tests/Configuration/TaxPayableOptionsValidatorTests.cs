using LedgerApi.Configuration;
using LedgerApi.Entities;

namespace LedgerApi.Tests.Configuration;

public class TaxPayableOptionsValidatorTests
{
    private static TaxPayableOptions AllCurrencies() => new()
    {
        Accounts = SupportedCurrencies.DecimalPlaces.Keys.ToDictionary(c => c, c => $"{c}300000001"),
    };

    [Fact]
    public void Passes_WhenEverySupportedCurrencyHasATaxPayableAccount()
    {
        Assert.True(new TaxPayableOptionsValidator().Validate(null, AllCurrencies()).Succeeded);
    }

    [Fact]
    public void Fails_NamingTheCurrenciesWithoutATaxPayableAccount()
    {
        var options = AllCurrencies();
        options.Accounts.Remove("XOF");
        options.Accounts["JPY"] = " ";

        var result = new TaxPayableOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("XOF", result.FailureMessage);
        Assert.Contains("JPY", result.FailureMessage);
        Assert.DoesNotContain("NGN", result.FailureMessage);
    }
}
