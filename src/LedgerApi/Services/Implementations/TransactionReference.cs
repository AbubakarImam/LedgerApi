namespace LedgerApi.Services;

// Generates human-readable references, e.g. TXN-7KQ2M9XH4TPLW3R (transactions) or CHN-… (chains, decision #45).
// Ambiguous characters (0/O, 1/I) are left out so references can be read aloud or typed from a screenshot.
public static class TransactionReference
{
    private const string AllowedChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 15;

    public static string Generate(string prefix = "TXN")
    {
        var reference = new char[Length];
        for (int i = 0; i < Length; i++)
        {
            reference[i] = AllowedChars[Random.Shared.Next(AllowedChars.Length)];
        }
        return prefix + "-" + new string(reference);
    }
}
