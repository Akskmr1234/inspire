using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesCounterPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "amount_received",
                table: "sales_invoices",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "payment_reference",
                table: "sales_invoices",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "receipt_voucher_id",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "received_into_ledger_id",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_receipt_voucher_id",
                table: "sales_invoices",
                column: "receipt_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_received_into_ledger_id",
                table: "sales_invoices",
                column: "received_into_ledger_id");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoices_ledgers_received_into_ledger_id",
                table: "sales_invoices",
                column: "received_into_ledger_id",
                principalTable: "ledgers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoices_vouchers_receipt_voucher_id",
                table: "sales_invoices",
                column: "receipt_voucher_id",
                principalTable: "vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoices_ledgers_received_into_ledger_id",
                table: "sales_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoices_vouchers_receipt_voucher_id",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoices_receipt_voucher_id",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoices_received_into_ledger_id",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "amount_received",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "payment_reference",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "receipt_voucher_id",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "received_into_ledger_id",
                table: "sales_invoices");
        }
    }
}
