namespace LedgerApi.Entities;

// A chart-of-accounts category (decision #48): how an Account's balance rolls up for reporting.
// Structurally separate from Account - Account stays the only thing LegPoster/ChainService ever touch;
// GlAccount answers a different question ("how does this balance roll up"), not "what can be posted to".
public class GlAccount
{
    public long Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public GlAccountType AccountType { get; set; }

    // Null for a top-level account. A header account (one with children) aggregates its children's
    // balances for reporting; "IsHeader" is deliberately not a stored column - it is computed from
    // whether anything points here as a parent, so it can never drift from the real tree shape.
    public long? ParentGlAccountId { get; set; }
    public GlAccount? ParentGlAccount { get; set; }
    public ICollection<GlAccount> Children { get; set; } = new List<GlAccount>();

    public DateTimeOffset CreatedAt { get; set; }
}
