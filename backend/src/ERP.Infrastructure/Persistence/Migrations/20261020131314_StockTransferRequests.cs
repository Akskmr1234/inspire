using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockTransferRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_transit",
                table: "warehouses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "stock_transfer_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_year_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    from_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    narration = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    dispatch_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dispatched_by = table.Column<Guid>(type: "uuid", nullable: true),
                    dispatched_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    receipt_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_by = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_transfer_requests_financial_years_financial_year_id",
                        column: x => x.financial_year_id,
                        principalTable: "financial_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_requests_stock_documents_dispatch_document_id",
                        column: x => x.dispatch_document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_requests_stock_documents_receipt_document_id",
                        column: x => x.receipt_document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_requests_warehouses_from_warehouse_id",
                        column: x => x.from_warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_requests_warehouses_to_warehouse_id",
                        column: x => x.to_warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_request_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_transfer_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    stock_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    approved_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    dispatched_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_transfer_request_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_transfer_request_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_transfer_request_lines_stock_transfer_requests_stock_~",
                        column: x => x.stock_transfer_request_id,
                        principalTable: "stock_transfer_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_stock_transfer_request_lines_units_of_measure_unit_id",
                        column: x => x.unit_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_request_lines_product_id",
                table: "stock_transfer_request_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_request_lines_request",
                table: "stock_transfer_request_lines",
                columns: new[] { "stock_transfer_request_id", "line_number" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_request_lines_tenant",
                table: "stock_transfer_request_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_request_lines_unit_id",
                table: "stock_transfer_request_lines",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_requests_date",
                table: "stock_transfer_requests",
                columns: new[] { "firm_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_requests_dispatch",
                table: "stock_transfer_requests",
                column: "dispatch_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_requests_financial_year_id",
                table: "stock_transfer_requests",
                column: "financial_year_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_requests_from_warehouse_id",
                table: "stock_transfer_requests",
                column: "from_warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_requests_number",
                table: "stock_transfer_requests",
                columns: new[] { "firm_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_requests_receipt",
                table: "stock_transfer_requests",
                column: "receipt_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_requests_tenant",
                table: "stock_transfer_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfer_requests_to_warehouse_id",
                table: "stock_transfer_requests",
                column: "to_warehouse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_transfer_request_lines");

            migrationBuilder.DropTable(
                name: "stock_transfer_requests");

            migrationBuilder.DropColumn(
                name: "is_transit",
                table: "warehouses");
        }
    }
}
