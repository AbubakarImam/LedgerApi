namespace LedgerApi.Contracts.Requests;

public record CreateAccountRequest(
    string AccountName,
    string AccountType,
    string CurrencyCode);
