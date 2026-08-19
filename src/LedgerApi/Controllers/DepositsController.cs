using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DepositsController(IDepositService depositService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TransferResponse>> Deposit(
        [FromBody] DepositRequest request,
        CancellationToken cancellationToken)
    {
        var response = await depositService.DepositAsync(request, cancellationToken);
        return Ok(response);
    }
}
