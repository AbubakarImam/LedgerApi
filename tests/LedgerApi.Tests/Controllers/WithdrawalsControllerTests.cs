using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Controllers;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Tests.Controllers;

public class WithdrawalsControllerTests
{
    // Returns a fixed response and counts calls, so the controller's validation and
    // status-code mapping can be tested without a database.
    private sealed class StubWithdrawalService(TransferResponse response) : IWithdrawalService
    {
        public int Calls { get; private set; }

        public Task<TransferResponse> WithdrawAsync(WithdrawalRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(response);
        }
    }

    private static WithdrawalRequest ValidRequest() =>
        new("1000000001", 100m, "Controller test", Guid.NewGuid().ToString());

    [Fact]
    public async Task Withdraw_Returns200_WhenWithdrawalSucceeds()
    {
        var response = new TransferResponse("TXN-SUCCESS", "Success");
        var controller = new WithdrawalsController(new StubWithdrawalService(response), new WithdrawalRequestValidator());

        var result = await controller.Withdraw(ValidRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(response, ok.Value);
    }

    [Fact]
    public async Task Withdraw_Returns422_WithStoredResponse_WhenWithdrawalFailsABusinessRule()
    {
        var response = new TransferResponse("TXN-FAILED", "Failed", "Insufficient Account Balance");
        var controller = new WithdrawalsController(new StubWithdrawalService(response), new WithdrawalRequestValidator());

        var result = await controller.Withdraw(ValidRequest(), CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.Equal(response, unprocessable.Value);
    }

    [Fact]
    public async Task Withdraw_Returns400_AndDoesNotCallService_WhenRequestIsInvalid()
    {
        var service = new StubWithdrawalService(new TransferResponse("TXN-UNUSED", "Success"));
        var controller = new WithdrawalsController(service, new WithdrawalRequestValidator());
        var invalid = new WithdrawalRequest("1000000001", -5m, null, Guid.NewGuid().ToString());

        var result = await controller.Withdraw(invalid, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var errors = Assert.IsAssignableFrom<IReadOnlyList<string>>(badRequest.Value);
        Assert.Contains(errors, e => e.Contains("Amount", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, service.Calls);
    }
}
