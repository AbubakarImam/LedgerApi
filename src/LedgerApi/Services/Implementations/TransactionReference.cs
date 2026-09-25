namespace LedgerApi.Services;

// Generates human-readable transaction references, e.g. TXN-7KQ2M9XH4TPLW3R.
// Ambiguous characters (0/O, 1/I) are left out so references can be read aloud or typed from a screenshot.
public static class TransactionReference
{
    private const string AllowedChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 15;

    public static string Generate()
    {
        var reference = new char[Length];
        for (int i = 0; i < Length; i++)
        {
            reference[i] = AllowedChars[Random.Shared.Next(AllowedChars.Length)];
        }
        return "TXN-" + new string(reference);
    }
}
