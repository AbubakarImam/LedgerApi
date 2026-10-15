namespace LedgerApi.Exceptions;

// A chain cannot be reversed: it failed, or it is itself a reversal (decision #45). A subclass, so code
// catching TransactionNotReversibleException still does; the middleware gives it a chain-specific title.
public class ChainNotReversibleException : TransactionNotReversibleException
{
    public ChainNotReversibleException(string message)
        : base(message)
    {
    }
}
