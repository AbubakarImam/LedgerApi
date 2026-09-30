using LedgerApi.Authorization;

namespace LedgerApi.Tests.Authorization;

public class ApiKeyOptionsValidatorTests
{
    private static readonly string ValidHash = Convert.ToBase64String(ApiKeyHasher.Hash("lk_test_key"));

    private static ApiClient Client(string clientId = "payment-api", string? keyHash = null, params string[] scopes) => new()
    {
        ClientId = clientId,
        KeyHash = keyHash ?? ValidHash,
        Scopes = scopes.Length > 0 ? [.. scopes] : [LedgerScopes.Transfer],
    };

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(params ApiClient[] clients) =>
        new ApiKeyOptionsValidator().Validate(null, new ApiKeyOptions { Clients = [.. clients] });

    [Fact]
    public void Passes_ForAValidClient()
    {
        Assert.True(Validate(Client()).Succeeded);
    }

    [Fact]
    public void Passes_ForNoClients_SoEveryRequestIsRejectedRatherThanTheAppFailingToStart()
    {
        Assert.True(Validate().Succeeded);
    }

    [Fact]
    public void Fails_WhenClientIdIsMissing()
    {
        var result = Validate(Client(clientId: " "));

        Assert.True(result.Failed);
        Assert.Contains("ClientId is required", result.FailureMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")] // valid base64, but 16 bytes, not a SHA-256
    public void Fails_WhenKeyHashIsNotABase64Sha256(string keyHash)
    {
        var result = Validate(Client(keyHash: keyHash));

        Assert.True(result.Failed);
        Assert.Contains("KeyHash", result.FailureMessage);
    }

    [Fact]
    public void Fails_ForAnUnknownScope()
    {
        var result = Validate(Client(scopes: "ledger.transfers"));

        Assert.True(result.Failed);
        Assert.Contains("unknown scope 'ledger.transfers'", result.FailureMessage);
    }

    [Fact]
    public void Hash_MatchesTheOpensslCommandInTheReadme()
    {
        // The development key and the hash in appsettings.Development.json, produced by
        // printf %s "$KEY" | openssl dgst -sha256 -binary | base64
        var hash = ApiKeyHasher.Hash("lk_dev_payment_wj6C4L8LnpS03vUDi_UV6CZJClsr4SBc");

        Assert.Equal("m421IWnAotwYbQG4E3Yocc7QKP/7c6bvTvpfnMZYIKI=", Convert.ToBase64String(hash));
    }
}
