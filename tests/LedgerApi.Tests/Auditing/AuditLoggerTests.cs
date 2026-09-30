using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using LedgerApi.Auditing;
using LedgerApi.Entities;
using LedgerApi.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LedgerApi.Tests.Auditing;

[Collection("Postgres")]
public class AuditLoggerTests
{
    private readonly PostgresFixture _fixture;

    public AuditLoggerTests(PostgresFixture fixture) => _fixture = fixture;

    private static AuditLog NewEntry() => new()
    {
        Action = AuditActions.Transfer,
        EntityType = nameof(Transaction),
        EntityId = "TXN-TEST",
    };

    [Fact]
    public void Record_FillsRequestDetails_FromTheCurrentRequest()
    {
        using var ctx = _fixture.CreateContext();
        var httpContext = new DefaultHttpContext { TraceIdentifier = "corr-123" };
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/deposits";
        httpContext.Request.Headers.UserAgent = "ledger-tests/1.0";
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.7");
        httpContext.User = new ClaimsPrincipal(new GenericIdentity("ops-user"));
        var logger = new AuditLogger(ctx, new HttpContextAccessor { HttpContext = httpContext });

        var entry = logger.Record(NewEntry());

        Assert.Equal("ops-user", entry.ActorId);
        Assert.Equal("corr-123", entry.CorrelationId);
        Assert.Equal("POST /api/deposits", entry.Endpoint);
        Assert.Equal("10.0.0.7", entry.IpAddress);
        Assert.Equal("ledger-tests/1.0", entry.UserAgent);
        // Staged only: the caller's SaveChanges writes it.
        Assert.Equal(EntityState.Added, ctx.Entry(entry).State);
    }

    [Fact]
    public void Record_LeavesRequestDetailsNull_OutsideAnHttpRequest()
    {
        using var ctx = _fixture.CreateContext();

        var entry = TestAudit.For(ctx).Record(NewEntry());

        Assert.Null(entry.CorrelationId);
        Assert.Null(entry.Endpoint);
        Assert.Null(entry.IpAddress);
        Assert.NotEqual(default, entry.CreatedAt);
    }

    [Fact]
    public void Record_TruncatesAnOversizedUserAgent()
    {
        using var ctx = _fixture.CreateContext();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.UserAgent = new string('x', 2000);
        var logger = new AuditLogger(ctx, new HttpContextAccessor { HttpContext = httpContext });

        var entry = logger.Record(NewEntry());

        Assert.Equal(512, entry.UserAgent!.Length);
    }
}
