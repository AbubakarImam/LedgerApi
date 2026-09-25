using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Controllers;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Tests.Controllers;

public class AccountsControllerTests
{
    private static readonly AccountResponse Account = new(
        "1000000001", "Ada Lovelace", "Savings", "Customer", "NGN", "Active", DateTimeOffset.UtcNow);

    private static readonly BalanceResponse Balance = new("1000000001", 2500m, "NGN", DateTimeOffset.UtcNow);

    // Returns fixed results and counts create calls, so the controller's validation and
    // not-found mapping can be tested without a database. A null account or balance means "not found".
    private sealed class StubAccountService(AccountResponse? account, BalanceResponse? balance) : IAccountService
    {
        public int CreateCalls { get; private set; }

        public Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(account!);
        }

        public Task<AccountResponse?> GetAccountAsync(string accountNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(account);

        public Task<BalanceResponse?> GetBalanceAsync(string accountNumber, CancellationToken cancellationToken = default)
            => Task.FromResult(balance);
    }

    private static AccountsController CreateController(StubAccountService service) =>
        new(service, new CreateAccountRequestValidator());

    [Fact]
    public async Task CreateAccount_Returns200_WithCreatedAccount()
    {
        var controller = CreateController(new StubAccountService(Account, null));

        var result = await controller.CreateAccount(
            new CreateAccountRequest("Ada Lovelace", "Savings", "NGN"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(Account, ok.Value);
    }

    [Fact]
    public async Task CreateAccount_Returns400_AndDoesNotCallService_WhenAccountTypeIsUnknown()
    {
        var service = new StubAccountService(Account, null);
        var controller = CreateController(service);

        var result = await controller.CreateAccount(
            new CreateAccountRequest("Ada Lovelace", "Crypto", "NGN"), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var errors = Assert.IsAssignableFrom<IReadOnlyList<string>>(badRequest.Value);
        Assert.Contains(errors, e => e.Contains("Crypto"));
        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task GetAccount_Returns200_WhenAccountExists()
    {
        var controller = CreateController(new StubAccountService(Account, null));

        var result = await controller.GetAccount("1000000001", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(Account, ok.Value);
    }

    [Fact]
    public async Task GetAccount_Returns404_WhenAccountDoesNotExist()
    {
        var controller = CreateController(new StubAccountService(null, null));

        var result = await controller.GetAccount("0000000000", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetBalance_Returns200_WhenAccountExists()
    {
        var controller = CreateController(new StubAccountService(Account, Balance));

        var result = await controller.GetBalance("1000000001", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(Balance, ok.Value);
    }

    [Fact]
    public async Task GetBalance_Returns404_WhenAccountDoesNotExist()
    {
        var controller = CreateController(new StubAccountService(null, null));

        var result = await controller.GetBalance("0000000000", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
