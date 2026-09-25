using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;

namespace LedgerApi.Services;

public interface IWithdrawalService
{
    Task<TransferResponse> WithdrawAsync(WithdrawalRequest request, CancellationToken cancellationToken = default);
}
