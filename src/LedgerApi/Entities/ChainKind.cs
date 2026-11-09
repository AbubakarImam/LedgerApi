namespace LedgerApi.Entities;

// What kind of chain this is (decision #53, extending decision #45). Payment: the original shape, a single
// Principal leg with optional Fee/Tax legs. Disbursement: one funding account paying many distinct
// recipients. Collection: many distinct payers collected into one account. The three kinds never mix --
// a chain's legs are all one purpose that matches its kind (ChainRequestValidator).
public enum ChainKind
{
    Payment,
    Disbursement,
    Collection
}
