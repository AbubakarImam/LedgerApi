using System.Collections.Generic;

namespace LedgerApi.Configuration;

// Mirrors FundingOptions (decision #25): a currency-to-account-number map, but for the
// fee-income system account each currency credits a Fee leg to (decision #46). Not read by
// any endpoint yet - that wiring is future orchestrator/ProductApi work.
public class FeeIncomeOptions
{
    public Dictionary<string, string> Accounts { get; set; } = new();
}
