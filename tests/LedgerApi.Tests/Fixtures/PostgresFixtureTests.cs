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
        var accountCount = await context.Accounts.CountAsync();
        Assert.Equal(0, accountCount);
    }

}
