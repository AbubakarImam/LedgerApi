using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransfersController(ITransferService transferService,
    TransferRequestValidator transferRequestValidator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TransferResponse>> Transfer(
        [FromBody] TransferRequest request,
        CancellationToken cancellationToken)
    {
        var errors = transferRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var response = await transferService.TransferAsync(request, cancellationToken);
        return Ok(response);
    }
}
