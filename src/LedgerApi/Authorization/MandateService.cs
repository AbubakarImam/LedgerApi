namespace LedgerApi.Authorization;

public class MandateService : IMandateService
{
    public Task<bool> HasMandateAsync(string actorId, string accountNumber, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
