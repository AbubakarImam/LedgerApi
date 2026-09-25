using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Controllers;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Tests.Controllers;

public class DepositsControllerTests
{
    // Returns a fixed response so the controller's status-code mapping can be tested without a database.
    private sealed class StubDepositService(TransferResponse response) : IDepositService
    {
        public Task<TransferResponse> DepositAsync(DepositRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(response);
    }

    private static DepositRequest ValidRequest() =>
        new("1000000001", 100m, "Controller test", Guid.NewGuid().ToString());

    [Fact]
    public async Task Deposit_Returns200_WhenDepositSucceeds()
    {
        var response = new TransferResponse("TXN-SUCCESS", "Success");
        var controller = new DepositsController(new StubDepositService(response), new DepositRequestValidator());

        var result = await controller.Deposit(ValidRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(response, ok.Value);
    }

    [Fact]
    public async Task Deposit_Returns422_WithStoredResponse_WhenDepositFailsABusinessRule()
    {
        var response = new TransferResponse("TXN-FAILED", "Failed", "Destination account is blocked");
        var controller = new DepositsController(new StubDepositService(response), new DepositRequestValidator());

        var result = await controller.Deposit(ValidRequest(), CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.Equal(response, unprocessable.Value);
    }
}
