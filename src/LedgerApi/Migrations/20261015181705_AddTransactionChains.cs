using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionChains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "chain_id",
                table: "transactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sequence",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "transaction_chains",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    reference = table.Column<string>(type: "text", nullable: false),
                    initiated_by = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    narration = table.Column<string>(type: "text", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    failed_sequence = table.Column<int>(type: "integer", nullable: true),
                    reverses_chain_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transaction_chains", x => x.id);
                    table.CheckConstraint("ck_transaction_chains_status", "\"status\" IN ('Pending', 'Success', 'Failed')");
                    table.ForeignKey(
                        name: "fk_transaction_chains_transaction_chains_reverses_chain_id",
                        column: x => x.reverses_chain_id,
                        principalTable: "transaction_chains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_chain_id_sequence",
                table: "transactions",
                columns: new[] { "chain_id", "sequence" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_chain_leg",
                table: "transactions",
                sql: "(\"chain_id\" IS NULL AND \"sequence\" IS NULL AND \"purpose\" IS NULL) OR (\"chain_id\" IS NOT NULL AND \"sequence\" >= 1 AND \"purpose\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_purpose",
                table: "transactions",
                sql: "\"purpose\" IN ('Principal', 'Fee', 'Tax')");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_chains_initiated_by_idempotency_key",
                table: "transaction_chains",
                columns: new[] { "initiated_by", "idempotency_key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_transaction_chains_reference",
                table: "transaction_chains",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transaction_chains_reverses_chain_id",
                table: "transaction_chains",
                column: "reverses_chain_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_transactions_transaction_chains_chain_id",
                table: "transactions",
                column: "chain_id",
                principalTable: "transaction_chains",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transactions_transaction_chains_chain_id",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "transaction_chains");

            migrationBuilder.DropIndex(
                name: "ix_transactions_chain_id_sequence",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_chain_leg",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_purpose",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "chain_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "sequence",
                table: "transactions");
        }
    }
}
