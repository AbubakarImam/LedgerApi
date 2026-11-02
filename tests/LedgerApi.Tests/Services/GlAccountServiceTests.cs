using LedgerApi.Auditing;
using LedgerApi.Contracts.Requests;
using LedgerApi.Exceptions;
using LedgerApi.Services;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace LedgerApi.Tests.Services;

[Collection("Postgres")]
public class GlAccountServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public GlAccountServiceTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateGlAccountAsync_OnHappyPath_PersistsTheRow()
    {
        await using var ctx = _fixture.CreateContext();
        var service = new GlAccountService(ctx, TestAudit.For(ctx));

        var response = await service.CreateGlAccountAsync(new CreateGlAccountRequest("1000", "Cash and Cash Equivalents", "Asset", null));

        Assert.Equal("1000", response.Code);
        Assert.Equal("Asset", response.AccountType);
        Assert.Null(response.ParentGlAccountId);

        await using var verifyCtx = _fixture.CreateContext();
        var row = await verifyCtx.GlAccounts.SingleAsync();
        Assert.Equal("1000", row.Code);
        Assert.Equal("Cash and Cash Equivalents", row.Name);
    }

    [Fact]
    public async Task CreateGlAccountAsync_RecordsAuditRow()
    {
        await using var ctx = _fixture.CreateContext();
        await new GlAccountService(ctx, TestAudit.For(ctx))
            .CreateGlAccountAsync(new CreateGlAccountRequest("1000", "Cash", "Asset", null));

        await using var verifyCtx = _fixture.CreateContext();
        var audit = await verifyCtx.AuditLogs.SingleAsync();
        Assert.Equal(AuditActions.GlAccountCreated, audit.Action);
        Assert.Equal("1000", audit.EntityId);
    }

    [Fact]
    public async Task CreateGlAccountAsync_WithAHeaderParent_CreatesTheChild()
    {
        await using var ctx = _fixture.CreateContext();
        var service = new GlAccountService(ctx, TestAudit.For(ctx));

        var header = await service.CreateGlAccountAsync(new CreateGlAccountRequest("1000", "Cash and Cash Equivalents", "Asset", null));
        var child = await service.CreateGlAccountAsync(new CreateGlAccountRequest("1010", "Petty Cash", "Asset", header.Id));

        Assert.Equal(header.Id, child.ParentGlAccountId);
    }

    [Fact]
    public async Task CreateGlAccountAsync_WithADuplicateCode_Throws409_AndCreatesOnlyOne()
    {
        await using var ctx1 = _fixture.CreateContext();
        await new GlAccountService(ctx1, TestAudit.For(ctx1))
            .CreateGlAccountAsync(new CreateGlAccountRequest("1000", "Cash", "Asset", null));

        await using var ctx2 = _fixture.CreateContext();
        await Assert.ThrowsAsync<DuplicateGlAccountCodeException>(() =>
            new GlAccountService(ctx2, TestAudit.For(ctx2))
                .CreateGlAccountAsync(new CreateGlAccountRequest("1000", "A Different Name", "Liability", null)));

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(1, await verifyCtx.GlAccounts.CountAsync());
    }

    [Fact]
    public async Task CreateGlAccountAsync_WithAnUnknownParentId_Throws404_AndCreatesNothing()
    {
        await using var ctx = _fixture.CreateContext();
        var service = new GlAccountService(ctx, TestAudit.For(ctx));

        await Assert.ThrowsAsync<GlAccountNotFoundException>(() =>
            service.CreateGlAccountAsync(new CreateGlAccountRequest("1010", "Petty Cash", "Asset", 999)));

        await using var verifyCtx = _fixture.CreateContext();
        Assert.Equal(0, await verifyCtx.GlAccounts.CountAsync());
    }

    [Fact]
    public async Task ListGlAccountsAsync_ReturnsEveryRow_OrderedByCode()
    {
        await using var ctx = _fixture.CreateContext();
        var service = new GlAccountService(ctx, TestAudit.For(ctx));
        await service.CreateGlAccountAsync(new CreateGlAccountRequest("4000", "Fee Income", "Income", null));
        await service.CreateGlAccountAsync(new CreateGlAccountRequest("1000", "Cash", "Asset", null));

        var list = await service.ListGlAccountsAsync();

        Assert.Equal(["1000", "4000"], list.Select(g => g.Code));
    }
}
