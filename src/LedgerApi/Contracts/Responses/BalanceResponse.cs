namespace LedgerApi.Contracts.Responses;

public record BalanceResponse(
    string AccountNumber,
    decimal Balance,
    string CurrencyCode,
    DateTimeOffset AsOf);
