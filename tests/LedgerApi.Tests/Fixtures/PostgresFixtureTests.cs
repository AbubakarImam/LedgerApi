using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;


namespace LedgerApi.Tests.Fixtures;

public class PostgresFixturetests : IAsyncLifetime
{
	private readonly PostgresFixture _fixture = new();

	public Task InitializeAsync() => _fixture.InitializeAsync();
	public Task DisposeAsync() => _fixture.DisposeAsync();

	[Fact]
	public void Container_StartsSuccessfully()
	{
		Assert.True(true);
	}

	[Fact]
    public async Task Container_HasSchemaApplied()
    {
        await using var context = _fixture.CreateContext();
        var fundingNumbers = await context.Accounts
            .Where(a => a.AccountClass == LedgerApi.Entities.AccountClass.System)
            .Select(a => a.AccountNumber)
            .ToListAsync();

        Assert.Equivalent(new[] { "NGN100000001", "USD100000001", "GBP100000001", "EUR100000001" }, fundingNumbers);
    }

}
