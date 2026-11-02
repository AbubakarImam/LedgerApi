using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;

namespace LedgerApi.Services;

public interface IGlAccountService
{
    Task<GlAccountResponse> CreateGlAccountAsync(CreateGlAccountRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GlAccountResponse>> ListGlAccountsAsync(CancellationToken cancellationToken = default);
}
