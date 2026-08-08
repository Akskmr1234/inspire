using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BatchTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "batch_id",
                table: "stock_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "batch_number",
                table: "stock_ledger_entries",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "batch_id",
                table: "stock_document_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    auto_sequence = table.Column<int>(type: "integer", nullable: true),
                    manufactured_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    purchase_rate = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_batches_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "batch_balances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(19,6)", precision: 19, scale: 6, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    last_movement_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_balances", x => x.id);
                    table.ForeignKey(
                        name: "fk_batch_balances_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_balances_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_balances_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_batch",
                table: "stock_ledger_entries",
                columns: new[] { "firm_id", "batch_id", "posted_at_utc" },
                filter: "batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entries_batch_id",
                table: "stock_ledger_entries",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_document_lines_batch_id",
                table: "stock_document_lines",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_balances_batch_id",
                table: "batch_balances",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_balances_position",
                table: "batch_balances",
                columns: new[] { "firm_id", "batch_id", "warehouse_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_batch_balances_product",
                table: "batch_balances",
                columns: new[] { "firm_id", "product_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_batch_balances_product_id",
                table: "batch_balances",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_balances_tenant",
                table: "batch_balances",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_batch_balances_warehouse_id",
                table: "batch_balances",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_expiry",
                table: "batches",
                columns: new[] { "firm_id", "expires_on" },
                filter: "expires_on IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_batches_number",
                table: "batches",
                columns: new[] { "firm_id", "product_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_batches_product_id",
                table: "batches",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_sequence",
                table: "batches",
                columns: new[] { "firm_id", "product_id", "auto_sequence" },
                filter: "auto_sequence IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_batches_tenant",
                table: "batches",
                column: "tenant_id");

            migrationBuilder.AddForeignKey(
                name: "fk_stock_document_lines_batches_batch_id",
                table: "stock_document_lines",
                column: "batch_id",
                principalTable: "batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_ledger_entries_batches_batch_id",
                table: "stock_ledger_entries",
                column: "batch_id",
                principalTable: "batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_stock_document_lines_batches_batch_id",
                table: "stock_document_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_ledger_entries_batches_batch_id",
                table: "stock_ledger_entries");

            migrationBuilder.DropTable(
                name: "batch_balances");

            migrationBuilder.DropTable(
                name: "batches");

            migrationBuilder.DropIndex(
                name: "ix_stock_ledger_batch",
                table: "stock_ledger_entries");

            migrationBuilder.DropIndex(
                name: "ix_stock_ledger_entries_batch_id",
                table: "stock_ledger_entries");

            migrationBuilder.DropIndex(
                name: "ix_stock_document_lines_batch_id",
                table: "stock_document_lines");

            migrationBuilder.DropColumn(
                name: "batch_id",
                table: "stock_ledger_entries");

            migrationBuilder.DropColumn(
                name: "batch_number",
                table: "stock_ledger_entries");

            migrationBuilder.DropColumn(
                name: "batch_id",
                table: "stock_document_lines");
        }
    }
}
