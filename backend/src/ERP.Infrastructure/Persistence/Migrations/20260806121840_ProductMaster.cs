using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description_arabic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    short_description = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    item_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    manufacturer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    size = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    origin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    item_type = table.Column<int>(type: "integer", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    default_supplier_ledger_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    costing_method = table.Column<int>(type: "integer", nullable: false),
                    movement = table.Column<int>(type: "integer", nullable: false),
                    tracks_batches = table.Column<bool>(type: "boolean", nullable: false),
                    tracks_serial_numbers = table.Column<bool>(type: "boolean", nullable: false),
                    shelf_life_days = table.Column<int>(type: "integer", nullable: true),
                    is_packing = table.Column<bool>(type: "boolean", nullable: false),
                    rack = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_discontinued = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    battery = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    colour = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    device = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ram = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    storage = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    maximum_level = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    minimum_level = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    reorder_level = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    cor_percentage = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    maximum_retail_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    other_rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    profit_percentage = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    retail_rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    wholesale_rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_products_brands_brand_id",
                        column: x => x.brand_id,
                        principalTable: "brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_ledgers_default_supplier_ledger_id",
                        column: x => x.default_supplier_ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_units_of_measure_purchase_unit_id",
                        column: x => x.purchase_unit_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_units_of_measure_sales_unit_id",
                        column: x => x.sales_unit_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_products_units_of_measure_stock_unit_id",
                        column: x => x.stock_unit_id,
                        principalTable: "units_of_measure",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_barcodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    barcode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    cor_percentage = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    maximum_retail_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    other_rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    profit_percentage = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    retail_rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    wholesale_rate = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_barcodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_barcodes_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_barcodes_code",
                table: "product_barcodes",
                columns: new[] { "tenant_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_barcodes_product",
                table: "product_barcodes",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_brand_id",
                table: "products",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_category",
                table: "products",
                columns: new[] { "firm_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_products_category_id",
                table: "products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_code",
                table: "products",
                columns: new[] { "firm_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_default_supplier_ledger_id",
                table: "products",
                column: "default_supplier_ledger_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_description",
                table: "products",
                columns: new[] { "firm_id", "description" },
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_products_is_deleted",
                table: "products",
                column: "is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_products_purchase_unit_id",
                table: "products",
                column: "purchase_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_sales_unit_id",
                table: "products",
                column: "sales_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_stock_unit_id",
                table: "products",
                column: "stock_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant",
                table: "products",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_barcodes");

            migrationBuilder.DropTable(
                name: "products");
        }
    }
}
