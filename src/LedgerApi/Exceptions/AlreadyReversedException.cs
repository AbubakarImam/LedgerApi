namespace LedgerApi.Exceptions;

public class AlreadyReversedException : Exception
{
    public AlreadyReversedException()
    {
    }

    public AlreadyReversedException(string message)
        : base(message)
    {
    }

    public AlreadyReversedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
