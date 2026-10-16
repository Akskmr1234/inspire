using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CancelledPurchasesReleaseSupplierNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_purchase_invoices_supplier_number",
                table: "purchase_invoices");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_supplier_number",
                table: "purchase_invoices",
                columns: new[] { "firm_id", "supplier_ledger_id", "supplier_invoice_number" },
                unique: true,
                filter: "supplier_invoice_number IS NOT NULL AND is_deleted = false AND status <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_purchase_invoices_supplier_number",
                table: "purchase_invoices");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_supplier_number",
                table: "purchase_invoices",
                columns: new[] { "firm_id", "supplier_ledger_id", "supplier_invoice_number" },
                unique: true,
                filter: "supplier_invoice_number IS NOT NULL AND is_deleted = false");
        }
    }
}
