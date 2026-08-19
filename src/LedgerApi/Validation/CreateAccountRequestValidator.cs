using LedgerApi.Contracts.Requests;

namespace LedgerApi.Validation;

public class CreateAccountRequestValidator
{
    public IReadOnlyList<string> Validate(CreateAccountRequest request)
    {
        var errors = new List<string>();
        return errors;
    }
}
