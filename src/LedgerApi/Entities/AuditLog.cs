namespace LedgerApi.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public string? ActorId { get; set; }
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string EntityId { get; set; } = null!;
    public string? DebitAccountNumber { get; set; }
    public string? CreditAccountNumber { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Status { get; set; }
    public string? CorrelationId { get; set; }
    public string? Endpoint { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
