namespace LedgerApi.Contracts.Responses;

public record TransactionResponse(
    string Reference,
    string Status,
    string? Narration,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
