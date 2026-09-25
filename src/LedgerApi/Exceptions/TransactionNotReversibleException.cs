namespace LedgerApi.Exceptions;

public class TransactionNotReversibleException : Exception
{
    public TransactionNotReversibleException()
    {
    }

    public TransactionNotReversibleException(string message)
        : base(message)
    {
    }

    public TransactionNotReversibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
