using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_cards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_ledger_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    mobile = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    alternate_mobile = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    email = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reference_number = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    payment_mode = table.Column<int>(type: "integer", nullable: false),
                    salesman_ledger_id = table.Column<Guid>(type: "uuid", nullable: true),
                    technician_ledger_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    brand_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    model_number = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    colour = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    serial_number = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    accessories = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    service_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    fault = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    problem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    estimated_on = table.Column<DateOnly>(type: "date", nullable: true),
                    estimated_time = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    estimated_charge = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    processing_on = table.Column<DateOnly>(type: "date", nullable: true),
                    ready_on = table.Column<DateOnly>(type: "date", nullable: true),
                    not_ready_on = table.Column<DateOnly>(type: "date", nullable: true),
                    informed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    delivered_on = table.Column<DateOnly>(type: "date", nullable: true),
                    dead_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    cancellation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sales_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_cards_financial_years_financial_year_id",
                        column: x => x.financial_year_id,
                        principalTable: "financial_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_cards_ledgers_customer_ledger_id",
                        column: x => x.customer_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_cards_ledgers_salesman_ledger_id",
                        column: x => x.salesman_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_cards_ledgers_technician_ledger_id",
                        column: x => x.technician_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_cards_sales_invoices_sales_invoice_id",
                        column: x => x.sales_invoice_id,
                        principalTable: "sales_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_cards_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_card_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_card_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_job_card_lines_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_job_card_lines_job_cards_job_card_id",
                        column: x => x.job_card_id,
                        principalTable: "job_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_job_card_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_card_lines_batch_id",
                table: "job_card_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_card_lines_card",
                table: "job_card_lines",
                columns: new[] { "job_card_id", "line_number" });

            migrationBuilder.CreateIndex(
                name: "ix_job_card_lines_product",
                table: "job_card_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_card_lines_tenant",
                table: "job_card_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_cards_customer_ledger_id",
                table: "job_cards",
                column: "customer_ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_cards_financial_year_id",
                table: "job_cards",
                column: "financial_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_cards_invoice",
                table: "job_cards",
                column: "sales_invoice_id",
                filter: "sales_invoice_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_job_cards_mobile",
                table: "job_cards",
                columns: new[] { "firm_id", "mobile" });

            migrationBuilder.CreateIndex(
                name: "ix_job_cards_number",
                table: "job_cards",
                columns: new[] { "firm_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_cards_salesman_ledger_id",
                table: "job_cards",
                column: "salesman_ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_cards_technician_ledger_id",
                table: "job_cards",
                column: "technician_ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_cards_tenant",
                table: "job_cards",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_job_cards_warehouse_id",
                table: "job_cards",
                column: "warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_card_lines");

            migrationBuilder.DropTable(
                name: "job_cards");
        }
    }
}
