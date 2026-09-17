using LedgerApi.Contracts.Requests;
using LedgerApi.Validation;

namespace LedgerApi.Tests.Validation;

public class TransferRequestValidatorTests
{
    [Fact]
    public void Validate_ReturnsError_WhenAmountIsZeroOrNegative()
    {
        var validator = new TransferRequestValidator();
        var request = new TransferRequest(
            DebitAccountNumber: "10000000001",
            CreditAccountNumber: "10000000002",
            Amount: -5m,
            Narration: null,
            IdempotencyKey: "test-key-1"
            );

        var errors = validator.Validate( request );
        

        Assert.Contains(errors, e => e.Contains("Amount", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ReturnsError_WhenDebitAccountNumberIsMissing(string? debitAccountNumber)
    {
        var validator = new TransferRequestValidator();
        var request = new TransferRequest(
            DebitAccountNumber: debitAccountNumber!,
            CreditAccountNumber: "10000000002",
            Amount: 100m,
            Narration: null,
            IdempotencyKey: "test-key-1");

        var errors = validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("Debit", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ReturnsError_WhenCreditAccountNumberIsMissing(string? creditAccountNumber)
    {
        var validator = new TransferRequestValidator();
        var request = new TransferRequest(
            DebitAccountNumber: "10000000002",
            CreditAccountNumber: creditAccountNumber!,
            Amount: 100m,
            Narration: null,
            IdempotencyKey: "test-key-1");

        var errors = validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("Credit", StringComparison.OrdinalIgnoreCase));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ReturnsError_WhenIdempotencyKeyIsMissing(string? idempotencyKey)
    {
        var validator = new TransferRequestValidator();
        var request = new TransferRequest(
            DebitAccountNumber: "10000000001",
            CreditAccountNumber: "10000000002",
            Amount: 100m,
            Narration: null,
            IdempotencyKey: idempotencyKey!);

        var errors = validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void Validate_ReturnsError_WhenDebitAccountNumberEqualToCreditAccountNumber()
    {
        var validator = new TransferRequestValidator();
        var request = new TransferRequest(
            DebitAccountNumber: "10000000002",
            CreditAccountNumber: "10000000002",
            Amount: 100M,
            Narration: null,
            IdempotencyKey: "test-key-1"
            );
        var errors = validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("same", StringComparison.OrdinalIgnoreCase));
    }
}