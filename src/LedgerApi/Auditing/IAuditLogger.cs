using LedgerApi.Entities;

namespace LedgerApi.Auditing;

public interface IAuditLogger
{
    Task LogAsync(AuditLog entry, CancellationToken cancellationToken = default);
}
