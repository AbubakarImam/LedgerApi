using LedgerApi.Contracts.Requests;
using LedgerApi.Entities;

namespace LedgerApi.Validation;

// Shape rules for a chain (decision #45, extended by #53). Rules that need the accounts (currency, fee/tax
// credit a system account, what the payer can afford) are checked when posting and fail the chain with 422.
public class ChainRequestValidator
{
    // Payment (decision #45), unchanged.
    public const int MaxLegs = 10;

    // Disbursement/Collection (decision #53's own explicit number).
    public const int MaxMultiEntryLegs = 1000;

    // The purpose(s) a kind's legs are allowed to carry, and the message when a leg's purpose does not
    // match. Payment keeps today's exact wording; Disbursement/Collection each allow only their own purpose
    // (so a Fee/Tax leg -- or the other kind's purpose -- is rejected here, not silently allowed).
    private static readonly Dictionary<ChainKind, (LegPurpose[] Allowed, string Message)> PurposeRules = new()
    {
        [ChainKind.Payment] = (new[] { LegPurpose.Principal, LegPurpose.Fee, LegPurpose.Tax }, "purpose must be Principal, Fee or Tax"),
        [ChainKind.Disbursement] = (new[] { LegPurpose.Disbursement }, "purpose must be Disbursement"),
        [ChainKind.Collection] = (new[] { LegPurpose.Collection }, "purpose must be Collection"),
    };

    public IReadOnlyList<string> Validate(ChainRequest request)
    {
        var errors = new List<string>();

        if (IdempotencyKeyRule.Validate(request.IdempotencyKey) is { } keyError)
        {
            errors.Add(keyError);
        }

        var chainKind = ParseChainKind(request.ChainKind);
        if (chainKind is null)
        {
            errors.Add("Chain kind must be Payment, Disbursement or Collection.");
            return errors;
        }

        if (request.Legs is null || request.Legs.Count == 0)
        {
            errors.Add("A chain needs at least one leg.");
            return errors;
        }

        var maxLegs = chainKind == ChainKind.Payment ? MaxLegs : MaxMultiEntryLegs;
        if (request.Legs.Count > maxLegs)
        {
            errors.Add($"A chain can have at most {maxLegs} legs.");
        }

        var (allowedPurposes, purposeMessage) = PurposeRules[chainKind.Value];
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
            if (purpose is null || !allowedPurposes.Contains(purpose.Value))
                errors.Add($"Leg {position}: {purposeMessage}.");
            purposes.Add(purpose);
        }

        switch (chainKind.Value)
        {
            case ChainKind.Payment:
                ValidatePayment(purposes, errors);
                break;
            case ChainKind.Disbursement:
                // One funding account, many distinct recipients: every leg debits the same account; credit
                // accounts (recipients) must all be distinct -- a repeat recipient is a caller error, not a
                // legitimate second payment (decision #53's explicit owner call).
                ValidateSharedAndDistinct(request.Legs, errors,
                    sharedSelector: l => l.DebitAccountNumber, sharedNoun: "debit account",
                    distinctSelector: l => l.CreditAccountNumber, distinctNoun: "recipient");
                break;
            case ChainKind.Collection:
                // Many distinct payers, one biller/clearing account: every leg credits the same account;
                // debit accounts (payers) must all be distinct, mirroring Disbursement.
                ValidateSharedAndDistinct(request.Legs, errors,
                    sharedSelector: l => l.CreditAccountNumber, sharedNoun: "credit account",
                    distinctSelector: l => l.DebitAccountNumber, distinctNoun: "payer");
                break;
        }

        return errors;
    }

    private static void ValidatePayment(List<LegPurpose?> purposes, List<string> errors)
    {
        if (purposes[0] is { } first && first != LegPurpose.Principal)
            errors.Add("The first leg must be the Principal.");
        if (purposes.Count(p => p == LegPurpose.Principal) > 1)
            errors.Add("A chain can have only one Principal leg.");
    }

    private static void ValidateSharedAndDistinct(
        IReadOnlyList<ChainLegRequest> legs, List<string> errors,
        Func<ChainLegRequest, string?> sharedSelector, string sharedNoun,
        Func<ChainLegRequest, string?> distinctSelector, string distinctNoun)
    {
        string? sharedValue = null;
        var seenDistinctValues = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            var position = i + 1;
            if (leg is null)
                continue; // already reported as "Leg {position} is missing."

            var shared = sharedSelector(leg);
            if (!string.IsNullOrWhiteSpace(shared))
            {
                if (sharedValue is null)
                    sharedValue = shared;
                else if (shared != sharedValue)
                    errors.Add($"Leg {position}: every leg must share the same {sharedNoun} ('{sharedValue}'); this leg has '{shared}'.");
            }

            var distinct = distinctSelector(leg);
            if (!string.IsNullOrWhiteSpace(distinct))
            {
                if (!seenDistinctValues.Add(distinct))
                    errors.Add($"Leg {position}: duplicate {distinctNoun} account '{distinct}' -- each leg must have a different {distinctNoun}.");
            }
        }
    }

    // Names only, case-insensitive: Enum.TryParse would also accept numbers such as "1".
    public static LegPurpose? ParsePurpose(string? value) =>
        Enum.GetNames<LegPurpose>().FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            is { } match ? Enum.Parse<LegPurpose>(match) : null;

    // Same split as ParsePurpose, same reason: ChainRequest.ChainKind is a plain string on the wire
    // (decision #53's own fix, found by QA against the real HTTP endpoint -- a raw C# enum with no
    // JsonStringEnumConverter only accepts the ordinal integer, not "Disbursement"/"Collection").
    public static ChainKind? ParseChainKind(string? value) =>
        Enum.GetNames<ChainKind>().FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            is { } match ? Enum.Parse<ChainKind>(match) : null;
}
