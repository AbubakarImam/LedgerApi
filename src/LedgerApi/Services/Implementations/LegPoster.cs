using LedgerApi.Data;
using LedgerApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerApi.Services;

// One leg: one debit and one matching credit for the same amount (decision #45).
public sealed record LegSpec(string DebitAccountNumber, string CreditAccountNumber, decimal Amount);

// A leg broke a business rule. Nothing about the leg has been committed; the caller decides what to store.
public sealed class LegRejectedException(string reason) : Exception(reason);

// Posts single legs inside a database transaction the caller owns: it never begins, commits or rolls back.
// Shared by plain transfers (TransferService) and chains (ChainService) so the posting rules exist once.
public sealed class LegPoster(LedgerDbContext dbContext)
{
    // Decision #14: a system account may go negative, but never below this floor.
    public const decimal SystemAccountOverdraftFloor = -1_000_000_000_000m;

    // Locks the account row for the rest of the caller's transaction (decision #7). Null if it does not exist.
    public Task<Account?> LockAccountAsync(string accountNumber, CancellationToken cancellationToken) =>
        dbContext.Accounts.FromSqlInterpolated($"""
            SELECT * FROM "accounts" WHERE "account_number" = {accountNumber}
            FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);

    // Balance derived from committed and already-saved entries (decision #1).
    public Task<decimal> BalanceAsync(Account account, CancellationToken cancellationToken) =>
        dbContext.LedgerEntries.Where(x => x.AccountId == account.Id)
            .SumAsync(x => x.EntryType == EntryType.Credit ? x.Amount : -x.Amount, cancellationToken);

    // The lowest balance this payer may reach, or null for an account class with no rule.
    public static decimal? FloorFor(Account account) => account.AccountClass switch
    {
        AccountClass.Customer => 0m,
        AccountClass.System => SystemAccountOverdraftFloor,
        _ => null,
    };

    // Checks one leg and stages its two entries on the envelope, which the caller has already added to the
    // context, then marks the envelope Success. Throws LegRejectedException on a business rule, before staging
    // anything for this leg. checkBalance false: the caller has already checked what the payer can afford.
    // Returns the debited account (its currency is the leg's currency).
    public async Task<Account> PostLegAsync(
        Transaction envelope, LegSpec leg, bool checkBalance, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (leg.Amount <= 0m)
            throw new LegRejectedException("Amount must be positive");

        //Locking Source Account
        var sourceAccount = await LockAccountAsync(leg.DebitAccountNumber, cancellationToken)
            ?? throw new LegRejectedException("Source account not found");

        //Getting Destination Account, no lock (decision #18)
        var destinationAccount = await dbContext.Accounts
            .SingleOrDefaultAsync(x => x.AccountNumber == leg.CreditAccountNumber, cancellationToken)
            ?? throw new LegRejectedException("Destination account not found");

        //Check Source account status
        if (sourceAccount.Status != AccountStatus.Active)
            throw new LegRejectedException("Source account not active");

        //Check Destination account status
        if (destinationAccount.Status == AccountStatus.Blocked)
            throw new LegRejectedException("Destination account is blocked");

        // Check Currency Code
        if (sourceAccount.CurrencyCode != destinationAccount.CurrencyCode)
            throw new LegRejectedException("Conflicting currency type");

        // Check Amount fits the currency's decimal places (e.g. whole numbers only for XOF or JPY).
        // Without this, decimal(18,2) would silently round 10.005 to 10.01 instead of rejecting it.
        if (!SupportedCurrencies.DecimalPlaces.TryGetValue(sourceAccount.CurrencyCode, out var decimalPlaces))
            throw new LegRejectedException($"Currency {sourceAccount.CurrencyCode} is not supported");
        if (decimal.Round(leg.Amount, decimalPlaces) != leg.Amount)
            throw new LegRejectedException(
                $"Amount has more decimal places than {sourceAccount.CurrencyCode} allows ({decimalPlaces})");

        if (checkBalance)
        {
            //Derive account balance and Check Sufficient funds (system accounts can go to the overdraft floor)
            var floor = FloorFor(sourceAccount)
                ?? throw new LegRejectedException($"Unrecognized account class: {sourceAccount.AccountClass}");
            var balance = await BalanceAsync(sourceAccount, cancellationToken);
            if (balance - leg.Amount < floor)
                throw new LegRejectedException("Insufficient Account Balance");
        }

        //Append Debit Entry
        await dbContext.LedgerEntries.AddAsync(new LedgerEntry
        {
            AccountId = sourceAccount.Id,
            EntryType = EntryType.Debit,
            Amount = leg.Amount,
            Currency = sourceAccount.CurrencyCode,
            CreatedAt = now,
            Transaction = envelope,
            Account = sourceAccount
        }, cancellationToken);

        //Append Credit Entry
        await dbContext.LedgerEntries.AddAsync(new LedgerEntry
        {
            AccountId = destinationAccount.Id,
            EntryType = EntryType.Credit,
            Amount = leg.Amount,
            Currency = destinationAccount.CurrencyCode,
            CreatedAt = now,
            Transaction = envelope,
            Account = destinationAccount
        }, cancellationToken);

        envelope.Status = TransactionStatus.Success;
        envelope.CompletedAt = DateTimeOffset.UtcNow;
        return sourceAccount;
    }
}
