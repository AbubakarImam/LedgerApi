using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;

namespace LedgerApi.Services;

public class DepositService(ITransferService transferService) : IDepositService
{
    public Task<TransferResponse> DepositAsync(DepositRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
