namespace LedgerApi.Auditing;

// The state changes that are audited. Outcome (success/failed) is in AuditLog.Status, not the action name.
public static class AuditActions
{
    public const string CustomerAccountCreated = "CustomerAccountCreated";
    public const string SystemAccountCreated = "SystemAccountCreated";
    public const string Transfer = "Transfer";
    public const string Reversal = "Reversal";
}
