namespace LedgerApi.Entities;

// An ordered list of legs posted together in one database transaction (decision #45). Each leg is an
// ordinary two-entry transaction with ChainId, Sequence (1-based, the leg's position) and Purpose set.
public class TransactionChain
{
    public long Id { get; set; }
    public string Reference { get; set; } = null!;
    public string? InitiatedBy { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public string? RequestHash { get; set; }
    public TransactionStatus Status { get; set; }

    // Payment (decision #45's original shape), Disbursement or Collection (decision #53). NOT NULL --
    // every chain conceptually has a kind; pre-existing rows were backfilled to Payment by the migration
    // that added this column, which is exactly what they always were.
    public ChainKind ChainKind { get; set; }

    public string? Narration { get; set; }
    public string? FailureReason { get; set; }
    public int? FailedSequence { get; set; }

    // Set on a chain that reverses another; unique, so a chain can be reversed at most once.
    public long? ReversesChainId { get; set; }
    public TransactionChain? ReversesChain { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    // Four additive, caller-supplied fields (decisions #49-#52), chain-level: one value applies to every leg,
    // so the individual leg rows in Legs do not carry their own copy. Same RequestHash exclusion and
    // ValueDate defaulting/mismatch rules as Transaction.
    public DateOnly? ValueDate { get; set; }
    public string? BranchCode { get; set; }
    public string? Channel { get; set; }
    public string? DecisionId { get; set; }

    public ICollection<Transaction> Legs { get; set; } = new List<Transaction>();
}
