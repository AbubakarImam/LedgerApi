namespace LedgerApi.Entities;

// The currencies v1 accepts, with their ISO 4217 decimal places (minor units). See DESIGN.md decision #37.
// Amounts are stored as decimal(18,2), so only currencies with 0 or 2 decimal places can be supported;
// 3-decimal currencies (OMR, KWD, BHD, JOD) need the minor-unit storage noted in decision #13.
// Adding a currency also needs a seeded funding account and a Funding:Accounts entry (decision #25),
// which FundingOptionsValidator checks at startup.
public static class SupportedCurrencies
{
    public static readonly IReadOnlyDictionary<string, int> DecimalPlaces = new Dictionary<string, int>
    {
        // Two decimal places
        ["USD"] = 2, // US dollar
        ["EUR"] = 2, // Euro
        ["GBP"] = 2, // Pound sterling
        ["NGN"] = 2, // Nigerian naira
        ["CNY"] = 2, // Chinese yuan
        ["GHS"] = 2, // Ghanaian cedi
        ["SAR"] = 2, // Saudi riyal
        ["QAR"] = 2, // Qatari riyal
        ["AED"] = 2, // UAE dirham
        ["CHF"] = 2, // Swiss franc
        ["CAD"] = 2, // Canadian dollar
        ["AUD"] = 2, // Australian dollar
        ["ZAR"] = 2, // South African rand
        ["KES"] = 2, // Kenyan shilling
        ["EGP"] = 2, // Egyptian pound
        ["MAD"] = 2, // Moroccan dirham
        ["INR"] = 2, // Indian rupee

        // No decimal places: amounts must be whole numbers
        ["XOF"] = 0, // West African CFA franc
        ["XAF"] = 0, // Central African CFA franc
        ["JPY"] = 0, // Japanese yen
    };

    // Codes are matched exactly: "ngn" is rejected rather than stored and never matched again.
    public static bool IsSupported(string? currencyCode) =>
        currencyCode is not null && DecimalPlaces.ContainsKey(currencyCode);
}
