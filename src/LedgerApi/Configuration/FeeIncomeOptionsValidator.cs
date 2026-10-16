using LedgerApi.Entities;
using Microsoft.Extensions.Options;

namespace LedgerApi.Configuration;

// Runs at startup (ValidateOnStart): every supported currency must have a fee-income account
// configured. Mirrors FundingOptionsValidator (decision #25), for the new accounts added by
// decision #46.
public class FeeIncomeOptionsValidator : IValidateOptions<FeeIncomeOptions>
{
    public ValidateOptionsResult Validate(string? name, FeeIncomeOptions options)
    {
        var missing = SupportedCurrencies.DecimalPlaces.Keys
            .Where(currency => !options.Accounts.TryGetValue(currency, out var accountNumber) || string.IsNullOrWhiteSpace(accountNumber))
            .ToList();

        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"FeeIncome:Accounts has no fee-income account for: {string.Join(", ", missing)}.");
    }
}
