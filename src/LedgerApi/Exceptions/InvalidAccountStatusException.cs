namespace LedgerApi.Exceptions;

public class InvalidAccountStatusException : Exception
{
    public InvalidAccountStatusException()
    {
    }

    public InvalidAccountStatusException(string message)
        : base(message)
    {
    }

    public InvalidAccountStatusException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
