using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransfersController(ITransferService transferService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TransferResponse>> Transfer(
        [FromBody] TransferRequest request,
        CancellationToken cancellationToken)
    {
        var response = await transferService.TransferAsync(request, cancellationToken);
        return Ok(response);
    }
}
