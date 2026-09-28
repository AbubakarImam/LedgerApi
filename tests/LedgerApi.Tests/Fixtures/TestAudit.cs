using LedgerApi.Auditing;
using LedgerApi.Data;
using Microsoft.AspNetCore.Http;

namespace LedgerApi.Tests.Fixtures;

// Service tests run outside an HTTP request, so request details on audit rows stay null.
public static class TestAudit
{
    public static IAuditLogger For(LedgerDbContext ctx) => new AuditLogger(ctx, new HttpContextAccessor());
}
