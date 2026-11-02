namespace LedgerApi.Contracts.Requests;

// GlAccountId is optional and today only read by the system-account create path (SystemAccountsController,
// decision #48); the customer-facing AccountsController ignores it, same as before this field existed.
public record CreateAccountRequest(
    string AccountName,
    string AccountType,
    string CurrencyCode,
    string IdempotencyKey,
    long? GlAccountId = null) : IIdempotentRequest;
