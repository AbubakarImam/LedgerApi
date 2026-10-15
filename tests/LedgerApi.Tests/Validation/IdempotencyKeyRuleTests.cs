using LedgerApi.Contracts.Requests;
using LedgerApi.Validation;

namespace LedgerApi.Tests.Validation;

public class IdempotencyKeyRuleTests
{
    public static TheoryData<string> ValidKeys => new()
    {
        Guid.NewGuid().ToString(),
        "order-42",
        "payment:2026-10-15:abc_1.2",
        new string('a', IdempotencyKeyRule.MaxLength),
    };

    public static TheoryData<string> InvalidKeys => new()
    {
        new string('a', IdempotencyKeyRule.MaxLength + 1),
        "has space",
        "a/b",
        "key\n",   // $ would accept this; \z does not
        "naïve",
        "<script>",
    };

    [Theory]
    [MemberData(nameof(ValidKeys))]
    public void Accepts_ValidKeys(string key) => Assert.Null(IdempotencyKeyRule.Validate(key));

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    public void Rejects_InvalidKeys(string key) =>
        Assert.Contains("must be 1-128 characters", IdempotencyKeyRule.Validate(key));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_KeepsTheRequiredMessage(string key) =>
        Assert.Equal("IdempotencyKey is required.", IdempotencyKeyRule.Validate(key));

    // Every validator that takes a key applies the rule.
    [Fact]
    public void AllFourValidators_RejectAnOverlongKey()
    {
        var key = new string('k', IdempotencyKeyRule.MaxLength + 1);

        Assert.Contains(new TransferRequestValidator().Validate(new TransferRequest("1", "2", 10m, null, key)), IsKeyError);
        Assert.Contains(new DepositRequestValidator().Validate(new DepositRequest("1", 10m, null, key)), IsKeyError);
        Assert.Contains(new WithdrawalRequestValidator().Validate(new WithdrawalRequest("1", 10m, null, key)), IsKeyError);
        Assert.Contains(new CreateAccountRequestValidator().Validate(new CreateAccountRequest("Ada", "Savings", "NGN", key)), IsKeyError);
    }

    private static bool IsKeyError(string error) => error.StartsWith("IdempotencyKey must be");
}
