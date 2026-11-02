using LedgerApi.Data;
using LedgerApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LedgerApi.Tests.Data;

// Runs the real SeedGlAccounts + BackfillAccountGlAccountIds migrations (decision #48) over
// pre-existing data that mimics every account the real seed migrations produce, on its own database:
// migrate to just after the schema change (AddGlAccounts), insert the data, then apply the rest and
// check the right counts land in the right GL buckets.
[Collection("Postgres")]
public class GlAccountsMigrationTests(PostgresFixture fixture)
{
    private const string MigrationBeforeSeedAndBackfill = "AddGlAccounts";

    private static async Task<IMigrator> MigrateToBeforeSeedAndBackfillAsync(LedgerDbContext ctx)
    {
        await ctx.Database.EnsureDeletedAsync();
        var migrator = ctx.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeSeedAndBackfill);
        return migrator;
    }

    [Fact]
    public async Task Backfill_SortsExistingAccounts_IntoTheRightGlBuckets()
    {
        await using var ctx = fixture.CreateContext(database: "gl_accounts_backfill");
        var migrator = await MigrateToBeforeSeedAndBackfillAsync(ctx);

        // Migrating to AddGlAccounts already ran every earlier seed migration, so the real funding/
        // fee-income/tax-payable accounts (decisions #25/#46) already exist with their real account
        // numbers - no need (and no room, the account number is unique) to insert them again. We add
        // only what those migrations don't: a plain customer account and an unrelated ad-hoc system
        // account that must be left uncategorized (decision #48: not every account is categorized yet).
        await ctx.Database.ExecuteSqlRawAsync("""
            INSERT INTO accounts (account_number, account_name, account_type, account_class, currency_code, status, created_at)
            VALUES
                ('5551234567', 'A Customer', 'Savings', 'Customer', 'NGN', 'Active', now()),
                ('5559876543', 'Another Customer', 'Wallet', 'Customer', 'USD', 'Active', now()),
                ('NGN999999999', 'Some Other System Account', 'Wallet', 'System', 'NGN', 'Active', now());
            """);

        await migrator.MigrateAsync();

        async Task<string?> GlCodeFor(string accountNumber) => await ctx.Database.SqlQuery<string?>($"""
            SELECT g.code AS "Value"
            FROM accounts a LEFT JOIN gl_accounts g ON g.id = a.gl_account_id
            WHERE a.account_number = {accountNumber}
            """).SingleAsync();

        Assert.Equal("1000", await GlCodeFor("NGN100000001"));
        Assert.Equal("1000", await GlCodeFor("USD100000001"));
        Assert.Equal("4000", await GlCodeFor("NGN200000001"));
        Assert.Equal("2100", await GlCodeFor("NGN300000001"));
        Assert.Equal("2000", await GlCodeFor("5551234567"));
        Assert.Equal("2000", await GlCodeFor("5559876543"));
        Assert.Null(await GlCodeFor("NGN999999999"));

        // Five flat top-level GL accounts, exactly as decision #48 specifies.
        var glAccounts = await ctx.Database.SqlQuery<string>($"""
            SELECT code AS "Value" FROM gl_accounts ORDER BY code
            """).ToListAsync();
        Assert.Equal(["1000", "2000", "2100", "4000", "5000"], glAccounts);

        // Show the work: the counts per bucket, not just one example account each. 20 currencies are
        // seeded for funding (decision #25) and 20 each for fee-income/tax-payable (decision #46); the
        // two customer rows this test added land on 2000; the one ad-hoc system account stays null.
        async Task<int> CountFor(string code) => await ctx.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS "Value" FROM accounts a
            JOIN gl_accounts g ON g.id = a.gl_account_id
            WHERE g.code = {code}
            """).SingleAsync();

        Assert.Equal(20, await CountFor("1000"));
        Assert.Equal(20, await CountFor("4000"));
        Assert.Equal(20, await CountFor("2100"));
        Assert.Equal(2, await CountFor("2000"));

        var uncategorizedSystemAccounts = await ctx.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS "Value" FROM accounts WHERE gl_account_id IS NULL AND account_class = 'System'
            """).SingleAsync();
        Assert.Equal(1, uncategorizedSystemAccounts);
    }
}
