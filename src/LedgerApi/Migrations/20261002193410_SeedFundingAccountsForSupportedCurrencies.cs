using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <summary>
    /// Seeds a funding system account for every currency added in decision #37
    /// (NGN, USD, GBP and EUR were seeded by SeedFundingAccounts).
    /// </summary>
    public partial class SeedFundingAccountsForSupportedCurrencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO accounts (account_number, account_name, account_type, account_class, currency_code, status, created_at)
                VALUES
                    ('CNY100000001', 'CNY Funding Account', 'Wallet', 'System', 'CNY', 'Active', now()),
                    ('GHS100000001', 'GHS Funding Account', 'Wallet', 'System', 'GHS', 'Active', now()),
                    ('SAR100000001', 'SAR Funding Account', 'Wallet', 'System', 'SAR', 'Active', now()),
                    ('QAR100000001', 'QAR Funding Account', 'Wallet', 'System', 'QAR', 'Active', now()),
                    ('AED100000001', 'AED Funding Account', 'Wallet', 'System', 'AED', 'Active', now()),
                    ('CHF100000001', 'CHF Funding Account', 'Wallet', 'System', 'CHF', 'Active', now()),
                    ('CAD100000001', 'CAD Funding Account', 'Wallet', 'System', 'CAD', 'Active', now()),
                    ('AUD100000001', 'AUD Funding Account', 'Wallet', 'System', 'AUD', 'Active', now()),
                    ('ZAR100000001', 'ZAR Funding Account', 'Wallet', 'System', 'ZAR', 'Active', now()),
                    ('KES100000001', 'KES Funding Account', 'Wallet', 'System', 'KES', 'Active', now()),
                    ('EGP100000001', 'EGP Funding Account', 'Wallet', 'System', 'EGP', 'Active', now()),
                    ('MAD100000001', 'MAD Funding Account', 'Wallet', 'System', 'MAD', 'Active', now()),
                    ('INR100000001', 'INR Funding Account', 'Wallet', 'System', 'INR', 'Active', now()),
                    ('XOF100000001', 'XOF Funding Account', 'Wallet', 'System', 'XOF', 'Active', now()),
                    ('XAF100000001', 'XAF Funding Account', 'Wallet', 'System', 'XAF', 'Active', now()),
                    ('JPY100000001', 'JPY Funding Account', 'Wallet', 'System', 'JPY', 'Active', now())
                ON CONFLICT (account_number) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM accounts
                WHERE account_number IN ('CNY100000001', 'GHS100000001', 'SAR100000001', 'QAR100000001', 'AED100000001', 'CHF100000001', 'CAD100000001', 'AUD100000001', 'ZAR100000001', 'KES100000001', 'EGP100000001', 'MAD100000001', 'INR100000001', 'XOF100000001', 'XAF100000001', 'JPY100000001');
                """);
        }
    }
}
