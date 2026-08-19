using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;

namespace LedgerApi.Services;

public interface IDepositService
{
    Task<TransferResponse> DepositAsync(DepositRequest request, CancellationToken cancellationToken = default);
}
