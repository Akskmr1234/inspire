using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    returns_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    supplier_ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    supplier_invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    supplier_invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    narration = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    posted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    stock_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bill_id = table.Column<Guid>(type: "uuid", nullable: true),
                    journal_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_bills_bill_id",
                        column: x => x.bill_id,
                        principalTable: "bills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_financial_years_financial_year_id",
                        column: x => x.financial_year_id,
                        principalTable: "financial_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_ledgers_supplier_ledger_id",
                        column: x => x.supplier_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_purchase_invoices_returns_invoice_id",
                        column: x => x.returns_invoice_id,
                        principalTable: "purchase_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_stock_documents_stock_document_id",
                        column: x => x.stock_document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_vouchers_journal_voucher_id",
                        column: x => x.journal_voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoice_charges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_addition = table.Column<bool>(type: "boolean", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_invoice_charges", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_charges_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_charges_purchase_invoices_purchase_invoice~",
                        column: x => x.purchase_invoice_id,
                        principalTable: "purchase_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    stock_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    taxable_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_invoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_lines_purchase_invoices_purchase_invoice_id",
                        column: x => x.purchase_invoice_id,
                        principalTable: "purchase_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_lines_units_of_measure_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoice_line_serials",
                columns: table => new
                {
                    purchase_invoice_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_invoice_line_serials", x => new { x.purchase_invoice_line_id, x.serial_number });
                    table.ForeignKey(
                        name: "fk_purchase_invoice_line_serials_purchase_invoice_lines_purcha~",
                        column: x => x.purchase_invoice_line_id,
                        principalTable: "purchase_invoice_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoice_line_taxes",
                columns: table => new
                {
                    purchase_invoice_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    percentage = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_invoice_line_taxes", x => new { x.purchase_invoice_line_id, x.type });
                    table.ForeignKey(
                        name: "fk_purchase_invoice_line_taxes_purchase_invoice_lines_purchase~",
                        column: x => x.purchase_invoice_line_id,
                        principalTable: "purchase_invoice_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_charges_ledger",
                table: "purchase_invoice_charges",
                columns: new[] { "purchase_invoice_id", "ledger_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_charges_ledger_id",
                table: "purchase_invoice_charges",
                column: "ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_charges_tenant",
                table: "purchase_invoice_charges",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_line_serials_serial",
                table: "purchase_invoice_line_serials",
                columns: new[] { "tenant_id", "serial_number" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_line_taxes_component",
                table: "purchase_invoice_line_taxes",
                columns: new[] { "tenant_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_lines_invoice",
                table: "purchase_invoice_lines",
                columns: new[] { "purchase_invoice_id", "line_number" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_lines_product",
                table: "purchase_invoice_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_lines_tenant",
                table: "purchase_invoice_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_lines_unit_id",
                table: "purchase_invoice_lines",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_bill_id",
                table: "purchase_invoices",
                column: "bill_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_date",
                table: "purchase_invoices",
                columns: new[] { "firm_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_financial_year_id",
                table: "purchase_invoices",
                column: "financial_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_is_deleted",
                table: "purchase_invoices",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_journal_voucher_id",
                table: "purchase_invoices",
                column: "journal_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_number",
                table: "purchase_invoices",
                columns: new[] { "firm_id", "number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_returns_invoice",
                table: "purchase_invoices",
                column: "returns_invoice_id",
                filter: "returns_invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_stock_document_id",
                table: "purchase_invoices",
                column: "stock_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_supplier",
                table: "purchase_invoices",
                columns: new[] { "firm_id", "supplier_ledger_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_supplier_ledger_id",
                table: "purchase_invoices",
                column: "supplier_ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_supplier_number",
                table: "purchase_invoices",
                columns: new[] { "firm_id", "supplier_ledger_id", "supplier_invoice_number" },
                unique: true,
                filter: "supplier_invoice_number IS NOT NULL AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_tenant",
                table: "purchase_invoices",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_warehouse_id",
                table: "purchase_invoices",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_invoice_charges");

            migrationBuilder.DropTable(
                name: "purchase_invoice_line_serials");

            migrationBuilder.DropTable(
                name: "purchase_invoice_line_taxes");

            migrationBuilder.DropTable(
                name: "purchase_invoice_lines");

            migrationBuilder.DropTable(
                name: "purchase_invoices");
        }
    }
}
