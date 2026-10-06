using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <inheritdoc />
    public partial class ScopeIdempotencyKeysToClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transactions_idempotency_key",
                table: "transactions");

            migrationBuilder.AddColumn<string>(
                name: "request_hash",
                table: "transactions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_initiated_by_idempotency_key",
                table: "transactions",
                columns: new[] { "initiated_by", "idempotency_key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transactions_initiated_by_idempotency_key",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "request_hash",
                table: "transactions");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_idempotency_key",
                table: "transactions",
                column: "idempotency_key",
                unique: true);
        }
    }
}
