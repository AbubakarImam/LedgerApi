using LedgerApi.Contracts.Responses;

namespace LedgerApi.Services;

public interface IReportService
{
    Task<TrialBalanceResponse> GetTrialBalanceAsync(CancellationToken cancellationToken = default);
}
