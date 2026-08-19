using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;

namespace LedgerApi.Services;

public interface IAccountService
{
    Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default);
    Task<AccountResponse?> GetAccountAsync(string accountNumber, CancellationToken cancellationToken = default);
    Task<BalanceResponse?> GetBalanceAsync(string accountNumber, CancellationToken cancellationToken = default);
}
