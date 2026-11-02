namespace LedgerApi.Contracts.Requests;

public record CreateGlAccountRequest(
    string Code,
    string Name,
    string AccountType,
    long? ParentGlAccountId);
