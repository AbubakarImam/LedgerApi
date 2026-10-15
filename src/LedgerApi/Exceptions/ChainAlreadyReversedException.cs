namespace LedgerApi.Exceptions;

// A chain was already reversed (decision #45). A subclass, so code catching AlreadyReversedException still
// does; the middleware gives it a chain-specific title.
public class ChainAlreadyReversedException : AlreadyReversedException
{
    public ChainAlreadyReversedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
