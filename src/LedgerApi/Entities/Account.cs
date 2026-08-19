namespace LedgerApi.Entities;

public class Account
{
    public long Id { get; set; }
    public string AccountNumber { get; set; } = null!;
    public string AccountName { get; set; } = null!;
    public AccountType AccountType { get; set; }
    public AccountClass AccountClass { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public AccountStatus Status { get; set; }
    public long? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<LedgerEntry> Entries { get; set; } = new List<LedgerEntry>();
}
