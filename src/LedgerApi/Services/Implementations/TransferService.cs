using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;
using LedgerApi.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using System.Data.Common;


namespace LedgerApi.Services;

public class TransferService(LedgerDbContext dbContext) : ITransferService
{
    //accept idempotency key from the request(client-supplied)
    // Attempt to insert transaction envelope with the key, inside transaction table
    // On unique-constraint violation: fetch and return the stored result for the key no reprocessing
    //lock the row on account debit
    // Fetch destination account no locking
    // check if source account allows debit
    // check if destination account allows credit
    // Check currency matches on both account
    //derived the balance of debit account
    // check balance sufficiency (system accounts can go to overdraft floor value)
    // write debit entry into account A
    // write credit entry into account b
    // mark transaction envelope status = success, set completed_at
    //commit all changes 
    // on business logic failure at any check it thows one of the scaffolded exceptions; mark envelope statuss = failed, with reason. commit only those
    // Map to transferResponse, return

    private async Task<TransferResponse> FailTransferAsync(
    Transaction transaction, string reason, IDbContextTransaction dbTransaction, CancellationToken cancellationToken)
    {
        transaction.Status = TransactionStatus.Failed;
        transaction.CompletedAt = DateTimeOffset.UtcNow;
        transaction.FailureReason = reason;
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbTransaction.CommitAsync(cancellationToken);
        return new TransferResponse(transaction.Reference, transaction.Status.ToString());
    }
    public async Task<TransferResponse> TransferAsync(TransferRequest request, CancellationToken cancellationToken = default)
    {

        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        //const int attempts = 10;
        //var inserted = false;
        //for (int i = 0; i < attempts; i++) {
            const string allowedChar = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            const int length = 15;

            var reference = new char[length];
            var now = DateTimeOffset.UtcNow;


            for (int j = 0; j < length; j++)
            {
                int indexChar = Random.Shared.Next(allowedChar.Length);
                reference[j] = allowedChar[indexChar]; 
            }
            var myReference = $"TXN-" + new string (reference);
            var transaction = new Transaction
            {
                Reference = myReference,
                IdempotencyKey = request.IdempotencyKey,
                Status = TransactionStatus.Pending,
                Narration = request.Narration,
                CreatedAt = now


            };
            await dbContext.Transactions.AddAsync(transaction);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            //inserted = true;
            //break;
            //Locking Account logic

        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pgEx)
        {
            if (pgEx.ConstraintName == "ix_transactions_idempotency_key")
            {
                var dbInsertedTransaction = await dbContext.Transactions.FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
                if (dbInsertedTransaction != null)
                {
                    await dbTransaction.RollbackAsync(cancellationToken);
                    return new TransferResponse(
                        dbInsertedTransaction.Reference,
                        dbInsertedTransaction.Status.ToString()
                        );


                }
                else
                {
                    await dbTransaction.RollbackAsync(cancellationToken);

                    throw new InvalidOperationException("Error retriving the result of the existing indemmpotency value");
                }
            }
            else
            {
                await dbTransaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("Unexpected constraint violation");
            }
        }
        catch
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }

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
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
        if (sourceAccount is null)
            return await FailTransferAsync(transaction, "Source account not found", dbTransaction, cancellationToken);

    }
}
