using LedgerApi.Contracts.Requests;

namespace LedgerApi.Validation;

public class TransferRequestValidator
{
    public IReadOnlyList<string> Validate(TransferRequest request)
    {
        var errors = new List<string>();
        return errors;
    }
}
