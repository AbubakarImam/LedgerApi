using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedgerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionPayloadFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "branch_code",
                table: "transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "channel",
                table: "transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_id",
                table: "transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "value_date",
                table: "transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "branch_code",
                table: "transaction_chains",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "channel",
                table: "transaction_chains",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_id",
                table: "transaction_chains",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "value_date",
                table: "transaction_chains",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "branch_code",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "decision_id",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "value_date",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "branch_code",
                table: "transaction_chains");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "transaction_chains");

            migrationBuilder.DropColumn(
                name: "decision_id",
                table: "transaction_chains");

            migrationBuilder.DropColumn(
                name: "value_date",
                table: "transaction_chains");
        }
    }
}
