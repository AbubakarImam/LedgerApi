namespace LedgerApi.Exceptions;

public class CurrencyMismatchException : Exception
{
    public CurrencyMismatchException()
    {
    }

    public CurrencyMismatchException(string message)
        : base(message)
    {
    }

    public CurrencyMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
