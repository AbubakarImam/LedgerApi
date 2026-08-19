using LedgerApi.Contracts.Responses;

namespace LedgerApi.Services;

public interface IAccountService
{
    Task<AccountResponse?> GetAccountAsync(string accountNumber, CancellationToken cancellationToken = default);
    Task<BalanceResponse?> GetBalanceAsync(string accountNumber, CancellationToken cancellationToken = default);
}
