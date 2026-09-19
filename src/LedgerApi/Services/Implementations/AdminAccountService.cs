using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;
using LedgerApi.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LedgerApi.Services;

public class AdminAccountService(LedgerDbContext dbContext) : IAdminAccountService
{
    public async Task<AccountResponse> CreateSystemAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 5;

        for(int a = 1;  a <= maxAttempts; a++)
        {
            var random = Random.Shared.NextInt64(100_000_000, 1_000_000_000).ToString();
            var GeneratedAccountNumber = request.CurrencyCode.ToUpperInvariant() + random;
            var now = DateTimeOffset.UtcNow;

            var account = new Account
            {
                AccountNumber = GeneratedAccountNumber,
                AccountName = request.AccountName,
                AccountType = Enum.Parse<AccountType>(request.AccountType, ignoreCase: true),
                AccountClass = AccountClass.System,
                CurrencyCode = request.CurrencyCode,
                Status = AccountStatus.Active,
                CreatedAt = now,
            };

            await dbContext.Accounts.AddAsync(account);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                return new AccountResponse(
                     account.AccountNumber,
                     account.AccountName,
                     account.AccountType.ToString(),
                     account.AccountClass.ToString(),
                     account.CurrencyCode,
                     account.Status.ToString(),
                     account.CreatedAt

                 );
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
            {
                dbContext.Entry(account).State = EntityState.Detached;
            }

        }
        throw new InvalidOperationException("Could not allocate a unique account number after 5 attempts");

    }
}
