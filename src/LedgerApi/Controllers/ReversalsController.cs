using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReversalsController(
    IReversalService reversalService,
    ReversalRequestValidator reversalRequestValidator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TransactionResponse>> Reverse(
        [FromBody] ReversalRequest request,
        CancellationToken cancellationToken)
    {
        var errors = reversalRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var response = await reversalService.ReverseAsync(request, cancellationToken);
        return Ok(response);
    }
}
