using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;

namespace LedgerApi.Services;

public interface IChainService
{
    Task<ChainResponse> PostAsync(ChainRequest request, CancellationToken cancellationToken = default);
    Task<ChainResponse?> GetAsync(string reference, CancellationToken cancellationToken = default);
    Task<ChainResponse> ReverseAsync(string reference, string? reason, CancellationToken cancellationToken = default);
}
