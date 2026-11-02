using LedgerApi.Contracts.Requests;
using LedgerApi.Entities;

namespace LedgerApi.Validation;

// Shape rules only (decision #48). Code uniqueness and whether ParentGlAccountId actually exists need
// the database, so GlAccountService checks those - same split as CreateAccountRequestValidator /
// AdminAccountService (the account-number collision retry).
public class CreateGlAccountRequestValidator
{
    public IReadOnlyList<string> Validate(CreateGlAccountRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            errors.Add("Code is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add("Name is required.");
        }

        if (!Enum.TryParse<GlAccountType>(request.AccountType, ignoreCase: true, out _))
        {
            errors.Add($"'{request.AccountType}' is not a recognized GL account type. " +
                $"Expected one of: {string.Join(", ", Enum.GetNames<GlAccountType>())}.");
        }

        if (request.ParentGlAccountId is <= 0)
        {
            errors.Add("ParentGlAccountId must be a positive id.");
        }

        return errors;
    }
}
