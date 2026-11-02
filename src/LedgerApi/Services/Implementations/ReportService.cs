using LedgerApi.Contracts.Responses;
using LedgerApi.Data;
using LedgerApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace LedgerApi.Services;

// The trial-balance report (decision #48): every GlAccount's rolled-up balance, grouped by currency.
// Reuses decision #1's existing derived-balance mechanism (SUM(credits) - SUM(debits) over
// LedgerEntries) - this adds no second balance mechanism, only a grouping/rollup on top of it.
public class ReportService(LedgerDbContext dbContext) : IReportService
{
    private sealed record GlAccountBalanceRow(long GlAccountId, string Currency, decimal Balance);

    public async Task<TrialBalanceResponse> GetTrialBalanceAsync(CancellationToken cancellationToken = default)
    {
        var glAccounts = await dbContext.GlAccounts.AsNoTracking().OrderBy(g => g.Code).ToListAsync(cancellationToken);

        // One row per (GlAccountId, currency) that an account is actually tagged with and has entries for.
        // A GlAccount with no directly-tagged accounts, or whose accounts have no postings yet, simply has
        // no row here - its OwnBalances list below is then empty, not an error.
        var balanceRows = await (
            from entry in dbContext.LedgerEntries
            join account in dbContext.Accounts on entry.AccountId equals account.Id
            where account.GlAccountId != null
            group entry by new { account.GlAccountId, account.CurrencyCode } into g
            select new GlAccountBalanceRow(
                g.Key.GlAccountId!.Value,
                g.Key.CurrencyCode,
                g.Sum(e => e.EntryType == EntryType.Credit ? e.Amount : -e.Amount)))
            .ToListAsync(cancellationToken);

        var childrenByParent = glAccounts
            .Where(g => g.ParentGlAccountId is not null)
            .GroupBy(g => g.ParentGlAccountId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var entries = glAccounts
            .Select(g => BuildEntry(g, glAccounts, balanceRows, childrenByParent))
            .ToList();

        return new TrialBalanceResponse(entries, DateTimeOffset.UtcNow);
    }

    private static TrialBalanceEntry BuildEntry(
        GlAccount glAccount,
        IReadOnlyList<GlAccount> allGlAccounts,
        IReadOnlyList<GlAccountBalanceRow> balanceRows,
        IReadOnlyDictionary<long, List<GlAccount>> childrenByParent)
    {
        var isHeader = childrenByParent.ContainsKey(glAccount.Id);
        var descendantIds = DescendantIdsIncludingSelf(glAccount.Id, childrenByParent);

        var ownBalances = SumByCurrency(balanceRows, [glAccount.Id]);
        var rolledUpBalances = isHeader ? SumByCurrency(balanceRows, descendantIds) : ownBalances;

        return new TrialBalanceEntry(
            glAccount.Id,
            glAccount.Code,
            glAccount.Name,
            glAccount.AccountType.ToString(),
            glAccount.ParentGlAccountId,
            isHeader,
            ownBalances,
            rolledUpBalances);
    }

    private static List<long> DescendantIdsIncludingSelf(long rootId, IReadOnlyDictionary<long, List<GlAccount>> childrenByParent)
    {
        var result = new List<long> { rootId };
        if (childrenByParent.TryGetValue(rootId, out var children))
        {
            foreach (var child in children)
            {
                result.AddRange(DescendantIdsIncludingSelf(child.Id, childrenByParent));
            }
        }
        return result;
    }

    private static IReadOnlyList<CurrencyBalance> SumByCurrency(IReadOnlyList<GlAccountBalanceRow> rows, IReadOnlyCollection<long> glAccountIds)
    {
        return rows
            .Where(r => glAccountIds.Contains(r.GlAccountId))
            .GroupBy(r => r.Currency)
            .Select(g => new CurrencyBalance(g.Key, SupportedCurrencies.ToCurrencyScale(g.Sum(r => r.Balance), g.Key)))
            .OrderBy(cb => cb.CurrencyCode)
            .ToList();
    }
}
