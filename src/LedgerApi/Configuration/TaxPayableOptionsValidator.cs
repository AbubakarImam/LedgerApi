using LedgerApi.Entities;
using Microsoft.Extensions.Options;

namespace LedgerApi.Configuration;

// Runs at startup (ValidateOnStart): every supported currency must have a tax-payable account
// configured. Mirrors FundingOptionsValidator (decision #25), for the new accounts added by
// decision #46.
public class TaxPayableOptionsValidator : IValidateOptions<TaxPayableOptions>
{
    public ValidateOptionsResult Validate(string? name, TaxPayableOptions options)
    {
        var missing = SupportedCurrencies.DecimalPlaces.Keys
            .Where(currency => !options.Accounts.TryGetValue(currency, out var accountNumber) || string.IsNullOrWhiteSpace(accountNumber))
            .ToList();

        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"TaxPayable:Accounts has no tax-payable account for: {string.Join(", ", missing)}.");
    }
}
