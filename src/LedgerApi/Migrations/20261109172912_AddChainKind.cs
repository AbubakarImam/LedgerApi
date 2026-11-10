using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddChainKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_purpose",
                table: "transactions");

            migrationBuilder.AddColumn<string>(
                name: "chain_kind",
                table: "transaction_chains",
                type: "text",
                nullable: false,
                defaultValue: "Payment");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_purpose",
                table: "transactions",
                sql: "\"purpose\" IN ('Principal', 'Fee', 'Tax', 'Disbursement', 'Collection')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transaction_chains_chain_kind",
                table: "transaction_chains",
                sql: "\"chain_kind\" IN ('Payment', 'Disbursement', 'Collection')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_purpose",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transaction_chains_chain_kind",
                table: "transaction_chains");

            migrationBuilder.DropColumn(
                name: "chain_kind",
                table: "transaction_chains");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_purpose",
                table: "transactions",
                sql: "\"purpose\" IN ('Principal', 'Fee', 'Tax')");
        }
    }
}
