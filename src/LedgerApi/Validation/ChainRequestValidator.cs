using LedgerApi.Contracts.Requests;
using LedgerApi.Entities;

namespace LedgerApi.Validation;

// Shape rules for a chain (decision #45). Rules that need the accounts (currency, fee/tax credit a system
// account, what the payer can afford) are checked when posting and fail the chain with 422.
public class ChainRequestValidator
{
    public const int MaxLegs = 10;

    public IReadOnlyList<string> Validate(ChainRequest request)
    {
        var errors = new List<string>();

        if (IdempotencyKeyRule.Validate(request.IdempotencyKey) is { } keyError)
        {
            errors.Add(keyError);
        }

        if (request.Legs is null || request.Legs.Count == 0)
        {
            errors.Add("A chain needs at least one leg.");
            return errors;
        }

        if (request.Legs.Count > MaxLegs)
        {
            errors.Add($"A chain can have at most {MaxLegs} legs.");
        }

        var purposes = new List<LegPurpose?>();
        for (var i = 0; i < request.Legs.Count; i++)
        {
            var leg = request.Legs[i];
            var position = i + 1;
            if (leg is null)
            {
                errors.Add($"Leg {position} is missing.");
                purposes.Add(null);
                continue;
            }

            if (string.IsNullOrWhiteSpace(leg.DebitAccountNumber))
                errors.Add($"Leg {position}: debit account number is required.");
            if (string.IsNullOrWhiteSpace(leg.CreditAccountNumber))
                errors.Add($"Leg {position}: credit account number is required.");
            if (leg.DebitAccountNumber == leg.CreditAccountNumber)
                errors.Add($"Leg {position}: debit and credit accounts must be different.");
            if (leg.Amount <= 0m)
                errors.Add($"Leg {position}: amount must be greater than zero.");

            var purpose = ParsePurpose(leg.Purpose);
            if (purpose is null)
                errors.Add($"Leg {position}: purpose must be Principal, Fee or Tax.");
            purposes.Add(purpose);
        }

        if (purposes[0] is { } first && first != LegPurpose.Principal)
            errors.Add("The first leg must be the Principal.");
        if (purposes.Count(p => p == LegPurpose.Principal) > 1)
            errors.Add("A chain can have only one Principal leg.");

        return errors;
    }

    // Names only, case-insensitive: Enum.TryParse would also accept numbers such as "1".
    public static LegPurpose? ParsePurpose(string? value) =>
        Enum.GetNames<LegPurpose>().FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            is { } match ? Enum.Parse<LegPurpose>(match) : null;
}
