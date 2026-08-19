using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;

namespace LedgerApi.Services;

public class AdminAccountService(LedgerDbContext dbContext) : IAdminAccountService
{
    public Task<AccountResponse> CreateSystemAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
