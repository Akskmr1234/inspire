using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_party_photos_ledger",
                table: "party_photos");

            migrationBuilder.AddColumn<int>(
                name: "slot",
                table: "party_photos",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "product_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    firm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "character varying(700000)", maxLength: 700000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_photos_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_party_photos_ledger",
                table: "party_photos",
                columns: new[] { "ledger_id", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_photos_product",
                table: "product_photos",
                columns: new[] { "product_id", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_photos_tenant",
                table: "product_photos",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_photos");

            migrationBuilder.DropIndex(
                name: "ix_party_photos_ledger",
                table: "party_photos");

            migrationBuilder.DropColumn(
                name: "slot",
                table: "party_photos");

            migrationBuilder.CreateIndex(
                name: "ix_party_photos_ledger",
                table: "party_photos",
                column: "ledger_id",
                unique: true);
        }
    }
}
