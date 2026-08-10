using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesInvoicePostingLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bill_id",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "journal_voucher_id",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "stock_document_id",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_bill_id",
                table: "sales_invoices",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_journal_voucher_id",
                table: "sales_invoices",
                column: "journal_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_stock_document_id",
                table: "sales_invoices",
                column: "stock_document_id");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoices_bills_bill_id",
                table: "sales_invoices",
                column: "bill_id",
                principalTable: "bills",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoices_stock_documents_stock_document_id",
                table: "sales_invoices",
                column: "stock_document_id",
                principalTable: "stock_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoices_vouchers_journal_voucher_id",
                table: "sales_invoices",
                column: "journal_voucher_id",
                principalTable: "vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoices_bills_bill_id",
                table: "sales_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoices_stock_documents_stock_document_id",
                table: "sales_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoices_vouchers_journal_voucher_id",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoices_bill_id",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoices_journal_voucher_id",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoices_stock_document_id",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "bill_id",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "journal_voucher_id",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "stock_document_id",
                table: "sales_invoices");
        }
    }
}
