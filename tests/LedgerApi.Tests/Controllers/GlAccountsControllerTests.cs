using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Controllers.Admin;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Tests.Controllers;

public class GlAccountsControllerTests
{
    // Returns fixed results and records what it received, so the controller can be tested without a
    // database - same shape as SystemAccountsControllerTests' StubAdminAccountService.
    private sealed class StubGlAccountService(GlAccountResponse response, IReadOnlyList<GlAccountResponse>? listResponse = null) : IGlAccountService
    {
        public CreateGlAccountRequest? ReceivedRequest { get; private set; }
        public int CreateCalls { get; private set; }

        public Task<GlAccountResponse> CreateGlAccountAsync(CreateGlAccountRequest request, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            ReceivedRequest = request;
            return Task.FromResult(response);
        }

        public Task<IReadOnlyList<GlAccountResponse>> ListGlAccountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(listResponse ?? [response]);
    }

    private static readonly GlAccountResponse GlAccount = new(1, "1000", "Cash and Cash Equivalents", "Asset", null, DateTimeOffset.UtcNow);

    private static GlAccountsController CreateController(StubGlAccountService service) =>
        new(service, new CreateGlAccountRequestValidator());

    [Fact]
    public async Task CreateGlAccount_Returns200_WithCreatedGlAccount()
    {
        var service = new StubGlAccountService(GlAccount);
        var controller = CreateController(service);
        var request = new CreateGlAccountRequest("1000", "Cash and Cash Equivalents", "Asset", null);

        var result = await controller.CreateGlAccount(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(GlAccount, ok.Value);
        Assert.Equal(request, service.ReceivedRequest);
    }

    [Fact]
    public async Task CreateGlAccount_Returns400_AndDoesNotCallService_WhenAccountTypeIsUnknown()
    {
        var service = new StubGlAccountService(GlAccount);
        var controller = CreateController(service);

        var result = await controller.CreateGlAccount(
            new CreateGlAccountRequest("1000", "Cash", "Crypto", null), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var errors = Assert.IsAssignableFrom<IReadOnlyList<string>>(badRequest.Value);
        Assert.Contains(errors, e => e.Contains("Crypto"));
        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task ListGlAccounts_Returns200_WithEveryGlAccount()
    {
        var accounts = new List<GlAccountResponse> { GlAccount, GlAccount with { Id = 2, Code = "2000" } };
        var service = new StubGlAccountService(GlAccount, accounts);
        var controller = CreateController(service);

        var result = await controller.ListGlAccounts(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(accounts, ok.Value);
    }
}
