using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <summary>
    /// Backfills gl_account_id on every pre-existing accounts row (decision #48, owner's explicit call,
    /// D-30 review: existing accounts are backfilled, not left null).
    ///
    /// The three System-account groups are told apart by the exact account numbers their own seed
    /// migrations inserted - not a guessed pattern. Each group's numbers are fixed and already on record
    /// in that migration's own Down() (and in appsettings.json's matching config map), so this reuses the
    /// same explicit lists rather than inferring anything from the account_number string:
    ///   - SeedFundingAccounts + SeedFundingAccountsForSupportedCurrencies (decision #25): the 20
    ///     '<CCY>100000001' rows -> 1000 Cash and Cash Equivalents.
    ///   - SeedFeeAndTaxAccounts's FeeIncome half (decision #46): the 20 '<CCY>200000001' rows -> 4000
    ///     Fee Income.
    ///   - SeedFeeAndTaxAccounts's TaxPayable half (decision #46): the 20 '<CCY>300000001' rows -> 2100
    ///     Tax Payable.
    /// Every remaining account_class = 'Customer' row -> 2000 Customer Deposits (a customer's balance is
    /// the institution's own liability). Any other System account (e.g. one created ad hoc through
    /// POST /api/admin/system-accounts, outside these three seeded groups) is left null on purpose -
    /// not every account is categorized yet, by design (decision #48).
    ///
    /// Any account created after this migration keeps gl_account_id null unless a caller explicitly
    /// supplies one - this backfill is a one-time courtesy for rows that already existed, not an
    /// ongoing default.
    /// </summary>
    public partial class BackfillAccountGlAccountIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE accounts
                SET gl_account_id = (SELECT id FROM gl_accounts WHERE code = '1000')
                WHERE account_number IN (
                    'NGN100000001', 'USD100000001', 'GBP100000001', 'EUR100000001',
                    'CNY100000001', 'GHS100000001', 'SAR100000001', 'QAR100000001', 'AED100000001',
                    'CHF100000001', 'CAD100000001', 'AUD100000001', 'ZAR100000001', 'KES100000001',
                    'EGP100000001', 'MAD100000001', 'INR100000001', 'XOF100000001', 'XAF100000001', 'JPY100000001'
                );

                UPDATE accounts
                SET gl_account_id = (SELECT id FROM gl_accounts WHERE code = '4000')
                WHERE account_number IN (
                    'USD200000001', 'EUR200000001', 'GBP200000001', 'NGN200000001', 'CNY200000001',
                    'GHS200000001', 'SAR200000001', 'QAR200000001', 'AED200000001', 'CHF200000001',
                    'CAD200000001', 'AUD200000001', 'ZAR200000001', 'KES200000001', 'EGP200000001',
                    'MAD200000001', 'INR200000001', 'XOF200000001', 'XAF200000001', 'JPY200000001'
                );

                UPDATE accounts
                SET gl_account_id = (SELECT id FROM gl_accounts WHERE code = '2100')
                WHERE account_number IN (
                    'USD300000001', 'EUR300000001', 'GBP300000001', 'NGN300000001', 'CNY300000001',
                    'GHS300000001', 'SAR300000001', 'QAR300000001', 'AED300000001', 'CHF300000001',
                    'CAD300000001', 'AUD300000001', 'ZAR300000001', 'KES300000001', 'EGP300000001',
                    'MAD300000001', 'INR300000001', 'XOF300000001', 'XAF300000001', 'JPY300000001'
                );

                UPDATE accounts
                SET gl_account_id = (SELECT id FROM gl_accounts WHERE code = '2000')
                WHERE account_class = 'Customer' AND gl_account_id IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverses exactly what Up set: before this migration ran, no account had a gl_account_id yet.
            migrationBuilder.Sql("""
                UPDATE accounts SET gl_account_id = NULL
                WHERE account_class = 'Customer'
                   OR account_number IN (
                        'NGN100000001', 'USD100000001', 'GBP100000001', 'EUR100000001',
                        'CNY100000001', 'GHS100000001', 'SAR100000001', 'QAR100000001', 'AED100000001',
                        'CHF100000001', 'CAD100000001', 'AUD100000001', 'ZAR100000001', 'KES100000001',
                        'EGP100000001', 'MAD100000001', 'INR100000001', 'XOF100000001', 'XAF100000001', 'JPY100000001',
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
