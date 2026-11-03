namespace LedgerApi.Contracts.Responses;

public record TransferResponse(
    string Reference,
    string Status,
    string? FailureReason = null,
    // Set only for a chain leg (decision #45).
    string? ChainReference = null,
    int? Sequence = null,
    string? Purpose = null,
    // As stored (decisions #49-#52): ValueDate is always populated after a successful post; the other three
    // only if the caller supplied them.
    DateOnly? ValueDate = null,
    string? BranchCode = null,
    string? Channel = null,
    string? DecisionId = null);
