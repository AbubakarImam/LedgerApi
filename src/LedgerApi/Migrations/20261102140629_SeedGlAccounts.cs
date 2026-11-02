using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <summary>
    /// Seeds the five top-level GL accounts for v1 (decision #48): a real, queryable chart of accounts,
    /// flat for now (parent_gl_account_id left null on all five - no hierarchy populated yet, the
    /// mechanism just needs to exist). Equity and Expense are seeded for a complete five-category chart
    /// even though nothing maps to either today.
    /// </summary>
    public partial class SeedGlAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO gl_accounts (code, name, account_type, parent_gl_account_id, created_at)
                VALUES
                    ('1000', 'Cash and Cash Equivalents', 'Asset', NULL, now()),
                    ('2000', 'Customer Deposits', 'Liability', NULL, now()),
                    ('2100', 'Tax Payable', 'Liability', NULL, now()),
                    ('4000', 'Fee Income', 'Income', NULL, now()),
                    ('5000', 'Expense', 'Expense', NULL, now())
                ON CONFLICT (code) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM gl_accounts WHERE code IN ('1000', '2000', '2100', '4000', '5000');
                """);
        }
    }
}
