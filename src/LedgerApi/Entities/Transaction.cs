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
}
