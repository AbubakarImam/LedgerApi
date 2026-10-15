using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LedgerApi.Auditing;
using LedgerApi.Authorization;
using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;
using LedgerApi.Entities;
using LedgerApi.Exceptions;
using LedgerApi.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace LedgerApi.Services;

// Posts chains of legs (decision #45): an ordered list of ordinary two-entry transactions that commit in ONE
// database transaction or not at all.
//
// accept the chain from the request (validated: 1-10 legs, Principal first and only once, amounts > 0)
// insert the chain row first, with the calling client, the key and a hash of the legs (decision #42)
// on unique violation for (client, key): same hash -> return the stored chain; different hash -> 409
// lock every debited account once, deduplicated, in ascending ordinal account-number order
// derive each paying account's balance once
// post each leg in order through LegPoster (per-leg rules as for a transfer), then the chain rules:
//   Fee/Tax legs credit a System account; every leg in the chain's currency;
//   each payer's running total across its legs fits the balance derived up front (floor for System)
// stage an audit row per leg and a ChainPosted row; mark the chain Success; commit
// business rejection: roll back every leg, then store the chain as Failed (reason + failed sequence) with its
//   audit row in a second small save; a same-key retry returns it (decision #6)
// technical failure: roll back, store nothing, rethrow
//
// Reversal: one reversing leg per original leg, last leg first, in one database transaction, through the
// single-leg LegReverser. Stored as a chain with reverses_chain_id (unique: a second reversal is 409). The
// accounts the reversing legs debit are locked first, once each, in ascending ordinal order. Frozen accounts
// may be debited (decision #38). Any leg failing rolls back the whole reversal.
public class ChainService(LedgerDbContext dbContext, IAuditLogger auditLogger, ICurrentClient currentClient) : IChainService
{
    private const string IdempotencyIndex = "ix_transaction_chains_initiated_by_idempotency_key";
    private const string ReversesChainIndex = "ix_transaction_chains_reverses_chain_id";

    private readonly LegPoster legPoster = new(dbContext);
    private readonly LegReverser legReverser = new(dbContext, auditLogger);

    // A leg was rejected: the whole chain fails at this 1-based position.
    private sealed class ChainRejectedException(int sequence, string reason) : Exception(reason)
    {
        public int Sequence { get; } = sequence;
    }

    private sealed record Leg(LegSpec Spec, LegPurpose Purpose);

    public async Task<ChainResponse> PostAsync(ChainRequest request, CancellationToken cancellationToken = default)
    {
        var clientId = currentClient.ClientId;
        var legs = request.Legs
            .Select(l => new Leg(
                new LegSpec(l.DebitAccountNumber, l.CreditAccountNumber, l.Amount),
                ChainRequestValidator.ParsePurpose(l.Purpose)!.Value))
            .ToList();
        var requestHash = HashLegs(legs);
        var reference = TransactionReference.Generate("CHN");
        var now = DateTimeOffset.UtcNow;

        ChainRejectedException rejection;
        await using (var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var chain = new TransactionChain
            {
                Reference = reference,
                InitiatedBy = clientId,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = requestHash,
                Status = TransactionStatus.Pending,
                Narration = request.Narration,
                CreatedAt = now,
            };
            dbContext.TransactionChains.Add(chain);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsIdempotencyConflict(ex))
            {
                await RollbackAsync(dbTransaction);
                return await ReplayAsync(clientId, request.IdempotencyKey, requestHash, cancellationToken);
            }
            catch
            {
                await RollbackAsync(dbTransaction);
                throw;
            }

            try
            {
                var postedLegs = await PostLegsAsync(chain, legs, request.Narration, clientId, now, cancellationToken);

                chain.Status = TransactionStatus.Success;
                chain.CompletedAt = DateTimeOffset.UtcNow;
                RecordChainAudit(chain, AuditActions.ChainPosted, legs.Sum(l => l.Spec.Amount), postedLegs[0].Currency);

                await dbContext.SaveChangesAsync(cancellationToken);
                await dbTransaction.CommitAsync(cancellationToken);
                return ToResponse(chain, postedLegs, reversesReference: null, reversedByReference: null);
            }
            catch (ChainRejectedException rejected)
            {
                await RollbackAsync(dbTransaction);
                rejection = rejected;
            }
            catch
            {
                await RollbackAsync(dbTransaction);
                throw;
            }
        }

        return await StoreFailedAsync(request, clientId, requestHash, reference, now, rejection, cancellationToken);
    }

    public async Task<ChainResponse?> GetAsync(string reference, CancellationToken cancellationToken = default)
    {
        var chain = await dbContext.TransactionChains.AsNoTracking()
            .Include(c => c.ReversesChain)
            .SingleOrDefaultAsync(c => c.Reference == reference, cancellationToken);
        return chain is null ? null : await LoadResponseAsync(chain, cancellationToken);
    }

    public async Task<ChainResponse> ReverseAsync(string reference, string? reason, CancellationToken cancellationToken = default)
    {
        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var response = await ReverseInsideTransactionAsync(reference, reason, cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            // Any leg failing rolls back the whole reversal; nothing is stored (as for single reversals, #27).
            await RollbackAsync(dbTransaction);
            throw;
        }
    }

    private async Task<ChainResponse> ReverseInsideTransactionAsync(string reference, string? reason, CancellationToken cancellationToken)
    {
        var clientId = currentClient.ClientId;
        var now = DateTimeOffset.UtcNow;

        //Getting the original chain with its legs, their entries and accounts
        var original = await dbContext.TransactionChains
            .Include(c => c.Legs).ThenInclude(l => l.Entries).ThenInclude(e => e.Account)
            .SingleOrDefaultAsync(c => c.Reference == reference, cancellationToken)
            ?? throw new ChainNotFoundException($"Chain '{reference}' was not found.");

        if (original.Status != TransactionStatus.Success)
            throw new ChainNotReversibleException(
                $"Only successful chains can be reversed; '{original.Reference}' is {original.Status}.");
        if (original.ReversesChainId is not null)
            throw new ChainNotReversibleException(
                $"'{original.Reference}' is itself a reversal and cannot be reversed.");

        //Insert the reversal chain first: the unique reverses_chain_id refuses a second reversal before any work
        var reversal = new TransactionChain
        {
            Reference = TransactionReference.Generate("CHN"),
            InitiatedBy = clientId,
            // Reversals carry no client key; a generated one keeps the (client, key) index from treating two
            // reversals by the same client as duplicates. reverses_chain_id is the real guard.
            IdempotencyKey = $"REVERSAL-{Guid.NewGuid()}",
            Status = TransactionStatus.Pending,
            Narration = reason ?? $"Reversal of {original.Reference}",
            ReversesChainId = original.Id,
            CreatedAt = now,
        };
        dbContext.TransactionChains.Add(reversal);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ReversesChainIndex
        })
        {
            throw new ChainAlreadyReversedException($"Chain '{original.Reference}' has already been reversed.", ex);
        }

        //Lock the accounts the reversing legs debit (each original leg's credit side), once each, ascending order
        foreach (var number in original.Legs
                     .Select(l => l.Entries.Single(e => e.EntryType == EntryType.Credit).Account.AccountNumber)
                     .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            await legPoster.LockAccountAsync(number, cancellationToken);
        }

        //Reverse each leg, last leg first (e.g. Tax, Fee, Principal)
        var reversingLegs = new List<ChainLegResponse>();
        var lastFirst = original.Legs.OrderByDescending(l => l.Sequence).ToList();
        for (var i = 0; i < lastFirst.Count; i++)
        {
            var originalLeg = lastFirst[i];
            var sequence = i + 1;
            var envelope = new Transaction
            {
                Reference = TransactionReference.Generate(),
                IdempotencyKey = $"chain:{reversal.Id}:{sequence}",
                InitiatedBy = clientId,
                Status = TransactionStatus.Pending,
                Narration = reason ?? $"Reversal of {originalLeg.Reference}",
                CreatedAt = now,
                Chain = reversal,
                Sequence = sequence,
                Purpose = originalLeg.Purpose,
            };

            await legReverser.ReverseLegAsync(originalLeg, envelope, reason, now, cancellationToken);

            var originalDebit = originalLeg.Entries.Single(e => e.EntryType == EntryType.Debit);
            var originalCredit = originalLeg.Entries.Single(e => e.EntryType == EntryType.Credit);
            reversingLegs.Add(new ChainLegResponse(sequence, envelope.Purpose!.Value.ToString(), envelope.Reference,
                originalCredit.Account.AccountNumber, originalDebit.Account.AccountNumber,
                SupportedCurrencies.ToCurrencyScale(originalDebit.Amount, originalDebit.Currency), originalDebit.Currency,
                envelope.Status.ToString()));
        }

        reversal.Status = TransactionStatus.Success;
        reversal.CompletedAt = DateTimeOffset.UtcNow;
        RecordChainAudit(reversal, AuditActions.ChainReversed, reversingLegs.Sum(l => l.Amount), reversingLegs[0].Currency);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(reversal, reversingLegs, reversesReference: original.Reference, reversedByReference: null);
    }

    private async Task<List<ChainLegResponse>> PostLegsAsync(
        TransactionChain chain, List<Leg> legs, string? narration, string? clientId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Lock every debited account once, in ascending ordinal account-number order, before posting anything:
        // any two chains lock shared accounts in the same order, so they cannot deadlock (decision #45).
        // An account that does not exist is reported by LegPoster at its leg.
        var lockedPayers = new Dictionary<string, Account>(StringComparer.Ordinal);
        foreach (var number in legs.Select(l => l.Spec.DebitAccountNumber).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (await legPoster.LockAccountAsync(number, cancellationToken) is { } account)
                lockedPayers[number] = account;
        }

        // One balance per paying account, derived up front. Money a payer receives earlier in the same chain is
        // not counted: it must already afford everything it pays (decision #45).
        var available = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var (number, account) in lockedPayers)
            available[number] = await legPoster.BalanceAsync(account, cancellationToken);
        var paid = new Dictionary<string, decimal>(StringComparer.Ordinal);

        string? chainCurrency = null;
        var posted = new List<ChainLegResponse>();
        for (var i = 0; i < legs.Count; i++)
        {
            var (spec, purpose) = legs[i];
            var sequence = i + 1;
            var leg = new Transaction
            {
                Reference = TransactionReference.Generate(),
                // Generated, so leg keys can never collide with a client's own keys (those may not start with "chain:").
                IdempotencyKey = $"chain:{chain.Id}:{sequence}",
                InitiatedBy = clientId,
                Status = TransactionStatus.Pending,
                Narration = narration is null ? purpose.ToString() : $"{narration} · {purpose}",
                CreatedAt = now,
                Chain = chain,
                Sequence = sequence,
                Purpose = purpose,
            };
            dbContext.Transactions.Add(leg);

            try
            {
                // Per-leg rules, as for a transfer (decision #45); the payer's balance is checked below for the chain.
                var debit = await legPoster.PostLegAsync(leg, spec, checkBalance: false, now, cancellationToken);
                var credit = await dbContext.Accounts.SingleAsync(a => a.AccountNumber == spec.CreditAccountNumber, cancellationToken);

                if (purpose is LegPurpose.Fee or LegPurpose.Tax && credit.AccountClass != AccountClass.System)
                    throw new LegRejectedException($"{purpose} legs must credit a system account");

                chainCurrency ??= debit.CurrencyCode;
                if (debit.CurrencyCode != chainCurrency)
                    throw new LegRejectedException("All legs in a chain must use the same currency");

                var floor = LegPoster.FloorFor(debit)
                    ?? throw new LegRejectedException($"Unrecognized account class: {debit.AccountClass}");
                paid[spec.DebitAccountNumber] = paid.GetValueOrDefault(spec.DebitAccountNumber) + spec.Amount;
                if (available[spec.DebitAccountNumber] - paid[spec.DebitAccountNumber] < floor)
                    throw new LegRejectedException("Insufficient Account Balance");

                // Audit, committed with the postings: the existing transfer action, one row per leg.
                auditLogger.Record(new AuditLog
                {
                    Action = AuditActions.Transfer,
                    EntityType = nameof(Transaction),
                    EntityId = leg.Reference,
                    DebitAccountNumber = spec.DebitAccountNumber,
                    CreditAccountNumber = spec.CreditAccountNumber,
                    Amount = spec.Amount,
                    Currency = debit.CurrencyCode,
                    Status = leg.Status.ToString(),
                });

                posted.Add(new ChainLegResponse(sequence, purpose.ToString(), leg.Reference, spec.DebitAccountNumber,
                    spec.CreditAccountNumber, SupportedCurrencies.ToCurrencyScale(spec.Amount, debit.CurrencyCode),
                    debit.CurrencyCode, leg.Status.ToString()));
            }
            catch (LegRejectedException rejected)
            {
                throw new ChainRejectedException(sequence, rejected.Message);
            }

            // Save each leg, so its id exists and its entries are visible to later reads in this transaction.
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return posted;
    }

    // Second, small save after a rolled-back chain: store it as Failed with its audit row (decision #6). If a
    // same-key retry stored its own chain in the meantime, return that one instead.
    private async Task<ChainResponse> StoreFailedAsync(
        ChainRequest request, string? clientId, string requestHash, string reference, DateTimeOffset now,
        ChainRejectedException rejection, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var failed = new TransactionChain
        {
            Reference = reference,
            InitiatedBy = clientId,
            IdempotencyKey = request.IdempotencyKey,
            RequestHash = requestHash,
            Status = TransactionStatus.Failed,
            Narration = request.Narration,
            FailureReason = rejection.Message,
            FailedSequence = rejection.Sequence,
            CreatedAt = now,
            CompletedAt = DateTimeOffset.UtcNow,
        };
        dbContext.TransactionChains.Add(failed);
        RecordChainAudit(failed, AuditActions.ChainPosted, request.Legs.Sum(l => l.Amount), currency: null);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyConflict(ex))
        {
            dbContext.ChangeTracker.Clear();
            return await ReplayAsync(clientId, request.IdempotencyKey, requestHash, cancellationToken);
        }

        return ToResponse(failed, [], reversesReference: null, reversedByReference: null);
    }

    // The key is already taken by this client: same legs -> the stored chain; different legs -> 409.
    private async Task<ChainResponse> ReplayAsync(
        string? clientId, string idempotencyKey, string requestHash, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var stored = await dbContext.TransactionChains.AsNoTracking()
            .Include(c => c.ReversesChain)
            .SingleAsync(c => c.InitiatedBy == clientId && c.IdempotencyKey == idempotencyKey, cancellationToken);

        if (stored.RequestHash != requestHash)
            throw new DuplicateIdempotencyKeyException(
                $"Idempotency key '{idempotencyKey}' was already used for a different request.");

        return await LoadResponseAsync(stored, cancellationToken);
    }

    private async Task<ChainResponse> LoadResponseAsync(TransactionChain chain, CancellationToken cancellationToken)
    {
        var legs = await dbContext.Transactions.AsNoTracking()
            .Where(t => t.ChainId == chain.Id)
            .Include(t => t.Entries).ThenInclude(e => e.Account)
            .OrderBy(t => t.Sequence)
            .ToListAsync(cancellationToken);
        var reversedBy = await dbContext.TransactionChains.AsNoTracking()
            .Where(c => c.ReversesChainId == chain.Id)
            .Select(c => c.Reference)
            .SingleOrDefaultAsync(cancellationToken);

        var legResponses = legs.Select(t =>
        {
            var debit = t.Entries.Single(e => e.EntryType == EntryType.Debit);
            var credit = t.Entries.Single(e => e.EntryType == EntryType.Credit);
            return new ChainLegResponse(t.Sequence!.Value, t.Purpose!.Value.ToString(), t.Reference,
                debit.Account.AccountNumber, credit.Account.AccountNumber,
                SupportedCurrencies.ToCurrencyScale(debit.Amount, debit.Currency), debit.Currency, t.Status.ToString());
        }).ToList();

        return ToResponse(chain, legResponses, chain.ReversesChain?.Reference, reversedBy);
    }

    private static ChainResponse ToResponse(
        TransactionChain chain, IReadOnlyList<ChainLegResponse> legs, string? reversesReference, string? reversedByReference) =>
        new(chain.Reference, chain.Status.ToString(), chain.Narration, chain.FailureReason, chain.FailedSequence,
            reversesReference, reversedByReference, chain.CreatedAt, chain.CompletedAt, legs);

    private void RecordChainAudit(TransactionChain chain, string action, decimal totalAmount, string? currency) =>
        auditLogger.Record(new AuditLog
        {
            Action = action,
            EntityType = nameof(TransactionChain),
            EntityId = chain.Reference,
            Amount = totalAmount,
            Currency = currency,
            Status = chain.Status.ToString(),
        });

    // SHA-256 of the ordered legs as [debit, credit, amount (G29, so 100 = 100.00), purpose]. Narration is left
    // out, as for transfers (decision #44).
    private static string HashLegs(IEnumerable<Leg> legs) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            legs.Select(l => new[]
            {
                l.Spec.DebitAccountNumber,
                l.Spec.CreditAccountNumber,
                l.Spec.Amount.ToString("G29", CultureInfo.InvariantCulture),
                l.Purpose.ToString(),
            })))));

    private static bool IsIdempotencyConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IdempotencyIndex };

    // Cleanup, not work: runs even if the client disconnected (see TransferService).
    private static Task RollbackAsync(IDbContextTransaction dbTransaction) =>
        dbTransaction.RollbackAsync(CancellationToken.None);
}
