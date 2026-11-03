namespace LedgerApi.Contracts.Requests;

// POST /api/chains (decision #45). Legs post in array order; a leg's sequence is its 1-based position.
// ValueDate/BranchCode/Channel/DecisionId (decisions #49-#52) are chain-level, not per-leg: one value applies
// to every leg in the chain, matching DESIGN.md Section 8's note for transaction_chains. None of the four is
// part of the idempotency request hash, for the same reason Narration is excluded (decision #44).
public record ChainRequest(
    string IdempotencyKey,
    string? Narration,
    IReadOnlyList<ChainLegRequest> Legs,
    DateOnly? ValueDate = null,
    string? BranchCode = null,
    string? Channel = null,
    string? DecisionId = null) : IIdempotentRequest;

// Purpose: Principal, Fee or Tax.
public record ChainLegRequest(
    string DebitAccountNumber,
    string CreditAccountNumber,
    decimal Amount,
    string Purpose);

// POST /api/chains/{reference}/reversals. The body is optional.
public record ChainReversalRequest(string? Reason);
