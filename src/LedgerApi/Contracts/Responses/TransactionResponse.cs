namespace LedgerApi.Contracts.Responses;

public record TransactionResponse(
    string Reference,
    string Status,
    string? Narration,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    // Set only for a chain leg (decision #45).
    string? ChainReference = null,
    int? Sequence = null,
    string? Purpose = null);
