using LedgerApi.Contracts.Requests;
using LedgerApi.Validation;

namespace LedgerApi.Tests.Validation;

public class CreateAccountRequestValidatorTests
{
    private static IReadOnlyList<string> Validate(string currencyCode) =>
        new CreateAccountRequestValidator().Validate(new CreateAccountRequest("Test", "Savings", currencyCode));

    [Theory]
    [InlineData("NGN")]
    [InlineData("CNY")] // yuan
    [InlineData("GHS")] // cedi
    [InlineData("SAR")] // Saudi riyal
    [InlineData("QAR")] // Qatari riyal
    [InlineData("XOF")] // West African CFA franc, 0 decimal places
    [InlineData("XAF")] // Central African CFA franc, 0 decimal places
    [InlineData("JPY")] // 0 decimal places
    public void Accepts_SupportedCurrencies(string currencyCode)
    {
        Assert.Empty(Validate(currencyCode));
    }

    [Theory]
    [InlineData("ngn")] // codes are matched exactly; lowercase would never match funding accounts or other NGN accounts
    [InlineData("OMR")] // Omani rial: 3 decimal places, cannot be stored in decimal(18,2)
    [InlineData("KWD")] // 3 decimal places
    [InlineData("XYZ")]
    [InlineData("")]
    public void Rejects_UnsupportedCurrencies(string currencyCode)
    {
        var errors = Validate(currencyCode);

        Assert.Contains(errors, e => e.Contains("not a supported currency"));
    }

    [Fact]
    public void Rejects_NullCurrency()
    {
        var errors = new CreateAccountRequestValidator().Validate(new CreateAccountRequest("Test", "Savings", null!));

        Assert.Contains(errors, e => e.Contains("not a supported currency"));
    }
}
