using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyToAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "initiated_by",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_hash",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_initiated_by_idempotency_key",
                table: "accounts",
                columns: new[] { "initiated_by", "idempotency_key" },
                unique: true,
                filter: "\"idempotency_key\" IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_initiated_by_idempotency_key",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "initiated_by",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "request_hash",
                table: "accounts");
        }
    }
}
