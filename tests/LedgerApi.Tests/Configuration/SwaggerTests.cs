using System.Net;
using System.Text.Json;
using LedgerApi.Authorization;
using LedgerApi.Tests.Fixtures;

namespace LedgerApi.Tests.Configuration;

// The fixture runs in the Testing environment with Swagger:Enabled=true, like the showcase on Azure.
[Collection("Api")]
public class SwaggerTests(ApiFixture fixture)
{
    [Fact]
    public async Task SwaggerUi_IsServed_WithoutAnApiKey()
    {
        using var client = fixture.CreateClient(clientId: null);

        using var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_DescribesTheApiKeyScheme_AndEveryEndpoint()
    {
        using var client = fixture.CreateClient(clientId: null);

        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));

        var scheme = document.RootElement.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty(ApiKeyAuthenticationHandler.SchemeName);
        Assert.Equal(ApiKeyAuthenticationHandler.HeaderName, scheme.GetProperty("name").GetString());

        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/transfers", paths);
        Assert.Contains("/api/reversals", paths);
        Assert.Contains("/api/admin/system-accounts", paths);
    }

    [Fact]
    public async Task CallingTheApi_StillNeedsAKey_WhenSwaggerIsPublic()
    {
        using var client = fixture.CreateClient(clientId: null);

        using var response = await client.GetAsync("/api/accounts/NGN100000001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
