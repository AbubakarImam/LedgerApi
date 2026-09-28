using LedgerApi.Middleware;
using Microsoft.AspNetCore.Http;

namespace LedgerApi.Tests.Middleware;

public class CorrelationIdMiddlewareTests
{
    // Runs the middleware with a "next" step that records the id the rest of the pipeline would see.
    private static async Task<(HttpContext Context, string? SeenByNext)> RunAsync(string? incomingId)
    {
        var context = new DefaultHttpContext();
        if (incomingId is not null)
        {
            context.Request.Headers[CorrelationIdMiddleware.HeaderName] = incomingId;
        }

        string? seenByNext = null;
        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            seenByNext = ctx.TraceIdentifier;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        return (context, seenByNext);
    }

    [Fact]
    public async Task UsesIncomingId_WhenItIsValid()
    {
        var (context, seenByNext) = await RunAsync("client-req_42.a");

        Assert.Equal("client-req_42.a", seenByNext);
        Assert.Equal("client-req_42.a", context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
    }

    [Fact]
    public async Task GeneratesId_WhenHeaderIsMissing()
    {
        var (context, seenByNext) = await RunAsync(null);

        Assert.NotNull(seenByNext);
        Assert.Equal(32, seenByNext!.Length);
        Assert.Equal(seenByNext, context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
    }

    [Theory]
    [InlineData("has spaces in it")]
    [InlineData("line\nbreak")]
    [InlineData("<script>")]
    public async Task ReplacesIncomingId_WhenItContainsUnsafeCharacters(string incomingId)
    {
        var (_, seenByNext) = await RunAsync(incomingId);

        Assert.NotEqual(incomingId, seenByNext);
        Assert.Equal(32, seenByNext!.Length);
    }

    [Fact]
    public async Task ReplacesIncomingId_WhenItIsTooLong()
    {
        var (_, seenByNext) = await RunAsync(new string('a', 65));

        Assert.Equal(32, seenByNext!.Length);
    }
}
