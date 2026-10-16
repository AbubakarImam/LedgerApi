using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <summary>
    /// Seeds a fee-income and a tax-payable system account for every currency in
    /// SupportedCurrencies (decision #46). Unlike the funding accounts (decisions #25/#37,
    /// seeded in two migrations only because the currency list grew over time), the currency
    /// list is already complete, so both account types for all 20 currencies are seeded here
    /// in one migration.
    ///
    /// These account numbers must match the FeeIncome:Accounts and TaxPayable:Accounts maps
    /// in appsettings.json.
    /// </summary>
    public partial class SeedFeeAndTaxAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO accounts (account_number, account_name, account_type, account_class, currency_code, status, created_at)
                VALUES
                    ('USD200000001', 'USD Fee Income Account', 'Wallet', 'System', 'USD', 'Active', now()),
                    ('EUR200000001', 'EUR Fee Income Account', 'Wallet', 'System', 'EUR', 'Active', now()),
                    ('GBP200000001', 'GBP Fee Income Account', 'Wallet', 'System', 'GBP', 'Active', now()),
                    ('NGN200000001', 'NGN Fee Income Account', 'Wallet', 'System', 'NGN', 'Active', now()),
                    ('CNY200000001', 'CNY Fee Income Account', 'Wallet', 'System', 'CNY', 'Active', now()),
                    ('GHS200000001', 'GHS Fee Income Account', 'Wallet', 'System', 'GHS', 'Active', now()),
                    ('SAR200000001', 'SAR Fee Income Account', 'Wallet', 'System', 'SAR', 'Active', now()),
                    ('QAR200000001', 'QAR Fee Income Account', 'Wallet', 'System', 'QAR', 'Active', now()),
                    ('AED200000001', 'AED Fee Income Account', 'Wallet', 'System', 'AED', 'Active', now()),
                    ('CHF200000001', 'CHF Fee Income Account', 'Wallet', 'System', 'CHF', 'Active', now()),
                    ('CAD200000001', 'CAD Fee Income Account', 'Wallet', 'System', 'CAD', 'Active', now()),
                    ('AUD200000001', 'AUD Fee Income Account', 'Wallet', 'System', 'AUD', 'Active', now()),
                    ('ZAR200000001', 'ZAR Fee Income Account', 'Wallet', 'System', 'ZAR', 'Active', now()),
                    ('KES200000001', 'KES Fee Income Account', 'Wallet', 'System', 'KES', 'Active', now()),
                    ('EGP200000001', 'EGP Fee Income Account', 'Wallet', 'System', 'EGP', 'Active', now()),
                    ('MAD200000001', 'MAD Fee Income Account', 'Wallet', 'System', 'MAD', 'Active', now()),
                    ('INR200000001', 'INR Fee Income Account', 'Wallet', 'System', 'INR', 'Active', now()),
                    ('XOF200000001', 'XOF Fee Income Account', 'Wallet', 'System', 'XOF', 'Active', now()),
                    ('XAF200000001', 'XAF Fee Income Account', 'Wallet', 'System', 'XAF', 'Active', now()),
                    ('JPY200000001', 'JPY Fee Income Account', 'Wallet', 'System', 'JPY', 'Active', now()),

                    ('USD300000001', 'USD Tax Payable Account', 'Wallet', 'System', 'USD', 'Active', now()),
                    ('EUR300000001', 'EUR Tax Payable Account', 'Wallet', 'System', 'EUR', 'Active', now()),
                    ('GBP300000001', 'GBP Tax Payable Account', 'Wallet', 'System', 'GBP', 'Active', now()),
                    ('NGN300000001', 'NGN Tax Payable Account', 'Wallet', 'System', 'NGN', 'Active', now()),
                    ('CNY300000001', 'CNY Tax Payable Account', 'Wallet', 'System', 'CNY', 'Active', now()),
                    ('GHS300000001', 'GHS Tax Payable Account', 'Wallet', 'System', 'GHS', 'Active', now()),
                    ('SAR300000001', 'SAR Tax Payable Account', 'Wallet', 'System', 'SAR', 'Active', now()),
                    ('QAR300000001', 'QAR Tax Payable Account', 'Wallet', 'System', 'QAR', 'Active', now()),
                    ('AED300000001', 'AED Tax Payable Account', 'Wallet', 'System', 'AED', 'Active', now()),
                    ('CHF300000001', 'CHF Tax Payable Account', 'Wallet', 'System', 'CHF', 'Active', now()),
                    ('CAD300000001', 'CAD Tax Payable Account', 'Wallet', 'System', 'CAD', 'Active', now()),
                    ('AUD300000001', 'AUD Tax Payable Account', 'Wallet', 'System', 'AUD', 'Active', now()),
                    ('ZAR300000001', 'ZAR Tax Payable Account', 'Wallet', 'System', 'ZAR', 'Active', now()),
                    ('KES300000001', 'KES Tax Payable Account', 'Wallet', 'System', 'KES', 'Active', now()),
                    ('EGP300000001', 'EGP Tax Payable Account', 'Wallet', 'System', 'EGP', 'Active', now()),
                    ('MAD300000001', 'MAD Tax Payable Account', 'Wallet', 'System', 'MAD', 'Active', now()),
                    ('INR300000001', 'INR Tax Payable Account', 'Wallet', 'System', 'INR', 'Active', now()),
                    ('XOF300000001', 'XOF Tax Payable Account', 'Wallet', 'System', 'XOF', 'Active', now()),
                    ('XAF300000001', 'XAF Tax Payable Account', 'Wallet', 'System', 'XAF', 'Active', now()),
                    ('JPY300000001', 'JPY Tax Payable Account', 'Wallet', 'System', 'JPY', 'Active', now())
                ON CONFLICT (account_number) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM accounts
                WHERE account_number IN (
                    'USD200000001', 'EUR200000001', 'GBP200000001', 'NGN200000001', 'CNY200000001',
                    'GHS200000001', 'SAR200000001', 'QAR200000001', 'AED200000001', 'CHF200000001',
                    'CAD200000001', 'AUD200000001', 'ZAR200000001', 'KES200000001', 'EGP200000001',
                    'MAD200000001', 'INR200000001', 'XOF200000001', 'XAF200000001', 'JPY200000001',
                    'USD300000001', 'EUR300000001', 'GBP300000001', 'NGN300000001', 'CNY300000001',
                    'GHS300000001', 'SAR300000001', 'QAR300000001', 'AED300000001', 'CHF300000001',
                    'CAD300000001', 'AUD300000001', 'ZAR300000001', 'KES300000001', 'EGP300000001',
                    'MAD300000001', 'INR300000001', 'XOF300000001', 'XAF300000001', 'JPY300000001'
                );
                """);
        }
    }
}
