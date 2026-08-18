namespace LedgerApi.Entities;

public class LedgerEntry
{
    public long Id { get; set; }
    public long TransactionId { get; set; }
    public long AccountId { get; set; }
    public EntryType EntryType { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public Transaction Transaction { get; set; } = null!;
    public Account Account { get; set; } = null!;
}
