using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountClassAndStatusLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "account_class",
                table: "accounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_account_class",
                table: "accounts",
                sql: "\"account_class\" IN ('Customer', 'System')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_accounts_status",
                table: "accounts",
                sql: "\"status\" IN ('Active', 'Frozen', 'Blocked')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_account_class",
                table: "accounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_accounts_status",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "account_class",
                table: "accounts");
        }
    }
}
