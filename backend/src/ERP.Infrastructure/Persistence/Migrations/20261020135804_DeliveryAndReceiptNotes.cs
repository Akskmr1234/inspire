using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryAndReceiptNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "party_ledger_id",
                table: "stock_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "delivery_note_id",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "receipt_note_id",
                table: "purchase_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_party",
                table: "stock_documents",
                columns: new[] { "firm_id", "party_ledger_id" },
                filter: "party_ledger_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_documents_party_ledger_id",
                table: "stock_documents",
                column: "party_ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_delivery_note",
                table: "sales_invoices",
                column: "delivery_note_id",
                filter: "delivery_note_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_receipt_note",
                table: "purchase_invoices",
                column: "receipt_note_id",
                filter: "receipt_note_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_invoices_stock_documents_receipt_note_id",
                table: "purchase_invoices",
                column: "receipt_note_id",
                principalTable: "stock_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoices_stock_documents_delivery_note_id",
                table: "sales_invoices",
                column: "delivery_note_id",
                principalTable: "stock_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_documents_ledgers_party_ledger_id",
                table: "stock_documents",
                column: "party_ledger_id",
                principalTable: "ledgers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_purchase_invoices_stock_documents_receipt_note_id",
                table: "purchase_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoices_stock_documents_delivery_note_id",
                table: "sales_invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_documents_ledgers_party_ledger_id",
                table: "stock_documents");

            migrationBuilder.DropIndex(
                name: "ix_stock_documents_party",
                table: "stock_documents");

            migrationBuilder.DropIndex(
                name: "ix_stock_documents_party_ledger_id",
                table: "stock_documents");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoices_delivery_note",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "ix_purchase_invoices_receipt_note",
                table: "purchase_invoices");

            migrationBuilder.DropColumn(
                name: "party_ledger_id",
                table: "stock_documents");

            migrationBuilder.DropColumn(
                name: "delivery_note_id",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "receipt_note_id",
                table: "purchase_invoices");
        }
    }
}
