namespace LedgerApi.Authorization;

public interface IMandateService
{
    Task<bool> HasMandateAsync(string actorId, string accountNumber, CancellationToken cancellationToken = default);
}
