using LedgerApi.Authorization;
using LedgerApi.Contracts.Responses;
using LedgerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController(IReportService reportService) : ControllerBase
{
    [Authorize(Policy = LedgerScopes.Read)]
    [HttpGet("trial-balance")]
    public async Task<ActionResult<TrialBalanceResponse>> GetTrialBalance(CancellationToken cancellationToken)
    {
        var report = await reportService.GetTrialBalanceAsync(cancellationToken);
        return Ok(report);
    }
}
