using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddGlAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "gl_account_id",
                table: "accounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gl_accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    account_type = table.Column<string>(type: "text", nullable: false),
                    parent_gl_account_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gl_accounts", x => x.id);
                    table.CheckConstraint("ck_gl_accounts_account_type", "\"account_type\" IN ('Asset', 'Liability', 'Equity', 'Income', 'Expense')");
                    table.ForeignKey(
                        name: "fk_gl_accounts_gl_accounts_parent_gl_account_id",
                        column: x => x.parent_gl_account_id,
                        principalTable: "gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_gl_account_id",
                table: "accounts",
                column: "gl_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_gl_accounts_code",
                table: "gl_accounts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gl_accounts_parent_gl_account_id",
                table: "gl_accounts",
                column: "parent_gl_account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_accounts_gl_accounts_gl_account_id",
                table: "accounts",
                column: "gl_account_id",
                principalTable: "gl_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_accounts_gl_accounts_gl_account_id",
                table: "accounts");

            migrationBuilder.DropTable(
                name: "gl_accounts");

            migrationBuilder.DropIndex(
                name: "ix_accounts_gl_account_id",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "gl_account_id",
                table: "accounts");
        }
    }
}
