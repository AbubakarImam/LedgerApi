using LedgerApi.Contracts.Requests;

namespace LedgerApi.Validation;

public class ReversalRequestValidator
{
    public IReadOnlyList<string> Validate(ReversalRequest request)
    {
        var errors = new List<string>();
        return errors;
    }
}
