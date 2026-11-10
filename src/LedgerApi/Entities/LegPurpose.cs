namespace LedgerApi.Entities;

// What a chain leg is for (decision #45, extended by #53). A Payment chain has exactly one Principal,
// first; Fee and Tax legs must credit a System account. Disbursement and Collection are their own chain
// kinds' only allowed purpose (ChainRequestValidator) -- a Disbursement/Collection chain's legs never mix
// with Principal/Fee/Tax, and vice versa.
public enum LegPurpose
{
    Principal,
    Fee,
    Tax,
    Disbursement,
    Collection
}
