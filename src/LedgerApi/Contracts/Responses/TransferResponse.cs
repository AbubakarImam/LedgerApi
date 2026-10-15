namespace LedgerApi.Contracts.Responses;

public record TransferResponse(
    string Reference,
    string Status,
    string? FailureReason = null,
    // Set only for a chain leg (decision #45).
    string? ChainReference = null,
    int? Sequence = null,
    string? Purpose = null);
