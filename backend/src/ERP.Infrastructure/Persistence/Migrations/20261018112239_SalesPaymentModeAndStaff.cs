using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Records how a sale was paid and which employees made and billed it.
    /// </summary>
    /// <remarks>
    /// Every invoice entered before this was on account, so the payment mode is filled in
    /// as credit (1) for them.
    /// </remarks>
    public partial class SalesPaymentModeAndStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "billing_man_ledger_id",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_mode",
                table: "sales_invoices",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "salesman_ledger_id",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_billing_man_ledger_id",
                table: "sales_invoices",
                column: "billing_man_ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_salesman_ledger_id",
                table: "sales_invoices",
                column: "salesman_ledger_id");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoices_ledgers_billing_man_ledger_id",
                table: "sales_invoices",
                column: "billing_man_ledger_id",
                principalTable: "ledgers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoices_ledgers_salesman_ledger_id",
                table: "sales_invoices",
                column: "salesman_ledger_id",
                principalTable: "ledgers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoices_ledgers_billing_man_ledger_id",
                table: "sales_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoices_ledgers_salesman_ledger_id",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoices_billing_man_ledger_id",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoices_salesman_ledger_id",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "billing_man_ledger_id",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "payment_mode",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "salesman_ledger_id",
                table: "sales_invoices");
        }
    }
}
