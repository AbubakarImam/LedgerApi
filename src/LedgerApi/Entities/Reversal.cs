namespace LedgerApi.Entities;

public class Reversal
{
    public long Id { get; set; }
    public long OriginalTransactionId { get; set; }
    public long ReversalTransactionId { get; set; }
    public string? Reason { get; set; }
    public string? ReversedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Transaction OriginalTransaction { get; set; } = null!;
    public Transaction ReversalTransaction { get; set; } = null!;
}
