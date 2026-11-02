namespace LedgerApi.Entities;

// The five standard chart-of-accounts categories (decision #48). Unrelated to Account.AccountType,
// which describes a posting account's own kind (Savings/Current/Wallet), not its reporting category.
public enum GlAccountType
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense
}
