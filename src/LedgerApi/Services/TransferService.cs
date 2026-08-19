using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;

namespace LedgerApi.Services;

public class TransferService(LedgerDbContext dbContext) : ITransferService
{
    public Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
