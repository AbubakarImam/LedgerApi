using LedgerApi.Contracts.Requests;

namespace LedgerApi.Validation;

public class DepositRequestValidator
{
    public IReadOnlyList<string> Validate(DepositRequest request)
    {
        var errors = new List<string>();
        return errors;
    }
}
