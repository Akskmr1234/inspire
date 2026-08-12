using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesDocumentKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One, not zero. Every row that exists when this runs is an invoice, and
            // SalesDocumentKind.Invoice is 1 - the generated default of 0 would give
            // every sale already in the books a kind the enum does not define, and the
            // posting reads the kind to decide which way the goods and the money move.
            migrationBuilder.AddColumn<int>(
                name: "kind",
                table: "sales_invoices",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "returns_invoice_id",
                table: "sales_invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_returns_invoice",
                table: "sales_invoices",
                column: "returns_invoice_id",
                filter: "returns_invoice_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_sales_invoices_sales_invoices_returns_invoice_id",
                table: "sales_invoices",
                column: "returns_invoice_id",
                principalTable: "sales_invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sales_invoices_sales_invoices_returns_invoice_id",
                table: "sales_invoices");

            migrationBuilder.DropIndex(
                name: "ix_sales_invoices_returns_invoice",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "sales_invoices");

            migrationBuilder.DropColumn(
                name: "returns_invoice_id",
                table: "sales_invoices");
        }
    }
}
