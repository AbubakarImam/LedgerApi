using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "amount",
                table: "audit_logs",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "audit_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "credit_account_number",
                table: "audit_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "audit_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "debit_account_number",
                table: "audit_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "endpoint",
                table: "audit_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "audit_logs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_correlation_id",
                table: "audit_logs",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_id",
                table: "audit_logs",
                column: "entity_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_logs_correlation_id",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_entity_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "amount",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "credit_account_number",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "debit_account_number",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "endpoint",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "status",
                table: "audit_logs");
        }
    }
}
