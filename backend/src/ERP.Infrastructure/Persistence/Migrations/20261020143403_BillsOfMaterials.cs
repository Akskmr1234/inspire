using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BillsOfMaterials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bills_of_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    output_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    narration = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bills_of_materials", x => x.id);
                    table.ForeignKey(
                        name: "fk_bills_of_materials_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bill_of_materials_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bill_of_materials_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bill_of_materials_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_bill_of_materials_lines_bills_of_materials_bill_of_material~",
                        column: x => x.bill_of_materials_id,
                        principalTable: "bills_of_materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bill_of_materials_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bill_of_materials_lines_bill",
                table: "bill_of_materials_lines",
                columns: new[] { "bill_of_materials_id", "line_number" });

            migrationBuilder.CreateIndex(
                name: "ix_bill_of_materials_lines_product",
                table: "bill_of_materials_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_bill_of_materials_lines_tenant",
                table: "bill_of_materials_lines",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_materials_product",
                table: "bills_of_materials",
                columns: new[] { "firm_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_materials_product_id",
                table: "bills_of_materials",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_bills_of_materials_tenant",
                table: "bills_of_materials",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bill_of_materials_lines");

            migrationBuilder.DropTable(
                name: "bills_of_materials");
        }
    }
}
