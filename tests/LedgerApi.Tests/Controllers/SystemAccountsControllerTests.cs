using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Controllers.Admin;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Tests.Controllers;

public class SystemAccountsControllerTests
{
    // Returns a fixed account and records the request it received, so the controller can be
    // tested without a database.
    private sealed class StubAdminAccountService(AccountResponse response) : IAdminAccountService
    {
        public CreateAccountRequest? ReceivedRequest { get; private set; }
        public int Calls { get; private set; }

        public Task<AccountResponse> CreateSystemAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            ReceivedRequest = request;
            return Task.FromResult(response);
        }
    }

    private static readonly AccountResponse Account = new(
        "NGN702022272", "NGN Fees", "Wallet", "System", "NGN", "Active", DateTimeOffset.UtcNow);

    private static SystemAccountsController CreateController(StubAdminAccountService service) =>
        new(service, new CreateAccountRequestValidator());

    [Fact]
    public async Task CreateSystemAccount_Returns200_WithCreatedAccount()
    {
        var service = new StubAdminAccountService(Account);
        var controller = CreateController(service);
        var request = new CreateAccountRequest("NGN Fees", "Wallet", "NGN");

        var result = await controller.CreateSystemAccount(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(Account, ok.Value);
        Assert.Equal(request, service.ReceivedRequest);
    }

    [Fact]
    public async Task CreateSystemAccount_Returns400_AndDoesNotCallService_WhenAccountTypeIsUnknown()
    {
        var service = new StubAdminAccountService(Account);
        var controller = CreateController(service);

        var result = await controller.CreateSystemAccount(
            new CreateAccountRequest("NGN Fees", "Crypto", "NGN"), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var errors = Assert.IsAssignableFrom<IReadOnlyList<string>>(badRequest.Value);
        Assert.Contains(errors, e => e.Contains("Crypto"));
        Assert.Equal(0, service.Calls);
    }
}
