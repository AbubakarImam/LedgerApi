using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddEntryTypeAndTransactionStatusChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_status",
                table: "transactions",
                sql: "\"status\" IN ('Pending', 'Success', 'Failed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_entry_type",
                table: "ledger_entries",
                sql: "\"entry_type\" IN ('Debit', 'Credit')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_status",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_entry_type",
                table: "ledger_entries");
        }
    }
}
