namespace LedgerApi.Contracts.Requests;

public record DepositRequest(
    string CustomerAccountNumber,
    decimal Amount,
    string? Narration);
