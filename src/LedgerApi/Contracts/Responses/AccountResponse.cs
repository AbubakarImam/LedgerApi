namespace LedgerApi.Contracts.Responses;

public record AccountResponse(
    string AccountNumber,
    string AccountName,
    string AccountType,
    string AccountClass,
    string CurrencyCode,
    string Status,
    DateTimeOffset CreatedAt);
