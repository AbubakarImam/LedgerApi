using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountsController(IAccountService accountService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AccountResponse>> CreateAccount(
        [FromBody] CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await accountService.CreateAccountAsync(request, cancellationToken);
        return Ok(account);
    }

    [HttpGet("{accountNumber}")]
    public async Task<ActionResult<AccountResponse>> GetAccount(
        string accountNumber,
        CancellationToken cancellationToken)
    {
        var account = await accountService.GetAccountAsync(accountNumber, cancellationToken);
        return account is null ? NotFound() : Ok(account);
    }

    [HttpGet("{accountNumber}/balance")]
    public async Task<ActionResult<BalanceResponse>> GetBalance(
        string accountNumber,
        CancellationToken cancellationToken)
    {
        var balance = await accountService.GetBalanceAsync(accountNumber, cancellationToken);
        return balance is null ? NotFound() : Ok(balance);
    }
}
