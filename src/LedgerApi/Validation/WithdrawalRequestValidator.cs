using LedgerApi.Contracts.Requests;

namespace LedgerApi.Validation;

public class WithdrawalRequestValidator
{
    public IReadOnlyList<string> Validate(WithdrawalRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.CustomerAccountNumber))
        {
            errors.Add("Customer account number is required.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            errors.Add("IdempotencyKey is required.");
        }

        if (request.Amount <= 0m)
        {
            errors.Add("Amount must be greater than zero.");
        }

        return errors;
    }
}
