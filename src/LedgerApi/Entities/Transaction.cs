namespace LedgerApi.Entities;

public class Transaction
{
    public long Id { get; set; }
    public string Reference { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public string? InitiatedBy { get; set; }
    public TransactionStatus Status { get; set; }
    public string? Narration { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<LedgerEntry> Entries { get; set; } = new List<LedgerEntry>();
    public string? FailureReason { get; set; }

    // SHA-256 of the request's business fields, so a reused idempotency key with a different body is
    // detected (decision #42). Null for transactions created before it existed and for reversals.
    public string? RequestHash { get; set; }

    // Set only on a chain leg (decision #45): the chain, the leg's 1-based position and its purpose.
    // Null for transactions outside a chain, including every row from before chains existed.
    public long? ChainId { get; set; }
    public TransactionChain? Chain { get; set; }
    public int? Sequence { get; set; }
    public LegPurpose? Purpose { get; set; }

    // Four additive, caller-supplied envelope fields (decisions #49-#52). Not part of RequestHash: none of
    // them changes which accounts money moves between or how much (decision #44's reasoning for Narration
    // applies the same way here). On a chain leg these stay null; the chain-level value on TransactionChain
    // is the one that applies to every leg (DESIGN.md Section 8).
    //
    // ValueDate defaults to the transaction's own creation date when the caller omits it, and is always
    // populated after a successful post; a caller-supplied value that does not match the creation date is
    // rejected with 422 before anything commits (decision #49), so a stored row's ValueDate is never wrong.
    public DateOnly? ValueDate { get; set; }
    public string? BranchCode { get; set; }
    public string? Channel { get; set; }
    public string? DecisionId { get; set; }
}
