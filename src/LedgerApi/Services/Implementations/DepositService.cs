using LedgerApi.Configuration;
using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;
using LedgerApi.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LedgerApi.Services;

public class DepositService(
    ITransferService transferService,
    LedgerDbContext dbContext,
    IOptions<FundingOptions> options) : IDepositService
{
    public async Task<TransferResponse> DepositAsync(DepositRequest request, CancellationToken cancellationToken = default)
    {
        var customerAccount = await dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.AccountNumber == request.CustomerAccountNumber, cancellationToken);

        if (customerAccount is null)
            throw new AccountNotFoundException($"Customer account '{request.CustomerAccountNumber}' was not found.");

        if (!options.Value.Accounts.TryGetValue(customerAccount.CurrencyCode, out var fundingAccountNumber))
            throw new InvalidOperationException($"No funding account is configured for currency '{customerAccount.CurrencyCode}'.");

        var transferRequest = new TransferRequest(
            DebitAccountNumber: fundingAccountNumber,
            CreditAccountNumber: customerAccount.AccountNumber,
            Amount: request.Amount,
            Narration: request.Narration,
            IdempotencyKey: request.IdempotencyKey);

        return await transferService.TransferAsync(transferRequest, cancellationToken);
    }
}
