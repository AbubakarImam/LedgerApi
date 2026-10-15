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
    private readonly LegPoster legPoster = new(dbContext);

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

    // Fingerprint of the fields that decide how money moves: debit account, credit account and amount
    // (normalised so 100 = 100.00). JSON keeps the fields unambiguous. Narration is only a description: a retry
    // with a different narration is the same transfer and gets the original back, original narration
    // included (decision #44).
    private static string HashRequest(TransferRequest request) => Sha256Base64(JsonSerializer.Serialize(new[]
    {
        request.DebitAccountNumber,
        request.CreditAccountNumber,
        request.Amount.ToString("G29", CultureInfo.InvariantCulture),
    }));

    // The formula used from 6 to 15 October 2026, which included the narration. Kept only so an exact retry of
    // a transaction from that window still matches; remove once those keys are past any realistic retry.
    private static string LegacyHashRequest(TransferRequest request) => Sha256Base64(JsonSerializer.Serialize(new[]
    {
        request.DebitAccountNumber,
        request.CreditAccountNumber,
        request.Amount.ToString("G29", CultureInfo.InvariantCulture),
        request.Narration ?? "",
    }));

    private static string Sha256Base64(string text) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

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
                    // The stored hash may come from either formula; null (rows from before hashes) counts as a match.
                    var storedHash = dbInsertedTransaction.RequestHash;
                    if (storedHash is not null && storedHash != requestHash && storedHash != LegacyHashRequest(request))
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

        try
        {
            // The posting rules live in LegPoster, shared with chains (decision #45). A rejection stages nothing,
            // so the envelope is committed as Failed with the reason, as before (decision #6).
            var sourceAccount = await legPoster.PostLegAsync(
                transaction,
                new LegSpec(request.DebitAccountNumber, request.CreditAccountNumber, request.Amount),
                checkBalance: true,
                now,
                cancellationToken);

            //Record Audit, committed with the entries
            RecordAudit(transaction, request, sourceAccount.CurrencyCode);

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
        }
        catch (LegRejectedException rejected)
        {
            return await FailTransferAsync(transaction, request, rejected.Message, dbTransaction, cancellationToken);
        }
        catch
        {
            await RollbackAsync(dbTransaction); throw;
        }

        return new TransferResponse(transaction.Reference, transaction.Status.ToString());
    }
}
