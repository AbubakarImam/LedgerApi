using LedgerApi.Authorization;
using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Entities;
using LedgerApi.Services;
using LedgerApi.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LedgerApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChainsController(
    IChainService chainService,
    ChainRequestValidator chainRequestValidator) : ControllerBase
{
    [Authorize(Policy = LedgerScopes.Transfer)]
    [HttpPost]
    public async Task<ActionResult<ChainResponse>> Post(
        [FromBody] ChainRequest request,
        CancellationToken cancellationToken)
    {
        var errors = chainRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var response = await chainService.PostAsync(request, cancellationToken);

        // A rejected chain is stored and returned like a success, but with 422 (decisions #29 and #45).
        return response.Status == nameof(TransactionStatus.Failed)
            ? UnprocessableEntity(response)
            : Ok(response);
    }

    [Authorize(Policy = LedgerScopes.Read)]
    [HttpGet("{reference}")]
    public async Task<ActionResult<ChainResponse>> Get(
        string reference,
        CancellationToken cancellationToken)
    {
        var chain = await chainService.GetAsync(reference, cancellationToken);
        return chain is null ? NotFound() : Ok(chain);
    }
}
