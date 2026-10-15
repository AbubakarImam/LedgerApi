namespace LedgerApi.Contracts.Requests;

// POST /api/chains (decision #45). Legs post in array order; a leg's sequence is its 1-based position.
public record ChainRequest(
    string IdempotencyKey,
    string? Narration,
    IReadOnlyList<ChainLegRequest> Legs) : IIdempotentRequest;

// Purpose: Principal, Fee or Tax.
public record ChainLegRequest(
    string DebitAccountNumber,
    string CreditAccountNumber,
    decimal Amount,
    string Purpose);

// POST /api/chains/{reference}/reversals. The body is optional.
public record ChainReversalRequest(string? Reason);
