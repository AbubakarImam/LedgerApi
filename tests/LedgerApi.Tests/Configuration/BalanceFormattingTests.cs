using System.Net.Http.Json;
using System.Text.Json;
using LedgerApi.Authorization;
using LedgerApi.Contracts.Responses;
using LedgerApi.Tests.Fixtures;

namespace LedgerApi.Tests.Configuration;

// End to end: the raw JSON a client receives for a balance, which is where the formatting shows.
[Collection("Api")]
public class BalanceFormattingTests(ApiFixture fixture)
{
    private async Task<string> CreateAccountAsync(string currency)
    {
        using var client = fixture.CreateClient(ApiFixture.OnlyClient(LedgerScopes.Accounts));
        using var response = await client.PostAsJsonAsync("/api/accounts",
            new { accountName = "Balance Format", accountType = "Wallet", currencyCode = currency });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!.AccountNumber;
    }

    private async Task<string> RawBalanceAsync(string accountNumber)
    {
        using var client = fixture.CreateClient(ApiFixture.OnlyClient(LedgerScopes.Read));
        using var balance = JsonDocument.Parse(await client.GetStringAsync($"/api/accounts/{accountNumber}/balance"));
        return balance.RootElement.GetProperty("balance").GetRawText();
    }

    [Fact]
    public async Task ZeroDecimalCurrency_BalanceHasNoDecimals()
    {
        var accountNumber = await CreateAccountAsync("XOF");
        using (var client = fixture.CreateClient(ApiFixture.OnlyClient(LedgerScopes.Funding)))
        {
            using var deposit = await client.PostAsJsonAsync("/api/deposits",
                new { customerAccountNumber = accountNumber, amount = 1500, idempotencyKey = Guid.NewGuid().ToString() });
            deposit.EnsureSuccessStatusCode();
        }

        Assert.Equal("1500", await RawBalanceAsync(accountNumber));
    }

    [Fact]
    public async Task TwoDecimalCurrency_EmptyBalanceHasTwoDecimals()
    {
        var accountNumber = await CreateAccountAsync("NGN");

        Assert.Equal("0.00", await RawBalanceAsync(accountNumber));
    }
}
