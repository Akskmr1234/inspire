using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    customer_ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    reference_number = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    narration = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    posted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_sales_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_invoices_financial_years_financial_year_id",
                        column: x => x.financial_year_id,
                        principalTable: "financial_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoices_ledgers_customer_ledger_id",
                        column: x => x.customer_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoices_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_invoice_charges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_addition = table.Column<bool>(type: "boolean", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_invoice_charges", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_invoice_charges_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoice_charges_sales_invoices_sales_invoice_id",
                        column: x => x.sales_invoice_id,
                        principalTable: "sales_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_sales_invoice_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_sales_invoice_lines_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoice_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_invoice_lines_sales_invoices_sales_invoice_id",
                        column: x => x.sales_invoice_id,
                        principalTable: "sales_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sales_invoice_lines_units_of_measure_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_invoice_line_serials",
                columns: table => new
                {
                    sales_invoice_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_invoice_line_serials", x => new { x.sales_invoice_line_id, x.serial_number_id });
                    table.ForeignKey(
                        name: "fk_sales_invoice_line_serials_sales_invoice_lines_sales_invoic~",
                        column: x => x.sales_invoice_line_id,
                        principalTable: "sales_invoice_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_sales_invoice_line_serials_serial_numbers_serial_number_id",
                        column: x => x.serial_number_id,
                        principalTable: "serial_numbers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_invoice_line_taxes",
                columns: table => new
                {
                    sales_invoice_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    percentage = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_invoice_line_taxes", x => new { x.sales_invoice_line_id, x.type });
                    table.ForeignKey(
                        name: "fk_sales_invoice_line_taxes_sales_invoice_lines_sales_invoice_~",
                        column: x => x.sales_invoice_line_id,
                        principalTable: "sales_invoice_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_charges_ledger",
                table: "sales_invoice_charges",
                columns: new[] { "sales_invoice_id", "ledger_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_charges_ledger_id",
                table: "sales_invoice_charges",
                column: "ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_charges_tenant",
                table: "sales_invoice_charges",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_line_serials_serial",
                table: "sales_invoice_line_serials",
                column: "serial_number_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_line_serials_tenant",
                table: "sales_invoice_line_serials",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_line_taxes_component",
                table: "sales_invoice_line_taxes",
                columns: new[] { "tenant_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_lines_batch_id",
                table: "sales_invoice_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_lines_invoice",
                table: "sales_invoice_lines",
                columns: new[] { "sales_invoice_id", "line_number" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_lines_product",
                table: "sales_invoice_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_lines_tenant",
                table: "sales_invoice_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoice_lines_unit_id",
                table: "sales_invoice_lines",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_customer",
                table: "sales_invoices",
                columns: new[] { "firm_id", "customer_ledger_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_customer_ledger_id",
                table: "sales_invoices",
                column: "customer_ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_date",
                table: "sales_invoices",
                columns: new[] { "firm_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_financial_year_id",
                table: "sales_invoices",
                column: "financial_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_is_deleted",
                table: "sales_invoices",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_number",
                table: "sales_invoices",
                columns: new[] { "firm_id", "number" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_tenant",
                table: "sales_invoices",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_invoices_warehouse_id",
                table: "sales_invoices",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_invoice_charges");

            migrationBuilder.DropTable(
                name: "sales_invoice_line_serials");

            migrationBuilder.DropTable(
                name: "sales_invoice_line_taxes");

            migrationBuilder.DropTable(
                name: "sales_invoice_lines");

            migrationBuilder.DropTable(
                name: "sales_invoices");
        }
    }
}
