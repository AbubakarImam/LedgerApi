using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Entities;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WithdrawalsController(
    IWithdrawalService withdrawalService,
    WithdrawalRequestValidator withdrawalRequestValidator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TransferResponse>> Withdraw(
        [FromBody] WithdrawalRequest request,
        CancellationToken cancellationToken)
    {
        var errors = withdrawalRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var response = await withdrawalService.WithdrawAsync(request, cancellationToken);

        // A business-rule failure is committed and returned like a success, but with 422 (decision #29).
        return response.Status == nameof(TransactionStatus.Failed)
            ? UnprocessableEntity(response)
            : Ok(response);
    }
}
