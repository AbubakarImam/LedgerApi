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
    public string? Narration { get; set; }
    public string? FailureReason { get; set; }
    public int? FailedSequence { get; set; }

    // Set on a chain that reverses another; unique, so a chain can be reversed at most once.
    public long? ReversesChainId { get; set; }
    public TransactionChain? ReversesChain { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<Transaction> Legs { get; set; } = new List<Transaction>();
}
