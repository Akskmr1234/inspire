using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxSlabs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tax_slab_id",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tax_slabs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_arabic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    percentage = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_slabs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_tax_slab_id",
                table: "products",
                column: "tax_slab_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_slabs_code",
                table: "tax_slabs",
                columns: new[] { "firm_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tax_slabs_tenant",
                table: "tax_slabs",
                column: "tenant_id");

            migrationBuilder.AddForeignKey(
                name: "fk_products_tax_slabs_tax_slab_id",
                table: "products",
                column: "tax_slab_id",
                principalTable: "tax_slabs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_products_tax_slabs_tax_slab_id",
                table: "products");

            migrationBuilder.DropTable(
                name: "tax_slabs");

            migrationBuilder.DropIndex(
                name: "ix_products_tax_slab_id",
                table: "products");

            migrationBuilder.DropColumn(
                name: "tax_slab_id",
                table: "products");
        }
    }
}
