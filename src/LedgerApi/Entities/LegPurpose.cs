namespace LedgerApi.Entities;

// What a chain leg is for (decision #45). A chain has exactly one Principal, first; Fee and Tax legs must
// credit a System account.
public enum LegPurpose
{
    Principal,
    Fee,
    Tax
}
