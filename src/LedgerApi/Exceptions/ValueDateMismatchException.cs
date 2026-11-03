namespace LedgerApi.Exceptions;

// Decision #49: a caller-supplied value_date that does not match the transaction's own creation date is a
// business-logic rejection (422), not a request-shape error, so this is thrown from the service layer after
// the account/balance checks already ran, not from TransferRequestValidator/ChainRequestValidator.
public class ValueDateMismatchException : Exception
{
    public ValueDateMismatchException()
    {
    }

    public ValueDateMismatchException(string message)
        : base(message)
    {
    }

    public ValueDateMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
