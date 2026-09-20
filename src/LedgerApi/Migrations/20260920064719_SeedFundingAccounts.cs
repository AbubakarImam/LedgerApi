using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <inheritdoc />
    public partial class SeedFundingAccounts : Migration
    {
        // These account numbers must match the Funding:Accounts map in appsettings.json.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO accounts (account_number, account_name, account_type, account_class, currency_code, status, created_at)
                VALUES
                    ('NGN100000001', 'NGN Funding Account', 'Wallet', 'System', 'NGN', 'Active', now()),
                    ('USD100000001', 'USD Funding Account', 'Wallet', 'System', 'USD', 'Active', now()),
                    ('GBP100000001', 'GBP Funding Account', 'Wallet', 'System', 'GBP', 'Active', now()),
                    ('EUR100000001', 'EUR Funding Account', 'Wallet', 'System', 'EUR', 'Active', now())
                ON CONFLICT (account_number) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM accounts
                WHERE account_number IN ('NGN100000001', 'USD100000001', 'GBP100000001', 'EUR100000001');
                """);
        }
    }
}
