using System.Globalization;
using LedgerApi.Entities;

namespace LedgerApi.Tests.Entities;

public class SupportedCurrenciesTests
{
    // The expected text is what System.Text.Json writes, since it keeps a decimal's scale.
    [Theory]
    [InlineData("1500.00", "XOF", "1500")]
    [InlineData("1500", "JPY", "1500")]
    [InlineData("-200.00", "XAF", "-200")]
    [InlineData("0", "NGN", "0.00")]
    [InlineData("10.5", "NGN", "10.50")]
    [InlineData("10.50", "USD", "10.50")]
    public void ToCurrencyScale_UsesTheCurrencysDecimalPlaces(string amount, string currency, string expected)
    {
        var result = SupportedCurrencies.ToCurrencyScale(decimal.Parse(amount, CultureInfo.InvariantCulture), currency);

        Assert.Equal(expected, result.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("1500.50", "XOF")] // does not fit the currency: never rounded away
    [InlineData("10.005", "NGN")]
    [InlineData("12.34", "OMR")]   // unsupported currency: left as stored
    public void ToCurrencyScale_NeverChangesTheValue(string amount, string currency)
    {
        var value = decimal.Parse(amount, CultureInfo.InvariantCulture);

        var result = SupportedCurrencies.ToCurrencyScale(value, currency);

        Assert.Equal(value, result);
        Assert.Equal(amount, result.ToString(CultureInfo.InvariantCulture));
    }
}
