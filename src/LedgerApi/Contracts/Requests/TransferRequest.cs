namespace LedgerApi.Contracts.Requests;

// ValueDate/BranchCode/Channel/DecisionId (decisions #49-#52) are optional, caller-supplied envelope
// metadata: none of the four changes which accounts money moves between or how much, so none is part of
// the idempotency request hash (decision #44's reasoning for Narration applies the same way). ValueDate is
// the one with a business-rule check: a supplied value that does not match the transaction's real creation
// date is rejected with 422 from the service layer, not here (this record stays request-shape only).
public record TransferRequest(
    string DebitAccountNumber,
    string CreditAccountNumber,
    decimal Amount,
    string? Narration,
    string IdempotencyKey,
    DateOnly? ValueDate = null,
    string? BranchCode = null,
    string? Channel = null,
    string? DecisionId = null) : IIdempotentRequest;
