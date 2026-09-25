using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Controllers;
using LedgerApi.Exceptions;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Tests.Controllers;

public class ReversalsControllerTests
{
    // Returns a fixed response (or throws a fixed exception) and counts calls, so the controller's
    // validation can be tested without a database.
    private sealed class StubReversalService(TransactionResponse? response, Exception? toThrow = null) : IReversalService
    {
        public int Calls { get; private set; }

        public Task<TransactionResponse> ReverseAsync(ReversalRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return toThrow is null ? Task.FromResult(response!) : Task.FromException<TransactionResponse>(toThrow);
        }
    }

    private static ReversalsController CreateController(StubReversalService service) =>
        new(service, new ReversalRequestValidator());

    [Fact]
    public async Task Reverse_Returns200_WithReversalTransaction()
    {
        var response = new TransactionResponse(
            "TXN-REVERSAL", "Success", "Customer dispute", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var controller = CreateController(new StubReversalService(response));

        var result = await controller.Reverse(new ReversalRequest("TXN-ORIGINAL", "Customer dispute"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(response, ok.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reverse_Returns400_AndDoesNotCallService_WhenReferenceIsMissing(string reference)
    {
        var service = new StubReversalService(null);
        var controller = CreateController(service);

        var result = await controller.Reverse(new ReversalRequest(reference, null), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var errors = Assert.IsAssignableFrom<IReadOnlyList<string>>(badRequest.Value);
        Assert.Contains(errors, e => e.Contains("reference", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task Reverse_LetsServiceExceptionsReachTheMiddleware()
    {
        // Reversal failures are exceptions, mapped to 404/409/422 by ExceptionHandlingMiddleware;
        // the controller must not swallow them.
        var controller = CreateController(new StubReversalService(null, new AlreadyReversedException("already reversed")));

        await Assert.ThrowsAsync<AlreadyReversedException>(() =>
            controller.Reverse(new ReversalRequest("TXN-ORIGINAL", null), CancellationToken.None));
    }
}
