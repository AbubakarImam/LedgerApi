using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;

namespace LedgerApi.Services;

public interface ITransferService
{
    Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default);
}
