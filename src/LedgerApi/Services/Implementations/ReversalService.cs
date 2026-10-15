using LedgerApi.Auditing;
using LedgerApi.Authorization;
using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;
using LedgerApi.Entities;
using LedgerApi.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LedgerApi.Services;

public class ReversalService(LedgerDbContext dbContext, IAuditLogger auditLogger, ICurrentClient currentClient) : IReversalService
{
    private readonly LegReverser legReverser = new(dbContext, auditLogger);

    // accept the original transaction reference from the request
    // open one db transaction so everything commits together or not at all
    // find the original transaction by reference, with its entries (not found -> 404)
    // check the original transaction was successful (failed or pending moved no money -> 422)
    // check the original is not itself a reversal (that would redo the original transfer -> 422)
    // insert the reversal envelope and the reversals row in one save
    // on unique-constraint violation on original_transaction_id: already reversed -> 409, no locking or balance work done
    // lock the account being debited now (original credit side)
    // fetch the account being credited now (original debit side), no locking
    // check the account being debited now (original credit side) is not blocked; frozen is allowed so money can be clawed back
    // check the account being credited now (original debit side) is not blocked
    // derive the balance of the account being debited
    // check balance sufficiency (system accounts can go to overdraft floor value)
    // write debit entry into the account the original credited
    // write credit entry into the account the original debited
    // mark reversal envelope status = success, set completed_at
    // stage an audit row so it commits with the entries and the reversals row
    // commit all changes
    // on any failure roll back everything, no failed row is kept (decision #27); middleware maps the exception to a status code
    // Map to TransactionResponse, return

    public async Task<TransactionResponse> ReverseAsync(ReversalRequest request, CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var response = await ReverseInsideTransactionAsync(request, cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            //Roll back everything; CancellationToken.None so the rollback still runs if the client disconnected
            await dbTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<TransactionResponse> ReverseInsideTransactionAsync(ReversalRequest request, CancellationToken cancellationToken)
    {
        //Getting Original Transaction with its entries
        var original = await dbContext.Transactions
            .Include(t => t.Entries)
            .SingleOrDefaultAsync(t => t.Reference == request.OriginalTransactionReference, cancellationToken)
            ?? throw new TransactionNotFoundException($"Transaction '{request.OriginalTransactionReference}' was not found.");

        //Check Original transaction status
        if (original.Status != TransactionStatus.Success)
            throw new TransactionNotReversibleException(
                $"Only successful transactions can be reversed; '{original.Reference}' is {original.Status}.");

        //Check Original is not a reversal
        var originalIsAReversal = await dbContext.Reversals
            .AnyAsync(r => r.ReversalTransactionId == original.Id, cancellationToken);
        if (originalIsAReversal)
            throw new TransactionNotReversibleException(
                $"'{original.Reference}' is itself a reversal and cannot be reversed.");

        //Insert Reversal envelope and Reversal row
        var now = DateTimeOffset.UtcNow;
        var reversalTransaction = new Transaction
        {
            Reference = TransactionReference.Generate(),
            // Reversals carry no client key: the unique reversals row is their idempotency guard.
            // The column is still required and unique, so it gets a server-generated value.
            IdempotencyKey = $"REVERSAL-{Guid.NewGuid()}",
            InitiatedBy = currentClient.ClientId,
            Status = TransactionStatus.Pending,
            Narration = request.Reason ?? $"Reversal of {original.Reference}",
            CreatedAt = now,
        };

        // The reversal rules live in LegReverser, shared with chain reversals (decision #45).
        await legReverser.ReverseLegAsync(original, reversalTransaction, request.Reason, now, cancellationToken);

        return new TransactionResponse(
            reversalTransaction.Reference,
            reversalTransaction.Status.ToString(),
            reversalTransaction.Narration,
            reversalTransaction.CreatedAt,
            reversalTransaction.CompletedAt);
    }
}
