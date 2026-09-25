using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;
using LedgerApi.Entities;
using LedgerApi.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LedgerApi.Services;

public class ReversalService(LedgerDbContext dbContext) : IReversalService
{
    // Decision #14: a system account may go negative, but never below this floor.
    private const decimal SystemAccountOverdraftFloor = -1_000_000_000_000m;

    // accept the original transaction reference from the request
    // open one db transaction so everything commits together or not at all
    // find the original transaction by reference, with its entries (not found -> 404)
    // check the original transaction was successful (failed or pending moved no money -> 422)
    // check the original is not itself a reversal (that would redo the original transfer -> 422)
    // insert the reversal envelope and the reversals row in one save
    // on unique-constraint violation on original_transaction_id: already reversed -> 409, no locking or balance work done
    // lock the account being debited now (original credit side)
    // fetch the account being credited now (original debit side), no locking
    // check the account being debited now (original credit side) is active
    // check the account being credited now (original debit side) is not blocked
    // derive the balance of the account being debited
    // check balance sufficiency (system accounts can go to overdraft floor value)
    // write debit entry into the account the original credited
    // write credit entry into the account the original debited
    // mark reversal envelope status = success, set completed_at
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
            Status = TransactionStatus.Pending,
            Narration = request.Reason ?? $"Reversal of {original.Reference}",
            CreatedAt = now,
        };

        dbContext.Reversals.Add(new Reversal
        {
            OriginalTransaction = original,
            ReversalTransaction = reversalTransaction,
            Reason = request.Reason,
            CreatedAt = now,
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_reversals_original_transaction_id"
        })
        {
            throw new AlreadyReversedException($"Transaction '{original.Reference}' has already been reversed.", ex);
        }

        //Locking Account being debited (original credit side)
        var debitAccountId = original.Entries.Single(e => e.EntryType == EntryType.Credit).AccountId;
        var debitAccount = await dbContext.Accounts.FromSqlInterpolated($"""
            SELECT * FROM "accounts" WHERE "id" = {debitAccountId}
            FOR UPDATE
            """).SingleAsync(cancellationToken);

        //Getting Account being credited (original debit side), no lock (decision #18)
        var creditAccountId = original.Entries.Single(e => e.EntryType == EntryType.Debit).AccountId;
        var creditAccount = await dbContext.Accounts.SingleAsync(a => a.Id == creditAccountId, cancellationToken);

        //Reversal moves the original amount back (both original entries carry the same amount)
        var amount = original.Entries.First().Amount;

        //Check Debit account status
        if (debitAccount.Status != AccountStatus.Active)
            throw new InvalidAccountStatusException(
                $"Account '{debitAccount.AccountNumber}' is {debitAccount.Status} and cannot be debited.");

        //Check Credit account status
        if (creditAccount.Status == AccountStatus.Blocked)
            throw new InvalidAccountStatusException(
                $"Account '{creditAccount.AccountNumber}' is Blocked and cannot be credited.");

        //Derive Debit account balance
        var balance = await dbContext.LedgerEntries
            .Where(x => x.AccountId == debitAccount.Id)
            .SumAsync(x => x.EntryType == EntryType.Credit ? x.Amount : -x.Amount, cancellationToken);

        //Check Sufficient funds
        var floor = debitAccount.AccountClass == AccountClass.System ? SystemAccountOverdraftFloor : 0m;
        if (balance - amount < floor)
            throw new InsufficientFundsException(
                $"Account '{debitAccount.AccountNumber}' has insufficient funds to reverse '{original.Reference}'.");

        //Append Debit Entry (the account the original credited)
        var debitEntry = new LedgerEntry
        {
            AccountId = debitAccount.Id,
            EntryType = EntryType.Debit,
            Amount = amount,
            Currency = debitAccount.CurrencyCode,
            CreatedAt = now,
            Transaction = reversalTransaction,
            Account = debitAccount
        };
        await dbContext.LedgerEntries.AddAsync(debitEntry, cancellationToken);

        //Append Credit Entry (the account the original debited)
        var creditEntry = new LedgerEntry
        {
            AccountId = creditAccount.Id,
            EntryType = EntryType.Credit,
            Amount = amount,
            Currency = creditAccount.CurrencyCode,
            CreatedAt = now,
            Transaction = reversalTransaction,
            Account = creditAccount
        };
        await dbContext.LedgerEntries.AddAsync(creditEntry, cancellationToken);

        //Mark Reversal envelope success (commit happens in ReverseAsync)
        reversalTransaction.Status = TransactionStatus.Success;
        reversalTransaction.CompletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TransactionResponse(
            reversalTransaction.Reference,
            reversalTransaction.Status.ToString(),
            reversalTransaction.Narration,
            reversalTransaction.CreatedAt,
            reversalTransaction.CompletedAt);
    }
}
