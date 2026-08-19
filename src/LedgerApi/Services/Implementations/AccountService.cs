using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;

namespace LedgerApi.Services;

public class AccountService(LedgerDbContext dbContext) : IAccountService
{
    public Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<AccountResponse?> GetAccountAsync(string accountNumber, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<BalanceResponse?> GetBalanceAsync(string accountNumber, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
