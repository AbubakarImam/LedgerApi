namespace LedgerApi.Exceptions;

public class GlAccountNotFoundException : Exception
{
    public GlAccountNotFoundException()
    {
    }

    public GlAccountNotFoundException(string message)
        : base(message)
    {
    }

    public GlAccountNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
