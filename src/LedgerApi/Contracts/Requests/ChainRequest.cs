using LedgerApi.Entities;

namespace LedgerApi.Contracts.Requests;

// POST /api/chains (decision #45). Legs post in array order; a leg's sequence is its 1-based position.
// ValueDate/BranchCode/Channel/DecisionId (decisions #49-#52) are chain-level, not per-leg: one value applies
// to every leg in the chain, matching DESIGN.md Section 8's note for transaction_chains. None of the four is
// part of the idempotency request hash, for the same reason Narration is excluded (decision #44).
// ChainKind (decision #53) defaults to Payment, so every existing caller's request is unaffected; unlike
// the four fields above, ChainKind IS part of the request hash -- it changes what the request means.
public record ChainRequest(
    string IdempotencyKey,
    string? Narration,
    IReadOnlyList<ChainLegRequest> Legs,
    DateOnly? ValueDate = null,
    string? BranchCode = null,
    string? Channel = null,
    string? DecisionId = null,
    ChainKind ChainKind = ChainKind.Payment) : IIdempotentRequest;

// Purpose: Principal, Fee or Tax for a Payment chain; Disbursement for a Disbursement chain; Collection
// for a Collection chain (decision #53).
public record ChainLegRequest(
    string DebitAccountNumber,
    string CreditAccountNumber,
    decimal Amount,
    string Purpose);

// POST /api/chains/{reference}/reversals. The body is optional.
public record ChainReversalRequest(string? Reason);
