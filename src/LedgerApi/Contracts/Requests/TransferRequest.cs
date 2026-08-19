namespace LedgerApi.Contracts.Requests;

public record TransferRequest(
    string DebitAccountNumber,
    string CreditAccountNumber,
    decimal Amount,
    string? Narration,
    string IdempotencyKey);
