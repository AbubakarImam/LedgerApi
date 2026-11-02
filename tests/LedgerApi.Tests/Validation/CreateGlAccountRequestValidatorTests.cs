using LedgerApi.Contracts.Requests;
using LedgerApi.Validation;

namespace LedgerApi.Tests.Validation;

public class CreateGlAccountRequestValidatorTests
{
    private static IReadOnlyList<string> Validate(CreateGlAccountRequest request) =>
        new CreateGlAccountRequestValidator().Validate(request);

    [Theory]
    [InlineData("Asset")]
    [InlineData("Liability")]
    [InlineData("Equity")]
    [InlineData("Income")]
    [InlineData("Expense")]
    [InlineData("asset")] // accepted case-insensitively, same as CreateAccountRequestValidator's AccountType
    public void Accepts_EachOfTheFiveAccountTypes(string accountType)
    {
        var errors = Validate(new CreateGlAccountRequest("1000", "Cash", accountType, null));

        Assert.Empty(errors);
    }

    [Fact]
    public void Rejects_UnknownAccountType()
    {
        var errors = Validate(new CreateGlAccountRequest("1000", "Cash", "Crypto", null));

        Assert.Contains(errors, e => e.Contains("Crypto"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_MissingCode(string? code)
    {
        var errors = Validate(new CreateGlAccountRequest(code!, "Cash", "Asset", null));

        Assert.Contains(errors, e => e.Contains("Code"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Rejects_MissingName(string? name)
    {
        var errors = Validate(new CreateGlAccountRequest("1000", name!, "Asset", null));

        Assert.Contains(errors, e => e.Contains("Name"));
    }

    [Fact]
    public void Rejects_NonPositiveParentId()
    {
        var errors = Validate(new CreateGlAccountRequest("1000", "Cash", "Asset", 0));

        Assert.Contains(errors, e => e.Contains("ParentGlAccountId"));
    }

    [Fact]
    public void Accepts_NullParentId()
    {
        var errors = Validate(new CreateGlAccountRequest("1000", "Cash", "Asset", null));

        Assert.Empty(errors);
    }
}
