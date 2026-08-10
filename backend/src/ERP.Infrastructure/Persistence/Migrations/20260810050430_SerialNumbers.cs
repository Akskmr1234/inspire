using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SerialNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "serial_numbers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    warranty_until = table.Column<DateOnly>(type: "date", nullable: true),
                    received_on = table.Column<DateOnly>(type: "date", nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    origin_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serial_numbers", x => x.id);
                    table.ForeignKey(
                        name: "fk_serial_numbers_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serial_numbers_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serial_numbers_stock_documents_last_document_id",
                        column: x => x.last_document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serial_numbers_stock_documents_origin_document_id",
                        column: x => x.origin_document_id,
                        principalTable: "stock_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_serial_numbers_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_document_line_serials",
                columns: table => new
                {
                    stock_document_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_document_line_serials", x => new { x.stock_document_line_id, x.serial_number_id });
                    table.ForeignKey(
                        name: "fk_stock_document_line_serials_serial_numbers_serial_number_id",
                        column: x => x.serial_number_id,
                        principalTable: "serial_numbers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_document_line_serials_stock_document_lines_stock_docu~",
                        column: x => x.stock_document_line_id,
                        principalTable: "stock_document_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_available",
                table: "serial_numbers",
                columns: new[] { "firm_id", "product_id", "warehouse_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_batch_id",
                table: "serial_numbers",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_last_document_id",
                table: "serial_numbers",
                column: "last_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_lookup",
                table: "serial_numbers",
                columns: new[] { "firm_id", "number" });

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_number",
                table: "serial_numbers",
                columns: new[] { "firm_id", "product_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_origin_document_id",
                table: "serial_numbers",
                column: "origin_document_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_product_id",
                table: "serial_numbers",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_tenant",
                table: "serial_numbers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_serial_numbers_warehouse_id",
                table: "serial_numbers",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_line_serials_serial",
                table: "stock_document_line_serials",
                column: "serial_number_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_line_serials_tenant",
                table: "stock_document_line_serials",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_document_line_serials");

            migrationBuilder.DropTable(
                name: "serial_numbers");
        }
    }
}
