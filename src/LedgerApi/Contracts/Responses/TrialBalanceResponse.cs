namespace LedgerApi.Contracts.Responses;

// A balance in one currency. GL accounts have no currency of their own - the accounts posted to them do,
// and different currencies are never summed together (decision #48 report; see DESIGN.md's note on this).
public record CurrencyBalance(string CurrencyCode, decimal Balance);

// OwnBalances is only the accounts tagged with this exact GlAccountId; RolledUpBalances adds every
// descendant's own balances too. For a leaf GL account (IsHeader false) the two are always equal.
public record TrialBalanceEntry(
    long GlAccountId,
    string Code,
    string Name,
    string AccountType,
    long? ParentGlAccountId,
    bool IsHeader,
    IReadOnlyList<CurrencyBalance> OwnBalances,
    IReadOnlyList<CurrencyBalance> RolledUpBalances);

public record TrialBalanceResponse(IReadOnlyList<TrialBalanceEntry> GlAccounts, DateTimeOffset GeneratedAt);
