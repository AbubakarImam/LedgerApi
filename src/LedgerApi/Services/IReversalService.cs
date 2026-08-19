using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;

namespace LedgerApi.Services;

public interface IReversalService
{
    Task<TransactionResponse> ReverseAsync(ReversalRequest request, CancellationToken cancellationToken = default);
}
