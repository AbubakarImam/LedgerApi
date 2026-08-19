namespace LedgerApi.Contracts.Requests;

public record ReversalRequest(
    string OriginalTransactionReference,
    string? Reason);
