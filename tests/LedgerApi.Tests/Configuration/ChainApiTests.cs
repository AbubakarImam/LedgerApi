using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LedgerApi.Authorization;
using LedgerApi.Contracts.Responses;
using LedgerApi.Tests.Fixtures;

namespace LedgerApi.Tests.Configuration;

// Chains end to end through the real app (decision #45): status codes and response shapes a client sees.
[Collection("Api")]
public class ChainApiTests(ApiFixture fixture)
{
    private const string NgnFunding = "NGN100000001"; // seeded system account

    private HttpClient Client() => fixture.CreateClient(ApiFixture.AllButClient(LedgerScopes.Admin));

    private static object Leg(string debit, string credit, decimal amount, string purpose) =>
        new { debitAccountNumber = debit, creditAccountNumber = credit, amount, purpose };

    private static async Task<string> CreateCustomerAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/accounts",
            new { accountName = "Chain API", accountType = "Wallet", currencyCode = "NGN", idempotencyKey = Guid.NewGuid().ToString() });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!.AccountNumber;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostChainAsync(HttpClient client, string key, params object[] legs)
    {
        using var response = await client.PostAsJsonAsync("/api/chains", new { idempotencyKey = key, narration = "Rent", legs });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, json.RootElement.Clone());
    }

    [Fact]
    public async Task Chain_PostGetReverse_EndToEnd()
    {
        using var client = Client();
        var payer = await CreateCustomerAsync(client);
        var payee = await CreateCustomerAsync(client);

        // Fund the payer: a one-leg chain from the seeded NGN funding account.
        var (fundStatus, _) = await PostChainAsync(client, Guid.NewGuid().ToString(), Leg(NgnFunding, payer, 1000m, "Principal"));
        Assert.Equal(HttpStatusCode.OK, fundStatus);

        var key = Guid.NewGuid().ToString();
        var (status, chain) = await PostChainAsync(client, key,
            Leg(payer, payee, 100m, "Principal"), Leg(payer, NgnFunding, 5m, "Fee"));
        Assert.Equal(HttpStatusCode.OK, status);
        var reference = chain.GetProperty("reference").GetString()!;
        Assert.Equal(2, chain.GetProperty("legs").GetArrayLength());

        // Same key, changed fee: 409.
        var (conflict, _) = await PostChainAsync(client, key,
            Leg(payer, payee, 100m, "Principal"), Leg(payer, NgnFunding, 6m, "Fee"));
        Assert.Equal(HttpStatusCode.Conflict, conflict);

        // GET returns it; an unknown chain is 404.
        using (var get = await client.GetAsync($"/api/chains/{reference}"))
        {
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        }
        using (var missing = await client.GetAsync("/api/chains/CHN-DOESNOTEXIST"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        // Reverse it with no body; a second reversal is 409.
        using (var reversal = await client.PostAsync($"/api/chains/{reference}/reversals", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, reversal.StatusCode);
            using var body = JsonDocument.Parse(await reversal.Content.ReadAsStringAsync());
            Assert.Equal(reference, body.RootElement.GetProperty("reversesChainReference").GetString());
        }
        using (var again = await client.PostAsync($"/api/chains/{reference}/reversals", content: null))
        {
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            using var problem = JsonDocument.Parse(await again.Content.ReadAsStringAsync());
            Assert.Equal("Chain has already been reversed.", problem.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task RejectedChain_Returns422_WithTheFailedLeg_AndRetriesGetTheSame()
    {
        using var client = Client();
        var payer = await CreateCustomerAsync(client); // no money
        var key = Guid.NewGuid().ToString();

        var (status, failed) = await PostChainAsync(client, key, Leg(payer, NgnFunding, 10m, "Principal"));
        var (retryStatus, retry) = await PostChainAsync(client, key, Leg(payer, NgnFunding, 10m, "Principal"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal("Failed", failed.GetProperty("status").GetString());
        Assert.Equal(1, failed.GetProperty("failedSequence").GetInt32());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, retryStatus);
        Assert.Equal(failed.GetProperty("reference").GetString(), retry.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task BadLegList_Returns400()
    {
        using var client = Client();

        var (status, _) = await PostChainAsync(client, Guid.NewGuid().ToString(),
            Leg("1", "2", 10m, "Fee"), Leg("1", "3", 10m, "Principal"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task ChainPrefixedKey_Returns400_OnTheExistingEndpoints()
    {
        using var client = Client();
        const string key = "chain:1:1";

        async Task<HttpStatusCode> PostAsync(string path, object body)
        {
            using var response = await client.PostAsJsonAsync(path, body);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync("/api/transfers",
            new { debitAccountNumber = "1", creditAccountNumber = "2", amount = 10, idempotencyKey = key }));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync("/api/deposits",
            new { customerAccountNumber = "1", amount = 10, idempotencyKey = key }));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync("/api/withdrawals",
            new { customerAccountNumber = "1", amount = 10, idempotencyKey = key }));
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync("/api/accounts",
            new { accountName = "Ada", accountType = "Savings", currencyCode = "NGN", idempotencyKey = key }));
    }

    [Fact]
    public async Task ReversingAFailedChain_Returns422_WithAChainTitle()
    {
        using var client = Client();
        var payer = await CreateCustomerAsync(client); // no money: the chain fails
        var (_, failed) = await PostChainAsync(client, Guid.NewGuid().ToString(), Leg(payer, NgnFunding, 10m, "Principal"));

        using var response = await client.PostAsync($"/api/chains/{failed.GetProperty("reference").GetString()}/reversals", content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Chain cannot be reversed.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task SingleTransactionReversal_KeepsItsTransactionTitle()
    {
        // The chain titles must not leak into single reversals.
        using var client = fixture.CreateClient(ApiFixture.OnlyClient(LedgerScopes.Reverse));

        using var response = await client.PostAsJsonAsync("/api/reversals", new { originalTransactionReference = "TXN-DOESNOTEXIST" });

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Transaction not found.", problem.RootElement.GetProperty("title").GetString());
    }
}
