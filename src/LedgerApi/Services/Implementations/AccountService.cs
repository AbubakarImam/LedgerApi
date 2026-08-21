using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;
using LedgerApi.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LedgerApi.Services;

public class AccountService(LedgerDbContext dbContext) : IAccountService
{

    //
    // Generate the account number which are 10 digits in number and generated at random.
    // we take the request payload
    //we check for unique constariant so that we dont have duplicate account number
    // We create a row on how account table with the information all correctly mapped
    // We now send a response with our AccountResponse contract value
    public async Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 5;

        for(int attempt =1;  attempt <= maxAttempts; attempt++)
        {
            var random = Random.Shared.NextInt64(1_000_000_000, 10_000_000_000).ToString();
            var now = DateTimeOffset.UtcNow;

            var account = new Account
            {
                AccountNumber = random,
                AccountName = request.AccountName,
                AccountType = Enum.Parse<AccountType>(request.AccountType, ignoreCase: true),
                AccountClass = AccountClass.Customer,
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
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } )
            {
                dbContext.Entry(account).State = EntityState.Detached;
            }

        }
        throw new InvalidOperationException("Could not allocate a unique account number after 5 attempts");

    }
    // We check the db with the account number we got from the url
    // If it exist we return the account
    // When account does not exist return null
    public async Task<AccountResponse?> GetAccountAsync(string accountNumber, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.Accounts.FirstOrDefaultAsync(x => x.AccountNumber == accountNumber, cancellationToken);
        if (account is null) return null;
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
    // We collate all the entries on the account to derive it balance
    // We return the derived balance
    //
    public async Task<BalanceResponse?> GetBalanceAsync(string accountNumber, CancellationToken cancellationToken = default)
    {
        var account = await dbContext.Accounts.FirstOrDefaultAsync(x => x.AccountNumber == accountNumber, cancellationToken);
        if (account is null) return null;
        var balance = await dbContext.LedgerEntries.Where(x => x.AccountId == account.Id)
            .SumAsync(x => x.EntryType == EntryType.Credit ? x.Amount : -x.Amount, cancellationToken);
        return new BalanceResponse(
            account.AccountNumber,
            balance,
            account.CurrencyCode,
            DateTime.UtcNow);
    }
}
