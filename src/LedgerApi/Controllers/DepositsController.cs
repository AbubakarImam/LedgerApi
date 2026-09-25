using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Entities;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DepositsController(
    IDepositService depositService,
    DepositRequestValidator depositRequestValidator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TransferResponse>> Deposit(
        [FromBody] DepositRequest request,
        CancellationToken cancellationToken)
    {
        var errors = depositRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var response = await depositService.DepositAsync(request, cancellationToken);

        // A business-rule failure is committed and returned like a success, but with 422 (decision #29).
        return response.Status == nameof(TransactionStatus.Failed)
            ? UnprocessableEntity(response)
            : Ok(response);
    }
}
