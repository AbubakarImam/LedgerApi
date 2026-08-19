using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers.Admin;

[ApiController]
[Route("api/admin/system-accounts")]
public class SystemAccountsController(IAdminAccountService adminAccountService) : ControllerBase
{
    // TODO: authorization requirement not yet implemented (docs/DESIGN.md decision #20).
    [HttpPost]
    public async Task<ActionResult<AccountResponse>> CreateSystemAccount(
        [FromBody] CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await adminAccountService.CreateSystemAccountAsync(request, cancellationToken);
        return Ok(account);
    }
}
