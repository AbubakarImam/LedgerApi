namespace LedgerApi.Authorization;

// Each scope is also the name of the authorization policy that requires it.
public static class LedgerScopes
{
    public const string ClaimType = "scope";

    public const string Read = "ledger.read";
    public const string Accounts = "ledger.accounts";
    public const string Transfer = "ledger.transfer";
    public const string Funding = "ledger.funding";
    public const string Reverse = "ledger.reverse";
    public const string Admin = "ledger.admin";

    public static readonly IReadOnlyList<string> All = [Read, Accounts, Transfer, Funding, Reverse, Admin];
}
