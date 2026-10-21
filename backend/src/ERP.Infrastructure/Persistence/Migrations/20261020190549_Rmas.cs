using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Rmas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rmas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    party_ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_reference = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sales_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purchase_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    narration = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    condition = table.Column<int>(type: "integer", nullable: true),
                    inspection_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    inspected_on = table.Column<DateOnly>(type: "date", nullable: true),
                    replacement_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: true),
                    debit_note_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rmas", x => x.id);
                    table.ForeignKey(
                        name: "fk_rmas_financial_years_financial_year_id",
                        column: x => x.financial_year_id,
                        principalTable: "financial_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rmas_ledgers_party_ledger_id",
                        column: x => x.party_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rmas_purchase_invoices_debit_note_id",
                        column: x => x.debit_note_id,
                        principalTable: "purchase_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rmas_purchase_invoices_purchase_invoice_id",
                        column: x => x.purchase_invoice_id,
                        principalTable: "purchase_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rmas_sales_invoices_credit_note_id",
                        column: x => x.credit_note_id,
                        principalTable: "sales_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rmas_sales_invoices_sales_invoice_id",
                        column: x => x.sales_invoice_id,
                        principalTable: "sales_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rmas_stock_documents_replacement_document_id",
                        column: x => x.replacement_document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rmas_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rma_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rma_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_rma_events_rmas_rma_id",
                        column: x => x.rma_id,
                        principalTable: "rmas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rma_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    serial_number = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rma_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_rma_lines_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rma_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rma_lines_rmas_rma_id",
                        column: x => x.rma_id,
                        principalTable: "rmas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rma_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rma_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "character varying(700000)", maxLength: 700000, nullable: false),
                    caption = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rma_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_rma_photos_rmas_rma_id",
                        column: x => x.rma_id,
                        principalTable: "rmas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rma_events_rma",
                table: "rma_events",
                columns: new[] { "rma_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_rma_events_tenant",
                table: "rma_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rma_lines_batch_id",
                table: "rma_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_rma_lines_product",
                table: "rma_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_rma_lines_rma",
                table: "rma_lines",
                columns: new[] { "rma_id", "line_number" });

            migrationBuilder.CreateIndex(
                name: "ix_rma_lines_serial",
                table: "rma_lines",
                column: "serial_number",
                filter: "serial_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rma_lines_tenant",
                table: "rma_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rma_photos_rma",
                table: "rma_photos",
                column: "rma_id");

            migrationBuilder.CreateIndex(
                name: "ix_rma_photos_tenant",
                table: "rma_photos",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rmas_credit_note_id",
                table: "rmas",
                column: "credit_note_id");

            migrationBuilder.CreateIndex(
                name: "ix_rmas_debit_note_id",
                table: "rmas",
                column: "debit_note_id");

            migrationBuilder.CreateIndex(
                name: "ix_rmas_financial_year_id",
                table: "rmas",
                column: "financial_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_rmas_number",
                table: "rmas",
                columns: new[] { "firm_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rmas_party",
                table: "rmas",
                columns: new[] { "firm_id", "party_ledger_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rmas_party_ledger_id",
                table: "rmas",
                column: "party_ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_rmas_purchase_invoice_id",
                table: "rmas",
                column: "purchase_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_rmas_replacement_document_id",
                table: "rmas",
                column: "replacement_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_rmas_sales_invoice_id",
                table: "rmas",
                column: "sales_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_rmas_tenant",
                table: "rmas",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_rmas_warehouse_id",
                table: "rmas",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rma_events");

            migrationBuilder.DropTable(
                name: "rma_lines");

            migrationBuilder.DropTable(
                name: "rma_photos");

            migrationBuilder.DropTable(
                name: "rmas");
        }
    }
}
