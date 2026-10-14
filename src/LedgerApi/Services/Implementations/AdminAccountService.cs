using LedgerApi.Auditing;
using LedgerApi.Authorization;
using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;
using LedgerApi.Entities;
using LedgerApi.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LedgerApi.Services;

public class AdminAccountService(LedgerDbContext dbContext, IAuditLogger auditLogger, ICurrentClient currentClient) : IAdminAccountService
{
    // Mirrors AccountService.HashRequest: fingerprints the fields that define the account being created.
    private static string HashRequest(CreateAccountRequest request)
    {
        var fields = JsonSerializer.Serialize(new[]
        {
            request.AccountName,
            // The parsed name, not the raw text: the type is accepted case-insensitively, so "savings" and
            // "Savings" describe the same account and must hash the same.
            Enum.Parse<AccountType>(request.AccountType, ignoreCase: true).ToString(),
            request.CurrencyCode,
        });
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(fields)));
    }

    private static AccountResponse ToResponse(Account account) => new(
        account.AccountNumber,
        account.AccountName,
        account.AccountType.ToString(),
        account.AccountClass.ToString(),
        account.CurrencyCode,
        account.Status.ToString(),
        account.CreatedAt);

    public async Task<AccountResponse> CreateSystemAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 5;

        var clientId = currentClient.ClientId;
        var requestHash = HashRequest(request);

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
                IdempotencyKey = request.IdempotencyKey,
                InitiatedBy = clientId,
                RequestHash = requestHash,
            };

            await dbContext.Accounts.AddAsync(account);

            var audit = auditLogger.Record(new AuditLog
            {
                Action = AuditActions.SystemAccountCreated,
                EntityType = nameof(Account),
                EntityId = account.AccountNumber,
                Currency = account.CurrencyCode,
                Status = account.Status.ToString(),
            });

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                return ToResponse(account);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pgEx)
            {
                // Detach the audit row too, or the retry would save it alongside the next attempt's row.
                dbContext.Entry(account).State = EntityState.Detached;
                dbContext.Entry(audit).State = EntityState.Detached;

                if (pgEx.ConstraintName == "ix_accounts_initiated_by_idempotency_key")
                {
                    var existingAccount = await dbContext.Accounts.AsNoTracking().FirstOrDefaultAsync(
                        x => x.InitiatedBy == clientId && x.IdempotencyKey == request.IdempotencyKey, cancellationToken);

                    if (existingAccount is null)
                        throw new InvalidOperationException("Error retrieving the result of the existing idempotency key.");

                    if (existingAccount.RequestHash is not null && existingAccount.RequestHash != requestHash)
                        throw new DuplicateIdempotencyKeyException(
                            $"Idempotency key '{request.IdempotencyKey}' was already used for a different request.");

                    return ToResponse(existingAccount);
                }
                // Otherwise the collision was on the account number: loop around and try another one.
            }

        }
        throw new InvalidOperationException("Could not allocate a unique account number after 5 attempts");

    }
}
