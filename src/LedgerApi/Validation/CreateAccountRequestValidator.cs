using LedgerApi.Contracts.Requests;
using LedgerApi.Entities;

namespace LedgerApi.Validation;

public class CreateAccountRequestValidator
{
    public IReadOnlyList<string> Validate(CreateAccountRequest request)
    {
        var errors = new List<string>();

        if (!Enum.TryParse<AccountType>(request.AccountType, ignoreCase: true, out _))
        {
            errors.Add($"'{request.AccountType}' is not a recognized account type.");
        }

        return errors;
    }
}
