using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LedgerApi.Authorization;
using LedgerApi.Contracts.Responses;
using LedgerApi.Middleware;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace LedgerApi.Tests.Authorization;

[Collection("Api")]
public class AuthorizationTests(ApiFixture fixture)
{
    // Every endpoint with the one scope it requires. Bodies are valid enough to reach the controller.
    public static TheoryData<string, string, string?, string> Endpoints => new()
    {
        { "GET", "/api/accounts/1000000001", null, LedgerScopes.Read },
        { "GET", "/api/accounts/1000000001/balance", null, LedgerScopes.Read },
        { "POST", "/api/accounts", """{"accountName":"Auth Test","accountType":"Savings","currencyCode":"NGN"}""", LedgerScopes.Accounts },
        { "POST", "/api/transfers", $$"""{"debitAccountNumber":"1000000001","creditAccountNumber":"1000000002","amount":10,"idempotencyKey":"{{Guid.NewGuid()}}"}""", LedgerScopes.Transfer },
        { "POST", "/api/deposits", $$"""{"customerAccountNumber":"1000000001","amount":10,"idempotencyKey":"{{Guid.NewGuid()}}"}""", LedgerScopes.Funding },
        { "POST", "/api/withdrawals", $$"""{"customerAccountNumber":"1000000001","amount":10,"idempotencyKey":"{{Guid.NewGuid()}}"}""", LedgerScopes.Funding },
        { "POST", "/api/reversals", """{"originalTransactionReference":"TXN-DOESNOTEXIST"}""", LedgerScopes.Reverse },
        { "POST", "/api/admin/system-accounts", """{"accountName":"Auth Test System","accountType":"Wallet","currencyCode":"NGN"}""", LedgerScopes.Admin },
    };

    private static HttpRequestMessage Request(string method, string path, string? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        return request;
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Returns401_WithProblemDetails_WhenNoApiKeyIsSent(string method, string path, string? body, string scope)
    {
        _ = scope;
        using var client = fixture.CreateClient(clientId: null);

        using var response = await client.SendAsync(Request(method, path, body));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiKeyAuthenticationHandler.SchemeName, response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.Equal(correlationId, problem.RootElement.GetProperty("correlationId").GetString());
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Returns403_WhenTheKeyLacksTheRequiredScope(string method, string path, string? body, string scope)
    {
        using var client = fixture.CreateClient(ApiFixture.AllButClient(scope));

        using var response = await client.SendAsync(Request(method, path, body));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ReachesTheEndpoint_WhenTheKeyHoldsOnlyTheRequiredScope(string method, string path, string? body, string scope)
    {
        using var client = fixture.CreateClient(ApiFixture.OnlyClient(scope));

        using var response = await client.SendAsync(Request(method, path, body));

        // Past authorization the controller answers normally (200, 404 or 422 depending on the data).
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns401_ForAnUnknownApiKey()
    {
        using var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, "lk_not_a_real_key");

        using var response = await client.GetAsync("/api/accounts/1000000001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuditRow_RecordsTheCallingClientAsActor()
    {
        var clientId = ApiFixture.OnlyClient(LedgerScopes.Accounts);
        using var client = fixture.CreateClient(clientId);

        using var response = await client.PostAsJsonAsync("/api/accounts",
            new { accountName = "Actor Test", accountType = "Savings", currencyCode = "NGN" });
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>();

        await using var ctx = fixture.CreateContext();
        var audit = await ctx.AuditLogs.SingleAsync(a => a.EntityId == account!.AccountNumber);
        Assert.Equal(clientId, audit.ActorId);
    }
}
