using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;

namespace LedgerApi.Services;

public class ReversalService(LedgerDbContext dbContext) : IReversalService
{
    public Task<TransactionResponse> ReverseAsync(ReversalRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
