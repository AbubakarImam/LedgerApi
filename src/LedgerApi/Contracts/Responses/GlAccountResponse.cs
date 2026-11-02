namespace LedgerApi.Contracts.Responses;

public record GlAccountResponse(
    long Id,
    string Code,
    string Name,
    string AccountType,
    long? ParentGlAccountId,
    DateTimeOffset CreatedAt);
