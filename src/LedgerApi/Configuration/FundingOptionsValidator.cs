using LedgerApi.Entities;
using Microsoft.Extensions.Options;

namespace LedgerApi.Configuration;

// Runs at startup (ValidateOnStart): every supported currency must have a funding account configured,
// otherwise a deposit or withdrawal in that currency would fail with a 500 at request time.
public class FundingOptionsValidator : IValidateOptions<FundingOptions>
{
    public ValidateOptionsResult Validate(string? name, FundingOptions options)
    {
        var missing = SupportedCurrencies.DecimalPlaces.Keys
            .Where(currency => !options.Accounts.TryGetValue(currency, out var accountNumber) || string.IsNullOrWhiteSpace(accountNumber))
            .ToList();

        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Funding:Accounts has no funding account for: {string.Join(", ", missing)}.");
    }
}
