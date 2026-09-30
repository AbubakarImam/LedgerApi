using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LedgerApi.Authorization;
using LedgerApi.Middleware;
using LedgerApi.Tests.Fixtures;

namespace LedgerApi.Tests.Middleware;

[Collection("Api")]
public class ExceptionHandlingMiddlewareTests(ApiFixture fixture)
{
    [Fact]
    public async Task MappedException_ReturnsProblemJson_WithDetailAndCorrelationId()
    {
        using var client = fixture.CreateClient(ApiFixture.OnlyClient(LedgerScopes.Reverse));

        // An unknown reference makes ReversalService throw TransactionNotFoundException, mapped to 404.
        using var response = await client.PostAsJsonAsync("/api/reversals",
            new { originalTransactionReference = "TXN-DOESNOTEXIST" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Transaction not found.", problem.RootElement.GetProperty("title").GetString());
        Assert.Contains("TXN-DOESNOTEXIST", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single(),
            problem.RootElement.GetProperty("correlationId").GetString());
    }
}
