namespace LedgerApi.Exceptions;

public class ChainNotFoundException : Exception
{
    public ChainNotFoundException()
    {
    }

    public ChainNotFoundException(string message)
        : base(message)
    {
    }

    public ChainNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
