using LedgerApi.Data;
using LedgerApi.Entities;

namespace LedgerApi.Auditing;

public class AuditLogger(LedgerDbContext dbContext) : IAuditLogger
{
    public Task LogAsync(AuditLog entry, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
