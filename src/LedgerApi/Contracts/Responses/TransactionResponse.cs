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
    string? Purpose = null,
    // As stored (decisions #49-#52): ValueDate is always populated after a successful post; the other three
    // only if the caller supplied them. A reversal's own value stays whatever the reversal itself got (its
    // own creation date for ValueDate, null for the other three today, since ReversalRequest carries none).
    DateOnly? ValueDate = null,
    string? BranchCode = null,
    string? Channel = null,
    string? DecisionId = null);
