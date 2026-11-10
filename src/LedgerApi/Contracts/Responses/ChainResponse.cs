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
    IReadOnlyList<ChainLegResponse> Legs,
    // Chain-level, as stored (decisions #49-#52): one value applies to every leg, so these do not repeat on
    // ChainLegResponse. ValueDate is always populated after a successful post; the other three only if the
    // caller supplied them.
    DateOnly? ValueDate = null,
    string? BranchCode = null,
    string? Channel = null,
    string? DecisionId = null,
    // Payment, Disbursement or Collection (decision #53) -- always populated, every chain has a kind.
    string ChainKind = "Payment");

public record ChainLegResponse(
    int Sequence,
    string Purpose,
    string Reference,
    string DebitAccountNumber,
    string CreditAccountNumber,
    decimal Amount,
    string Currency,
    string Status);
