namespace LedgerApi.Contracts.Responses;

public record AccountResponse(
    string AccountNumber,
    string AccountName,
    string AccountType,
    string CurrencyCode,
    string Status,
    DateTimeOffset CreatedAt);
