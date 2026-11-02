namespace LedgerApi.Exceptions;

public class DuplicateGlAccountCodeException : Exception
{
    public DuplicateGlAccountCodeException()
    {
    }

    public DuplicateGlAccountCodeException(string message)
        : base(message)
    {
    }

    public DuplicateGlAccountCodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
