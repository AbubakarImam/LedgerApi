using LedgerApi.Contracts.Requests;
using LedgerApi.Validation;

namespace LedgerApi.Tests.Validation;

public class WithdrawalRequestValidatorTests
{
    private readonly WithdrawalRequestValidator _validator = new();

    [Fact]
    public void Validate_ReturnsNoErrors_ForValidRequest()
    {
        var errors = _validator.Validate(new WithdrawalRequest("1000000001", 100m, null, "key-1"));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ReturnsError_WhenCustomerAccountNumberIsMissing(string? accountNumber)
    {
        var errors = _validator.Validate(new WithdrawalRequest(accountNumber!, 100m, null, "key-1"));

        Assert.Contains(errors, e => e.Contains("account number", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ReturnsError_WhenIdempotencyKeyIsMissing(string? idempotencyKey)
    {
        var errors = _validator.Validate(new WithdrawalRequest("1000000001", 100m, null, idempotencyKey!));

        Assert.Contains(errors, e => e.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_ReturnsError_WhenAmountIsZeroOrNegative(int amount)
    {
        var errors = _validator.Validate(new WithdrawalRequest("1000000001", amount, null, "key-1"));

        Assert.Contains(errors, e => e.Contains("Amount", StringComparison.OrdinalIgnoreCase));
    }
}
