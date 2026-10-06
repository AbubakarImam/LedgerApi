using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LedgerApi.Authorization;
using LedgerApi.Tests.Fixtures;

namespace LedgerApi.Tests.Configuration;

// End to end through the real app: what a client sees when it reuses an idempotency key (decision #42).
[Collection("Api")]
public class IdempotencyApiTests(ApiFixture fixture)
{
    // Unknown accounts make every transfer a committed business failure (422), so no seed data is needed.
    private static object Transfer(string key, decimal amount) => new
    {
        debitAccountNumber = "9000000001",
        creditAccountNumber = "9000000002",
        amount,
        narration = "idempotency",
        idempotencyKey = key,
    };

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/transfers", body);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, json.RootElement.Clone());
    }

    [Fact]
    public async Task ReusedKey_WithADifferentBody_Returns409_AndAnExactRetryStillReplays()
    {
        using var client = fixture.CreateClient(ApiFixture.OnlyClient(LedgerScopes.Transfer));
        var key = Guid.NewGuid().ToString();

        var (firstStatus, first) = await PostAsync(client, Transfer(key, 10m));
        var (conflictStatus, conflict) = await PostAsync(client, Transfer(key, 20m));
        var (retryStatus, retry) = await PostAsync(client, Transfer(key, 10m));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, firstStatus);
        Assert.Equal(HttpStatusCode.Conflict, conflictStatus);
        Assert.Equal("Duplicate idempotency key.", conflict.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, retryStatus);
        Assert.Equal(first.GetProperty("reference").GetString(), retry.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task SameKey_FromTwoClients_GetsTwoSeparateTransactions()
    {
        var key = Guid.NewGuid().ToString();
        using var clientA = fixture.CreateClient(ApiFixture.OnlyClient(LedgerScopes.Transfer));
        using var clientB = fixture.CreateClient(ApiFixture.AllButClient(LedgerScopes.Reverse)); // also has ledger.transfer

        var (_, fromA) = await PostAsync(clientA, Transfer(key, 10m));
        var (_, fromB) = await PostAsync(clientB, Transfer(key, 10m));

        Assert.NotEqual(fromA.GetProperty("reference").GetString(), fromB.GetProperty("reference").GetString());
    }
}
