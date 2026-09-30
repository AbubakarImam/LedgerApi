using LedgerApi.Authorization;
using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers.Admin;

[ApiController]
[Route("api/admin/system-accounts")]
public class SystemAccountsController(
    IAdminAccountService adminAccountService,
    CreateAccountRequestValidator createAccountRequestValidator) : ControllerBase
{
    [Authorize(Policy = LedgerScopes.Admin)]
    [HttpPost]
    public async Task<ActionResult<AccountResponse>> CreateSystemAccount(
        [FromBody] CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var errors = createAccountRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var account = await adminAccountService.CreateSystemAccountAsync(request, cancellationToken);
        return Ok(account);
    }
}
