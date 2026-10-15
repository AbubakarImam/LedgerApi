using LedgerApi.Auditing;
using LedgerApi.Data;
using LedgerApi.Entities;
using LedgerApi.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LedgerApi.Services;

// Reverses single legs inside a database transaction the caller owns: it never begins, commits or rolls
// back. Shared by single reversals (ReversalService) and chain reversals (ChainService).
public sealed class LegReverser(LedgerDbContext dbContext, IAuditLogger auditLogger)
{
    // Writes the reversals row (unique per original, so a second reversal is refused before any other work),
    // then the opposite entries onto reversalEnvelope, marks it Success and stages its audit row. Saves, so a
    // later leg's balance check sees these entries. Throws the mapped exceptions (409/422) on a business rule.
    public async Task ReverseLegAsync(
        Transaction original, Transaction reversalEnvelope, string? reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        //Insert Reversal row (with the envelope, added through it)
        dbContext.Reversals.Add(new Reversal
        {
            OriginalTransaction = original,
            ReversalTransaction = reversalEnvelope,
            Reason = reason,
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

        //Check Debit account status: a Frozen account can be debited by a reversal, so money can be
        //clawed back from it (decision #38); a Blocked account cannot be debited at all.
        if (debitAccount.Status == AccountStatus.Blocked)
            throw new InvalidAccountStatusException(
                $"Account '{debitAccount.AccountNumber}' is Blocked and cannot be debited.");

        //Check Credit account status
        if (creditAccount.Status == AccountStatus.Blocked)
            throw new InvalidAccountStatusException(
                $"Account '{creditAccount.AccountNumber}' is Blocked and cannot be credited.");

        //Derive Debit account balance and Check Sufficient funds
        var balance = await dbContext.LedgerEntries
            .Where(x => x.AccountId == debitAccount.Id)
            .SumAsync(x => x.EntryType == EntryType.Credit ? x.Amount : -x.Amount, cancellationToken);
        var floor = debitAccount.AccountClass == AccountClass.System ? LegPoster.SystemAccountOverdraftFloor : 0m;
        if (balance - amount < floor)
            throw new InsufficientFundsException(
                $"Account '{debitAccount.AccountNumber}' has insufficient funds to reverse '{original.Reference}'.");

        //Append Debit Entry (the account the original credited)
        await dbContext.LedgerEntries.AddAsync(new LedgerEntry
        {
            AccountId = debitAccount.Id,
            EntryType = EntryType.Debit,
            Amount = amount,
            Currency = debitAccount.CurrencyCode,
            CreatedAt = now,
            Transaction = reversalEnvelope,
            Account = debitAccount
        }, cancellationToken);

        //Append Credit Entry (the account the original debited)
        await dbContext.LedgerEntries.AddAsync(new LedgerEntry
        {
            AccountId = creditAccount.Id,
            EntryType = EntryType.Credit,
            Amount = amount,
            Currency = creditAccount.CurrencyCode,
            CreatedAt = now,
            Transaction = reversalEnvelope,
            Account = creditAccount
        }, cancellationToken);

        //Mark Reversal envelope success
        reversalEnvelope.Status = TransactionStatus.Success;
        reversalEnvelope.CompletedAt = DateTimeOffset.UtcNow;

        //Record Audit, committed with the entries and the reversals row
        auditLogger.Record(new AuditLog
        {
            Action = AuditActions.Reversal,
            EntityType = nameof(Transaction),
            EntityId = reversalEnvelope.Reference,
            DebitAccountNumber = debitAccount.AccountNumber,
            CreditAccountNumber = creditAccount.AccountNumber,
            Amount = amount,
            Currency = debitAccount.CurrencyCode,
            Status = reversalEnvelope.Status.ToString(),
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
