using LedgerApi.Entities;

namespace LedgerApi.Auditing;

public interface IAuditLogger
{
    // Stages an audit row on the current DbContext; it is written by the caller's next SaveChanges,
    // so it commits (or rolls back) together with the change it describes.
    AuditLog Record(AuditLog entry);
}
