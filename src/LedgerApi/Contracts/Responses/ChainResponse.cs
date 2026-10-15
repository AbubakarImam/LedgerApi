namespace LedgerApi.Contracts.Responses;

// A chain and its legs (decision #45). A Failed chain has no legs: they were rolled back together, and
// FailedSequence says which leg was rejected.
public record ChainResponse(
    string Reference,
    string Status,
    string? Narration,
    string? FailureReason,
    int? FailedSequence,
    string? ReversesChainReference,
    string? ReversedByChainReference,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ChainLegResponse> Legs);

public record ChainLegResponse(
    int Sequence,
    string Purpose,
    string Reference,
    string DebitAccountNumber,
    string CreditAccountNumber,
    decimal Amount,
    string Currency,
    string Status);
