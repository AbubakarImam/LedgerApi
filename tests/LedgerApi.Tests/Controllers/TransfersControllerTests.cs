using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Controllers;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Tests.Controllers;

public class TransfersControllerTests
{
    // Returns a fixed response so the controller's status-code mapping can be tested without a database.
    private sealed class StubTransferService(TransferResponse response) : ITransferService
    {
        public int Calls { get; private set; }

        public Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(response);
        }
    }

    private static TransferRequest ValidRequest() =>
        new("1000000001", "1000000002", 100m, "Controller test", Guid.NewGuid().ToString());

    [Fact]
    public async Task Transfer_Returns200_WhenTransferSucceeds()
    {
        var response = new TransferResponse("TXN-SUCCESS", "Success");
        var controller = new TransfersController(new StubTransferService(response), new TransferRequestValidator());

        var result = await controller.Transfer(ValidRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(response, ok.Value);
    }

    [Fact]
    public async Task Transfer_Returns422_WithStoredResponse_WhenTransferFailsABusinessRule()
    {
        var response = new TransferResponse("TXN-FAILED", "Failed", "Insufficient Account Balance");
        var controller = new TransfersController(new StubTransferService(response), new TransferRequestValidator());

        var result = await controller.Transfer(ValidRequest(), CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, unprocessable.StatusCode);
        Assert.Equal(response, unprocessable.Value);
    }

    [Fact]
    public async Task Transfer_Returns400_AndSkipsService_WhenRequestIsInvalid()
    {
        var service = new StubTransferService(new TransferResponse("TXN-UNUSED", "Success"));
        var controller = new TransfersController(service, new TransferRequestValidator());
        var request = ValidRequest() with { Amount = 0m };

        var result = await controller.Transfer(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(0, service.Calls);
    }
}
