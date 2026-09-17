using LedgerApi.Contracts.Requests;

namespace LedgerApi.Validation;

public class TransferRequestValidator
{
    public IReadOnlyList<string> Validate(TransferRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.DebitAccountNumber))
        {
            errors.Add("Debit account number is required.");
        }
        if (string.IsNullOrWhiteSpace(request.CreditAccountNumber))
        {
            errors.Add("Credit account number is required.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            errors.Add("IdempotencyKey is required.");
        }

        if (request.DebitAccountNumber == request.CreditAccountNumber)
        {
            errors.Add("Same accounts must be different.");
        }
        if (request.Amount <= 0m)
        {
            errors.Add("Amount must be greater than zero");
        }
        return errors;
    }
}
