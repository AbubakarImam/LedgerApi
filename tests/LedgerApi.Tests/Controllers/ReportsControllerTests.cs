using LedgerApi.Contracts.Responses;
using LedgerApi.Controllers;
using LedgerApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Tests.Controllers;

public class ReportsControllerTests
{
    private sealed class StubReportService(TrialBalanceResponse response) : IReportService
    {
        public Task<TrialBalanceResponse> GetTrialBalanceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(response);
    }

    [Fact]
    public async Task GetTrialBalance_Returns200_WithTheReport()
    {
        var report = new TrialBalanceResponse(
            [new TrialBalanceEntry(1, "1000", "Cash and Cash Equivalents", "Asset", null, false, [], [])],
            DateTimeOffset.UtcNow);
        var controller = new ReportsController(new StubReportService(report));

        var result = await controller.GetTrialBalance(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(report, ok.Value);
    }
}
