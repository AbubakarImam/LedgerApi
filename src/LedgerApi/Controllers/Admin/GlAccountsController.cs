using LedgerApi.Authorization;
using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers.Admin;

[ApiController]
[Route("api/admin/gl-accounts")]
public class GlAccountsController(
    IGlAccountService glAccountService,
    CreateGlAccountRequestValidator createGlAccountRequestValidator) : ControllerBase
{
    [Authorize(Policy = LedgerScopes.Admin)]
    [HttpPost]
    public async Task<ActionResult<GlAccountResponse>> CreateGlAccount(
        [FromBody] CreateGlAccountRequest request,
        CancellationToken cancellationToken)
    {
        var errors = createGlAccountRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var glAccount = await glAccountService.CreateGlAccountAsync(request, cancellationToken);
        return Ok(glAccount);
    }

    [Authorize(Policy = LedgerScopes.Admin)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GlAccountResponse>>> ListGlAccounts(CancellationToken cancellationToken)
    {
        var glAccounts = await glAccountService.ListGlAccountsAsync(cancellationToken);
        return Ok(glAccounts);
    }
}
