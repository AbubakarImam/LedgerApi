namespace LedgerApi.Entities;

public class Account
{
    public long Id { get; set; }
    public string AccountNumber { get; set; } = null!;
    public string AccountName { get; set; } = null!;
    public string AccountType { get; set; } = null!;
    public string CurrencyCode { get; set; } = null!;
    public string Status { get; set; } = null!;
    public long? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<LedgerEntry> Entries { get; set; } = new List<LedgerEntry>();
}
