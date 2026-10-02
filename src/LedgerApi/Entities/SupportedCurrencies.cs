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

    // Gives an amount exactly the currency's number of decimal places, so it serializes as 1500 for XOF
    // and 10.50 or 0.00 for NGN. Only the scale changes, never the value: an amount that does not fit
    // (which the transfer precision check prevents) or an unknown currency is returned unchanged.
    public static decimal ToCurrencyScale(decimal amount, string currencyCode)
    {
        if (!DecimalPlaces.TryGetValue(currencyCode, out var places))
            return amount;

        var rounded = decimal.Round(amount, places);
        if (rounded != amount)
            return amount;

        // decimal.Round can remove decimal places but never adds them; adding a zero with the
        // currency's scale does (0 + 0.00m is 0.00).
        return places == 0 ? rounded : rounded + new decimal(0, 0, 0, false, (byte)places);
    }
}
