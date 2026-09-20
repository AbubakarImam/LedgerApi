using System.Collections.Generic;

namespace LedgerApi.Configuration;

public class FundingOptions
{
    public Dictionary<string, string> Accounts { get; set; } = new();
}