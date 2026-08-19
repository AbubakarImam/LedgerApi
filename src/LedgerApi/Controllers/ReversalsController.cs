using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReversalsController(IReversalService reversalService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TransactionResponse>> Reverse(
        [FromBody] ReversalRequest request,
        CancellationToken cancellationToken)
    {
        var response = await reversalService.ReverseAsync(request, cancellationToken);
        return Ok(response);
    }
}
