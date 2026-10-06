using LedgerApi.Auditing;
using LedgerApi.Authorization;
using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;
using LedgerApi.Entities;
using LedgerApi.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;


namespace LedgerApi.Services;

public class TransferService(LedgerDbContext dbContext, IAuditLogger auditLogger, ICurrentClient currentClient) : ITransferService
{
    //accept idempotency key from the request(client-supplied)
    // Attempt to insert transaction envelope with the key, the calling client and a hash of the request
    // On unique-constraint violation for (client, key): same request hash -> return the stored result, no reprocessing;
    //   different request hash -> 409, the key was already used for a different request
    //lock the row on account debit
    // Fetch destination account no locking
    // check if source account allows debit
    // check if destination account allows credit
    // Check currency matches on both account
    // check the amount has no more decimal places than the currency allows
    //derived the balance of debit account
    // check balance sufficiency (system accounts can go to overdraft floor value)
    // write debit entry into account A
    // write credit entry into account b
    // mark transaction envelope status = success, set completed_at
    // stage an audit row so it commits with the envelope and entries
    //commit all changes 
    // on business logic failure at any check it thows one of the scaffolded exceptions; mark envelope statuss = failed, with reason. commit only those
    // Map to transferResponse, return

    private async Task<TransferResponse> FailTransferAsync(
    Transaction transaction, TransferRequest request, string reason, IDbContextTransaction dbTransaction, CancellationToken cancellationToken)
    {
        transaction.Status = TransactionStatus.Failed;
        transaction.CompletedAt = DateTimeOffset.UtcNow;
        transaction.FailureReason = reason;
        RecordAudit(transaction, request, currency: null);
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return new TransferResponse(transaction.Reference, transaction.Status.ToString(), transaction.FailureReason);
    }

    // Rollback is cleanup, not work: it must run even if the client has disconnected, so it never takes
    // the request's token. With a cancelled token it would throw before rolling back and hide the
    // original exception (ReversalService does the same).
    private static Task RollbackAsync(IDbContextTransaction dbTransaction) =>
        dbTransaction.RollbackAsync(CancellationToken.None);

    private void RecordAudit(Transaction transaction, TransferRequest request, string? currency) =>
        auditLogger.Record(new AuditLog
        {
            Action = AuditActions.Transfer,
            EntityType = nameof(Transaction),
            EntityId = transaction.Reference,
            DebitAccountNumber = request.DebitAccountNumber,
            CreditAccountNumber = request.CreditAccountNumber,
            Amount = request.Amount,
            Currency = currency,
            Status = transaction.Status.ToString(),
        });

    // Fingerprint of the fields that define a transfer. JSON keeps the fields unambiguous, and the amount is
    // normalised so 100 and 100.00 are the same request.
    private static string HashRequest(TransferRequest request)
    {
        var fields = JsonSerializer.Serialize(new[]
        {
            request.DebitAccountNumber,
            request.CreditAccountNumber,
            request.Amount.ToString("G29", CultureInfo.InvariantCulture),
            request.Narration ?? "",
        });
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(fields)));
    }

    public async Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default)
    {
        var clientId = currentClient.ClientId;
        var requestHash = HashRequest(request);

        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var transaction = new Transaction
        {
            Reference = TransactionReference.Generate(),
            IdempotencyKey = request.IdempotencyKey,
            InitiatedBy = clientId,
            RequestHash = requestHash,
            Status = TransactionStatus.Pending,
            Narration = request.Narration,
            CreatedAt = now
        };
        await dbContext.Transactions.AddAsync(transaction);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pgEx)
        {
            if (pgEx.ConstraintName == "ix_transactions_initiated_by_idempotency_key")
            {
                var dbInsertedTransaction = await dbContext.Transactions.FirstOrDefaultAsync(
                    x => x.InitiatedBy == clientId && x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
                if (dbInsertedTransaction != null)
                {
                    await RollbackAsync(dbTransaction);

                    // Same key, different request: refuse rather than report the old result as this one's (409).
                    // Rows from before request hashes were stored (null) are treated as a match.
                    if (dbInsertedTransaction.RequestHash is not null && dbInsertedTransaction.RequestHash != requestHash)
                        throw new DuplicateIdempotencyKeyException(
                            $"Idempotency key '{request.IdempotencyKey}' was already used for a different request.");

                    return new TransferResponse(
                        dbInsertedTransaction.Reference,
                        dbInsertedTransaction.Status.ToString(),
                        dbInsertedTransaction.FailureReason
                        );


                }
                else
                {
                    await RollbackAsync(dbTransaction);

                    throw new InvalidOperationException("Error retriving the result of the existing indemmpotency value");
                }
            }
            else
            {
                await RollbackAsync(dbTransaction);
                throw new InvalidOperationException("Unexpected constraint violation");
            }
        }
        catch
        {
            await RollbackAsync(dbTransaction);
            throw;
        }

        if (request.Amount <= 0m)
            return await FailTransferAsync(transaction, request, "Amount must be positive", dbTransaction, cancellationToken);

        Account? sourceAccount;
        try
        {
            //Locking Source Account 
            sourceAccount = await dbContext.Accounts.FromSqlInterpolated($"""
                SELECT * FROM "accounts" WHERE "account_number" = {request.DebitAccountNumber}
                FOR UPDATE
                """).SingleOrDefaultAsync(cancellationToken);

        }
        catch
        {
            await RollbackAsync(dbTransaction);
            throw;
        }
        if (sourceAccount is null)
            return await FailTransferAsync(transaction, request, "Source account not found", dbTransaction, cancellationToken);

        Account? destinationAccount;
        try
        {
            //Getting Destination Account
            destinationAccount = await dbContext.Accounts
                .SingleOrDefaultAsync(x => x.AccountNumber == request.CreditAccountNumber, cancellationToken);

        }
        catch
        {
            await RollbackAsync(dbTransaction); throw;
        }
        if (destinationAccount is null)
            return await FailTransferAsync(transaction, request, "Destination account not found", dbTransaction, cancellationToken);

        //Check Source account status
        if (sourceAccount.Status != AccountStatus.Active)
            return await FailTransferAsync(transaction, request, "Source account not active", dbTransaction, cancellationToken);

        //Check Destination account status
        if (destinationAccount.Status == AccountStatus.Blocked)
            return await FailTransferAsync(transaction, request, "Destination account is blocked", dbTransaction, cancellationToken);

        // Check Currency Code
        if (sourceAccount.CurrencyCode != destinationAccount.CurrencyCode)
            return await FailTransferAsync(transaction, request, "Conflicting currency type", dbTransaction, cancellationToken);

        // Check Amount fits the currency's decimal places (e.g. whole numbers only for XOF or JPY).
        // Without this, decimal(18,2) would silently round 10.005 to 10.01 instead of rejecting it.
        if (!SupportedCurrencies.DecimalPlaces.TryGetValue(sourceAccount.CurrencyCode, out var decimalPlaces))
            return await FailTransferAsync(transaction, request, $"Currency {sourceAccount.CurrencyCode} is not supported", dbTransaction, cancellationToken);
        if (decimal.Round(request.Amount, decimalPlaces) != request.Amount)
            return await FailTransferAsync(transaction, request,
                $"Amount has more decimal places than {sourceAccount.CurrencyCode} allows ({decimalPlaces})", dbTransaction, cancellationToken);

        //Derive account balance
        var balance = await dbContext.LedgerEntries.Where(x => x.AccountId == sourceAccount.Id)
           .SumAsync(x => x.EntryType == EntryType.Credit ? x.Amount : -x.Amount, cancellationToken);
       
        //Check Sufficient funds
        var sourceAccountBalance = balance - request.Amount;

        if (sourceAccount.AccountClass == AccountClass.Customer)
        {
            if (sourceAccountBalance < 0m)
                return await FailTransferAsync(transaction, request, "Insufficient Account Balance", dbTransaction, cancellationToken);
        }
        else if (sourceAccount.AccountClass == AccountClass.System)
        {
            if (sourceAccountBalance < -1_000_000_000_000m)
                return await FailTransferAsync(transaction, request, "Insufficient Account Balance", dbTransaction, cancellationToken);
        }
        else
        {
            return await FailTransferAsync(transaction, request, $"Unrecognized account class: {sourceAccount.AccountClass}", dbTransaction, cancellationToken);
        }
        //Append Debit Entry
        var debitEntry = new LedgerEntry
        {
            TransactionId = transaction.Id,
            AccountId = sourceAccount.Id,
            EntryType = EntryType.Debit,
            Amount = request.Amount,
            Currency = sourceAccount.CurrencyCode,
            CreatedAt = now,
            Transaction = transaction,
            Account = sourceAccount
        };
        await dbContext.LedgerEntries.AddAsync(debitEntry);


        //Append Credit Entry
        var creditEntry = new LedgerEntry
        {
            TransactionId = transaction.Id,
            AccountId = destinationAccount.Id,
            EntryType = EntryType.Credit,
            Amount = request.Amount,
            Currency = destinationAccount.CurrencyCode,
            CreatedAt = now,
            Transaction = transaction,
            Account = destinationAccount
        };
        await dbContext.LedgerEntries.AddAsync(creditEntry);

        transaction.Status = TransactionStatus.Success;
        transaction.CompletedAt = DateTimeOffset.UtcNow;

        //Record Audit, committed with the entries
        RecordAudit(transaction, request, sourceAccount.CurrencyCode);

        try
        {
            
            await dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
        } catch
        {
            await RollbackAsync(dbTransaction); throw;
        }

        return new TransferResponse(transaction.Reference, transaction.Status.ToString());
    }
}
