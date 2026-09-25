namespace LedgerApi.Contracts.Requests;

public record WithdrawalRequest(
    string CustomerAccountNumber,
    decimal Amount,
    string? Narration,
    string IdempotencyKey);
