using LedgerApi.Auditing;
using LedgerApi.Contracts.Requests;
using LedgerApi.Contracts.Responses;
using LedgerApi.Data;
using LedgerApi.Entities;
using LedgerApi.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LedgerApi.Services;

public class GlAccountService(LedgerDbContext dbContext, IAuditLogger auditLogger) : IGlAccountService
{
    private static GlAccountResponse ToResponse(GlAccount glAccount) => new(
        glAccount.Id,
        glAccount.Code,
        glAccount.Name,
        glAccount.AccountType.ToString(),
        glAccount.ParentGlAccountId,
        glAccount.CreatedAt);

    public async Task<GlAccountResponse> CreateGlAccountAsync(CreateGlAccountRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ParentGlAccountId is { } parentId
            && !await dbContext.GlAccounts.AnyAsync(g => g.Id == parentId, cancellationToken))
        {
            throw new GlAccountNotFoundException($"GL account '{parentId}' (parent) does not exist.");
        }

        var glAccount = new GlAccount
        {
            Code = request.Code,
            Name = request.Name,
            AccountType = Enum.Parse<GlAccountType>(request.AccountType, ignoreCase: true),
            ParentGlAccountId = request.ParentGlAccountId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await dbContext.GlAccounts.AddAsync(glAccount, cancellationToken);

        auditLogger.Record(new AuditLog
        {
            Action = AuditActions.GlAccountCreated,
            EntityType = nameof(GlAccount),
            EntityId = request.Code,
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505", ConstraintName: "ix_gl_accounts_code" })
        {
            throw new DuplicateGlAccountCodeException($"GL account code '{request.Code}' already exists.");
        }

        return ToResponse(glAccount);
    }

    public async Task<IReadOnlyList<GlAccountResponse>> ListGlAccountsAsync(CancellationToken cancellationToken = default)
    {
        var glAccounts = await dbContext.GlAccounts.AsNoTracking().OrderBy(g => g.Code).ToListAsync(cancellationToken);
        return glAccounts.Select(ToResponse).ToList();
    }
}
